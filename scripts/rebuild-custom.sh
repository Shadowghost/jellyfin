#!/usr/bin/env bash
#
# Rebuild the `custom` integration branch from scratch on top of `master`.
#
# `custom` carries no unique work of its own beyond a handful of commits that
# live nowhere else (see CHERRY_PICKS). Everything else is a topic branch that
# is merged in. Rebuilding it periodically keeps the merge history flat and
# drops merges of branches that meanwhile landed upstream.
#
# The rebuild runs in three stages, in this order:
#   1. PRE_12X_BRANCHES   -- topics intended to be merged into master before
#                            the 12.x release (bug fixes, PR-ready work).
#   2. POST_12X_BRANCHES  -- topics that can only land after 12.x (new
#                            dependencies, feature work, infrastructure churn).
#   3. CHERRY_PICKS       -- commits that only ever existed on `custom`, applied
#                            last so they sit on top of every merged branch.
#
# Everything is resolved up front, so a typo in a branch name aborts the run
# before the old `custom` is touched. The previous tip is always saved to
# refs/backup/custom/<timestamp> first, which also keeps the cherry-picked
# commits reachable.
#
# Usage:
#   scripts/rebuild-custom.sh [--onto <ref>] [--dry-run] [--fetch] [--resume]
#
#   --onto <ref>  base to rebuild onto (default: master)
#   --dry-run     print the plan, touch nothing
#   --fetch       `git fetch --all --prune` before resolving refs
#   --resume      continue an interrupted run: skip the reset, and skip every
#                 merge/pick already contained in HEAD. Use this after
#                 resolving a conflict and committing it.
#   --list-picks  print the commits made directly on the current branch, in the
#                 form CHERRY_PICKS expects
#
# On a conflict the script stops and leaves the merge or cherry-pick in place.
# Resolve it, `git commit`, then re-run with --resume.
#
# All merges and picks run with `git rerere` enabled, so every conflict you
# resolve by hand is recorded and replayed automatically the next time this
# script hits the same conflict. Branches that only ever merged cleanly into
# `custom` incrementally (nearer merge bases) can conflict when replayed onto a
# fresh master -- expect to resolve those once, then never again.

set -euo pipefail

# --- Configuration -----------------------------------------------------------
# Branch names are resolved against local heads first, then origin/<name>, and
# always at their current tip -- no SHAs are pinned, so a rebased topic branch
# is picked up automatically.

# Stage 1: meant to be merged into master before 12.x.
# Landed upstream as of master f898c35b96 and dropped from this list:
#   fix-code-migration, fix-scan-memory-leak, fix-tmdb-recommendations,
#   fix-unordered-multiepisode-nfo, optimize-db,
#   fix-refresh-queue-and-directory-cache
PRE_12X_BRANCHES=(
    plugin-install-fixes
    fix-byname
    fix-tmdb-url
)

# Stage 2: only mergeable into master after 12.x.
POST_12X_BRANCHES=(
    slnx
    libvips
    port-async-migration-routines
    directory-cache-bitfaster
    fix-nested-boxsets
    extend-studio-plugin
    open-telemetry
)

# Stage 3: commits that exist only on `custom`, applied in this order.
# These point at the most recently rebuilt incarnation of each commit, i.e. the
# version already resolved against the current master -- not the original from
# an older `custom`. After a rebuild, refresh them with:
#   scripts/rebuild-custom.sh --list-picks
CHERRY_PICKS=(
    1ff4d2a1d1  # Add SDK regen script
    44f1124784  # Supress websocket exception ons
    6a76caf1c5  # Migrate the TMDb provider to the local TMDbLib and its async paging API
    1b2db6d4e2  # Create and re-sync local collections from TMDb lists
    # Dropped: db9af9ebf0 and 654a74b898 ("Fixup") now apply to nothing -- their
    # content reached plugin-install-fixes' current tip.
)

TARGET_BRANCH="${TARGET_BRANCH:-custom}"

# --- Arguments ---------------------------------------------------------------
BASE="master"
DRY_RUN=0
DO_FETCH=0
RESUME=0
LIST_PICKS=0

while [ $# -gt 0 ]; do
    case "$1" in
        --onto)       BASE="${2:?--onto needs a ref}"; shift 2 ;;
        --dry-run)    DRY_RUN=1; shift ;;
        --fetch)      DO_FETCH=1; shift ;;
        --resume)     RESUME=1; shift ;;
        --list-picks) LIST_PICKS=1; shift ;;
        -h|--help)    sed -n '2,/^set -euo/p' "$0" | sed 's/^# \{0,1\}//; $d'; exit 0 ;;
        *)            printf 'Unknown argument: %s\n' "$1" >&2; exit 2 ;;
    esac
done

log()  { printf '\n\033[1;34m==>\033[0m %s\n' "$*"; }
info() { printf '    %s\n' "$*"; }
warn() { printf '\033[1;33mWarning:\033[0m %s\n' "$*" >&2; }
die()  { printf '\n\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

# --- Sanity checks -----------------------------------------------------------
git rev-parse --git-dir >/dev/null 2>&1 || die "not inside a git repository"
cd "$(git rev-parse --show-toplevel)"

# --- --list-picks: recompute the stage 3 list from the current branch --------
# Every non-merge commit on the first-parent line of `custom` is a commit that
# was made directly on the branch, i.e. exactly what stage 3 has to replay.
if [ "$LIST_PICKS" -eq 1 ]; then
    log "Commits made directly on $TARGET_BRANCH since $BASE (oldest first):"
    git log --first-parent --no-merges --reverse \
        --format='    %h  # %s' "$BASE..$TARGET_BRANCH"
    exit 0
fi

GIT_DIR="$(git rev-parse --git-dir)"
for state in MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD rebase-merge rebase-apply; do
    [ -e "$GIT_DIR/$state" ] && die "an operation is already in progress ($state); finish or abort it first"
done
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
    die "working tree has uncommitted changes; commit or stash them first"
fi

if [ "$DO_FETCH" -eq 1 ]; then
    log "Fetching all remotes..."
    git fetch --all --prune
fi

# --- Resolve every ref before touching anything ------------------------------
# resolve_branch <name> -> prints "<sha> <ref>" or fails
resolve_branch() {
    local name="$1" ref
    for ref in "refs/heads/$name" "refs/remotes/origin/$name"; do
        if git show-ref --verify --quiet "$ref"; then
            printf '%s %s\n' "$(git rev-parse "$ref")" "${ref#refs/}"
            return 0
        fi
    done
    return 1
}

BASE_SHA="$(git rev-parse --verify "$BASE^{commit}" 2>/dev/null)" \
    || die "base ref not found: $BASE"

PRE_SHAS=();  PRE_REFS=()
POST_SHAS=(); POST_REFS=()
PICK_SHAS=(); PICK_SUBJECTS=()
MISSING=()
LANDED=()

# note_landed <sha> <ref>: record branches whose work is already in the base.
note_landed() {
    git merge-base --is-ancestor "$1" "$BASE_SHA" 2>/dev/null && LANDED+=("$2")
    return 0
}

for b in "${PRE_12X_BRANCHES[@]}"; do
    if out="$(resolve_branch "$b")"; then
        PRE_SHAS+=("${out%% *}"); PRE_REFS+=("${out#* }")
        note_landed "${out%% *}" "${out#* }"
    else
        MISSING+=("branch: $b")
    fi
done
for b in "${POST_12X_BRANCHES[@]}"; do
    if out="$(resolve_branch "$b")"; then
        POST_SHAS+=("${out%% *}"); POST_REFS+=("${out#* }")
        note_landed "${out%% *}" "${out#* }"
    else
        MISSING+=("branch: $b")
    fi
done
for c in "${CHERRY_PICKS[@]}"; do
    if sha="$(git rev-parse --verify --quiet "$c^{commit}")"; then
        PICK_SHAS+=("$sha")
        PICK_SUBJECTS+=("$(git log -1 --format=%s "$sha")")
    else
        MISSING+=("commit: $c")
    fi
done

if [ "${#MISSING[@]}" -gt 0 ]; then
    printf '\n\033[1;31mError:\033[0m unresolvable refs:\n' >&2
    printf '    %s\n' "${MISSING[@]}" >&2
    die "nothing was changed"
fi

# --- Show the plan -----------------------------------------------------------
log "Rebuild plan for '$TARGET_BRANCH' onto $BASE ($(git rev-parse --short "$BASE_SHA"))"
printf '\n  Stage 1 - merge, meant for master before 12.x:\n'
for i in "${!PRE_REFS[@]}"; do
    printf '    %-12s %s\n' "$(git rev-parse --short "${PRE_SHAS[$i]}")" "${PRE_REFS[$i]}"
done
printf '\n  Stage 2 - merge, only mergeable after 12.x:\n'
for i in "${!POST_REFS[@]}"; do
    printf '    %-12s %s\n' "$(git rev-parse --short "${POST_SHAS[$i]}")" "${POST_REFS[$i]}"
done
printf '\n  Stage 3 - cherry-pick, %s-only commits:\n' "$TARGET_BRANCH"
for i in "${!PICK_SHAS[@]}"; do
    printf '    %-12s %s\n' "$(git rev-parse --short "${PICK_SHAS[$i]}")" "${PICK_SUBJECTS[$i]}"
done
if [ "${#LANDED[@]}" -gt 0 ]; then
    printf '\n  Already contained in %s -- drop from the lists above:\n' "$BASE"
    printf '    %s\n' "${LANDED[@]}"
fi
printf '\n'

if [ "$DRY_RUN" -eq 1 ]; then
    log "Dry run: stopping before making any change."
    exit 0
fi

# --- Back up and reset -------------------------------------------------------
if [ "$RESUME" -eq 1 ]; then
    [ "$(git rev-parse --abbrev-ref HEAD)" = "$TARGET_BRANCH" ] \
        || die "--resume expects $TARGET_BRANCH to be checked out"
    log "Resuming on $TARGET_BRANCH at $(git rev-parse --short HEAD); completed steps will be skipped."
else
    if OLD="$(git rev-parse --verify --quiet "refs/heads/$TARGET_BRANCH")"; then
        BACKUP="refs/backup/$TARGET_BRANCH/$(date +%Y%m%d-%H%M%S)"
        git update-ref "$BACKUP" "$OLD"
        log "Saved previous $TARGET_BRANCH ($(git rev-parse --short "$OLD")) to $BACKUP"
        info "restore with: git update-ref refs/heads/$TARGET_BRANCH $BACKUP"
    fi
    log "Resetting $TARGET_BRANCH to $BASE"
    git checkout -B "$TARGET_BRANCH" "$BASE_SHA"
fi

# --- Helpers -----------------------------------------------------------------
# git rerere records conflict resolutions so the next rebuild replays them.
GIT_RR=(git -c rerere.enabled=true -c rerere.autoupdate=true)

# git's own output for the step in flight; printed only if the step fails, so a
# recovered step (rerere replay, empty pick) stays quiet.
GIT_OUT="$(mktemp)"
trap 'rm -f "$GIT_OUT"' EXIT

contains() { git merge-base --is-ancestor "$1" HEAD 2>/dev/null; }

# True when the failed operation left no unresolved paths behind, i.e. rerere
# replayed a recorded resolution for every conflict and staged it.
fully_resolved() { [ -z "$(git diff --name-only --diff-filter=U)" ]; }

# Number of picks already processed in this run, per subject line.
declare -A SUBJ_SEEN=()
# Picks that turned out to apply to nothing, reported at the end.
EMPTY_PICKS=()

# pick_applied <sha> <subject> <seen>: true when this pick is already in HEAD.
pick_applied() {
    local sha="$1" subject="$2" seen="$3" have
    # Identical patch: `git cherry` marks an equivalent commit with a leading '-'.
    if [ "$(git cherry HEAD "$sha" "$sha^" 2>/dev/null | cut -c1)" = "-" ]; then
        return 0
    fi
    # A pick that needed conflict resolution has a different patch id, so the
    # test above misses it. cherry-pick preserves the subject line, so fall back
    # to matching that -- but only against the first-parent, non-merge commits
    # since the base, which is exactly stage 3. Without that restriction a
    # generic subject like "Fixup" matches a same-named commit inside a merged
    # branch and the pick is skipped for the wrong reason. <seen> counts earlier
    # picks in this run sharing the subject, so a repeated one is not swallowed
    # by the first.
    have="$(git log --first-parent --no-merges --format=%s "$BASE_SHA..HEAD" \
            | grep -Fxc "$subject" || true)"
    [ "$have" -gt "$seen" ]
}

conflict_stop() {
    printf '\n\033[1;31mConflict:\033[0m %s\n' "$1" >&2
    [ -s "$GIT_OUT" ] && sed 's/^/    /' "$GIT_OUT" >&2
    git --no-pager diff --name-only --diff-filter=U | sed 's/^/    /' >&2
    cat >&2 <<EOF

The failing operation was left in place. To continue:
    # resolve the conflicts, then
    git add -A && git commit          # or: git cherry-pick --continue
    $0 --resume --onto $BASE

Your resolution is recorded by rerere and replayed on the next rebuild.

To give up instead:
    git merge --abort   # or: git cherry-pick --abort
    git update-ref refs/heads/$TARGET_BRANCH <backup ref shown above>
EOF
    exit 1
}

merge_branch() {
    local sha="$1" ref="$2" name msg
    name="${ref#heads/}"
    if contains "$sha"; then
        info "already merged, skipping: $ref"
        return 0
    fi
    case "$ref" in
        remotes/*) msg="Merge remote-tracking branch '${ref#remotes/}' into $TARGET_BRANCH" ;;
        *)         msg="Merge branch '$name' into $TARGET_BRANCH" ;;
    esac
    info "merging $ref ($(git rev-parse --short "$sha"))"
    if ! "${GIT_RR[@]}" merge --no-ff --no-edit -m "$msg" "$sha" >"$GIT_OUT" 2>&1; then
        fully_resolved || conflict_stop "merge of $ref failed"
        info "  conflicts replayed from rerere, committing"
        git commit --no-edit -q || conflict_stop "merge of $ref failed"
    fi
}

# --- Stage 1: pre-12.x branches ---------------------------------------------
log "Stage 1: merging branches meant for master before 12.x"
for i in "${!PRE_REFS[@]}"; do
    merge_branch "${PRE_SHAS[$i]}" "${PRE_REFS[$i]}"
done

# --- Stage 2: post-12.x branches --------------------------------------------
log "Stage 2: merging branches that can only land after 12.x"
for i in "${!POST_REFS[@]}"; do
    merge_branch "${POST_SHAS[$i]}" "${POST_REFS[$i]}"
done

# --- Stage 3: cherry-picks ---------------------------------------------------
log "Stage 3: cherry-picking ${TARGET_BRANCH}-only commits"
for i in "${!PICK_SHAS[@]}"; do
    sha="${PICK_SHAS[$i]}"
    subject="${PICK_SUBJECTS[$i]}"
    seen="${SUBJ_SEEN[$subject]:-0}"
    SUBJ_SEEN[$subject]=$((seen + 1))
    if pick_applied "$sha" "$subject" "$seen"; then
        info "already applied, skipping: $(git rev-parse --short "$sha") $subject"
        continue
    fi
    info "picking $(git rev-parse --short "$sha") $subject"
    if ! "${GIT_RR[@]}" cherry-pick "$sha" >"$GIT_OUT" 2>&1; then
        failed="cherry-pick of $(git rev-parse --short "$sha") ($subject) failed"
        if ! fully_resolved; then
            conflict_stop "$failed"
        elif git diff --quiet --cached HEAD; then
            # The pick applied to nothing: its content already reached the tree
            # through a merged branch or through master. Drop it rather than
            # committing an empty commit, and flag it for removal from the list.
            info "  applies to nothing here, skipping (drop it from CHERRY_PICKS)"
            EMPTY_PICKS+=("$(git rev-parse --short "$sha")  $subject")
            git cherry-pick --skip >"$GIT_OUT" 2>&1 || conflict_stop "$failed"
        else
            info "  conflicts replayed from rerere, continuing"
            GIT_EDITOR=true git cherry-pick --continue >"$GIT_OUT" 2>&1 \
                || conflict_stop "$failed"
        fi
    fi
done

# --- Summary -----------------------------------------------------------------
log "Rebuilt $TARGET_BRANCH at $(git rev-parse --short HEAD)"
git --no-pager log --first-parent --oneline "$BASE_SHA..HEAD" | sed 's/^/    /'
printf '\n'
info "$(git rev-list --count "$BASE_SHA..HEAD") commits on top of $BASE"
if [ "${#EMPTY_PICKS[@]}" -gt 0 ]; then
    printf '\n'
    warn "these picks applied to nothing and were skipped; drop them from CHERRY_PICKS:"
    printf '    %s\n' "${EMPTY_PICKS[@]}" >&2
fi
if git rev-parse --verify --quiet "refs/remotes/origin/$TARGET_BRANCH" >/dev/null; then
    warn "origin/$TARGET_BRANCH now needs a force push: git push --force-with-lease origin $TARGET_BRANCH"
fi
