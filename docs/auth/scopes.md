# Endpoint Scopes

Scopes gate access to a family of endpoints. Tokens carry one or more `jellyfin:scope` claims; the
`full` scope satisfies every requirement. Scopes are distinct from per-user `PermissionKind`
capabilities — a token's scopes can only *narrow* what its user could already do.

## Catalog (`Jellyfin.Api/Constants/EndpointScopes.cs`)

| Scope | Meaning |
|---|---|
| `full` | Unrestricted; default for full-session JWTs. |
| `media.stream` | Audio/video stream endpoints. |
| `media.hls` | HLS segment endpoints. |
| `media.image` | Image endpoints. |
| `media.subtitle` | Subtitle endpoints. |
| `metadata.read` | Read-only library/item metadata. |
| `dlna` | DLNA/UPnP endpoints. |
| `quickconnect` | Quick Connect endpoints. |

## Enforcement

`scope:<name>` authorization policies are registered for every entry in `EndpointScopes.All` and
enforced by `ScopeAuthorizationHandler`:

- A principal with **no** scope claims (legacy token, or a full session) is **unrestricted** — this
  preserves backwards compatibility.
- A principal **with** scope claims must hold the required scope or `full`.
- If the token carries a bound item id (`jellyfin:iid`), the route's `itemId` must match.

## Adding a scope

1. Add a constant to `EndpointScopes` and include it in `EndpointScopes.All`.
2. Tag endpoints with `[Authorize(Policy = EndpointScopes.PolicyFor(EndpointScopes.MyScope))]`.
3. If it maps to a temp-token-grantable capability, it is automatically grantable (only `full` and
   non-catalog scopes are rejected by `/Auth/TempTokens`).

## Endpoint audit

Every controller action must carry an explicit `[Authorize]` or `[AllowAnonymous]` — enforced by the
`EndpointAuthAuditTests` build fence. `endpoint-scope-audit.csv` records the previously-anonymous
endpoints and which are candidates for future scope-protection.
