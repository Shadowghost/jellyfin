# Authentication & Authorization Architecture

Jellyfin uses the standard ASP.NET Core authentication pipeline with multiple schemes selected
per request.

## Request flow

```
HTTP request
  └─ UseAuthentication
       └─ JellyfinSelector (policy scheme)  ── ForwardDefaultSelector ─┐
                                                                       ▼
            JellyfinSchemeSelector.Select(HttpContext) picks one of:
              • Authorization: Bearer pt_*    → PluginToken (plugin-owned opaque tokens)
              • Authorization: Bearer <jwt>   → JellyfinJwt
              • ?token=<jwt>                  → JellyfinTempJwt
              • everything else               → LegacyMediaBrowserToken (transitional)
       └─ IClaimsTransformation: JellyfinClaimsTransformation
            normalizes the principal into internal claims (UserId, role, scopes)
  └─ UseAuthorization
       └─ policies + handlers (Default, UserPermission, Scope, FirstTimeSetup, …)
```

## Schemes (`AuthenticationSchemes`)

- **JellyfinSelector** (`"Jellyfin"`) — the policy scheme; every authorization policy authenticates
  through it. It forwards to a concrete scheme via `JellyfinSchemeSelector`.
- **JellyfinJwt** — full-session Jellyfin-issued JWT (`aud=urn:jellyfin:api`), carries `scope=full`.
- **JellyfinTempJwt** — short-lived scoped token (`aud=urn:jellyfin:temp`), accepted via the
  `?token=` query parameter, revocation-checked, lifetime-capped at 1h.
- **LegacyMediaBrowserToken** (`"CustomAuthentication"`) — transitional opaque device/api-key tokens.
- **PluginToken** — a single scheme fronting all authentication plugins; tokens are prefixed
  `pt_{pluginId}_` and validated by the owning `IAuthenticationPlugin`
  (`PluginAuthenticationHandler` + `AuthenticationPluginRegistry`). See
  [plugin-authoring.md](plugin-authoring.md).

## JWT issuance

`IJellyfinJwtIssuer` (impl in `Jellyfin.Server/Auth/JellyfinJwtIssuer.cs`) signs tokens with the
symmetric key from `IJellyfinSigningKeyProvider` (persisted in `auth.xml`, `0600`, current+previous
for rotation). Sessions are issued on login by `SessionManager`; temp tokens by the
`/Auth/TempTokens` endpoint.

## Claims

`JellyfinClaimsTransformation` resolves the user from the token and stamps `InternalClaimTypes.UserId`,
`ClaimTypes.Name`, `ClaimTypes.Role` (live from the DB), and scope claims. Legacy principals are
populated during authentication and pass through unchanged.

## Authorization

Policies live in `MediaBrowser.Common/Api/Policies.cs`. Scope policies (`scope:<name>`) are backed by
`ScopeAuthorizationHandler`: unscoped principals (legacy tokens, full sessions) are unrestricted;
scoped tokens must hold the required (or `full`) scope, and item-bound tokens may only act on the
bound item. See [scopes.md](scopes.md).

## Telemetry

`AuthMetrics` (`Jellyfin.Api/Auth/AuthMetrics.cs`) exposes prometheus-net counters, surfaced at
`/metrics` when `ServerConfiguration.EnableMetrics` is true:

| Metric | Labels |
|---|---|
| `jellyfin_authentication_attempts_total` | `scheme` (jwt/temp/legacy/plugin), `result` (success/failure) |
| `jellyfin_authentication_logins_total` | `result` (success/failure) |
| `jellyfin_authentication_temp_tokens_issued_total` | — |
| `jellyfin_authentication_temp_tokens_revoked_total` | — |

Per-request attempts are recorded by the scheme handlers (`CustomAuthenticationHandler`,
`ConfigureJellyfinJwtBearerOptions`, `PluginAuthenticationHandler`); interactive logins by
`AuthLoginMetricsConsumer` (consuming the existing authentication events); temp-token issue/revoke by
`AuthController`.
