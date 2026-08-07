#!/usr/bin/env bash
#
# Regenerate the Jellyfin OpenAPI spec from the current server code, rebuild the
# TypeScript SDK from it, and install the freshly built SDK into jellyfin-web.
#
# Pipeline:
#   1. Generate openapi.json by running the OpenApiSpecTests integration test.
#   2. Copy the spec into the SDK repo.
#   3. Rebuild the SDK (fix-schema + generate client + bundle).
#   4. Pack the SDK into a tarball (lib/ only, no bundled axios).
#   5. Install the tarball into jellyfin-web and type-check it.
#
# The tarball install (rather than a `file:` symlink) is deliberate: a symlinked
# SDK pulls in its own copy of the `axios` peer dependency, which makes the web
# project's TypeScript see two incompatible axios type identities. The packed
# tarball ships only lib/, so axios resolves to the web project's single copy --
# exactly like the published npm package.
#
# Paths can be overridden via environment variables:
#   SERVER_DIR  (default: the jellyfin repo containing this script)
#   SDK_DIR     (default: ../jellyfin-sdk-typescript)
#   WEB_DIR     (default: ../jellyfin-web)
#   CONFIG      dotnet build configuration (default: Release)

set -euo pipefail

# --- Resolve paths -----------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVER_DIR="${SERVER_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
REPOS_DIR="$(cd "$SERVER_DIR/.." && pwd)"
SDK_DIR="${SDK_DIR:-$REPOS_DIR/jellyfin-sdk-typescript}"
WEB_DIR="${WEB_DIR:-$REPOS_DIR/jellyfin-web}"
CONFIG="${CONFIG:-Release}"

TEST_PROJ="$SERVER_DIR/tests/Jellyfin.Server.Integration.Tests/Jellyfin.Server.Integration.Tests.csproj"
TEST_FILTER="Jellyfin.Server.Integration.Tests.OpenApiSpecTests"

log()  { printf '\n\033[1;34m==>\033[0m %s\n' "$*"; }
die()  { printf '\n\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

# --- Sanity checks -----------------------------------------------------------
[ -f "$TEST_PROJ" ] || die "Integration test project not found: $TEST_PROJ"
[ -d "$SDK_DIR" ]   || die "SDK repo not found: $SDK_DIR (override with SDK_DIR=...)"
[ -d "$WEB_DIR" ]   || die "Web repo not found: $WEB_DIR (override with WEB_DIR=...)"
command -v dotnet >/dev/null || die "dotnet is not installed"
command -v npm    >/dev/null || die "npm is not installed"
command -v java   >/dev/null || die "java is not installed (required by openapi-generator)"

# --- 1. Generate the OpenAPI spec -------------------------------------------
log "Generating OpenAPI spec from current server code ($CONFIG)..."
dotnet test "$TEST_PROJ" -c "$CONFIG" --filter "$TEST_FILTER"

SPEC="$(find "$SERVER_DIR/tests/Jellyfin.Server.Integration.Tests/bin/$CONFIG" \
            -name openapi.json -print 2>/dev/null | head -n1)"
[ -n "$SPEC" ] && [ -f "$SPEC" ] || die "Generated openapi.json not found under bin/$CONFIG"
log "Spec generated: $SPEC ($(wc -c < "$SPEC" | tr -d ' ') bytes)"

# --- 2. Copy the spec into the SDK repo -------------------------------------
log "Copying spec into SDK repo..."
cp "$SPEC" "$SDK_DIR/openapi.json"

# --- 3. Rebuild the SDK ------------------------------------------------------
if [ ! -d "$SDK_DIR/node_modules" ]; then
    log "Installing SDK dependencies (node_modules missing)..."
    ( cd "$SDK_DIR" && npm install )
fi

log "Fixing schema and rebuilding the SDK..."
( cd "$SDK_DIR" && npm run fix-schema && npm run build )

# --- 4. Pack the SDK ---------------------------------------------------------
log "Packing the SDK into a tarball..."
# npm pack prints notices to stderr; the tarball filename is the last stdout line.
TARBALL_NAME="$( cd "$SDK_DIR" && npm pack 2>/dev/null | tail -n1 )"
TARBALL="$SDK_DIR/$TARBALL_NAME"
[ -f "$TARBALL" ] || die "npm pack did not produce a tarball ($TARBALL)"
log "Created: $TARBALL"

# --- 5. Install into jellyfin-web and type-check -----------------------------
log "Installing the SDK tarball into jellyfin-web..."
( cd "$WEB_DIR" && npm install "$TARBALL" )

log "Type-checking jellyfin-web (tsc --noEmit)..."
( cd "$WEB_DIR" && npm run build:check )

log "Done. jellyfin-web now uses the freshly built @jellyfin/sdk."
printf '\n\033[1;33mNote:\033[0m jellyfin-web/package.json now references the local tarball\n'
printf '      (file:%s).\n' "$TARBALL"
printf '      Revert that change before committing the web repo.\n'
