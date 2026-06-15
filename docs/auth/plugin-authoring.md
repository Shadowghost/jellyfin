# Authoring an Authentication Plugin (`IAuthenticationPlugin`)

A plugin owns an authentication flow end-to-end: it authenticates against whatever backend it targets
(LDAP, OIDC, OAuth2, forward-auth headers, a custom HTTP API), returns an **opaque token of its own
choosing**, and validates that token on every request. Jellyfin never speaks to the external backend
itself.

Implement `MediaBrowser.Controller.Authentication.IAuthenticationPlugin` and register it in DI from your
plugin (the standard plugin loader picks it up).

## The contract

```csharp
public interface IAuthenticationPlugin
{
    string Id { get; }                                   // tokens are prefixed "pt_{Id}_"
    string DisplayName { get; }                          // shown in the login provider picker
    bool Enabled { get; }                                // from your plugin config
    PluginAuthenticationCapabilities Capabilities { get; } // UsernamePassword | UsernameOnly | HeaderTrust

    Task<PluginAuthenticationResult> AuthenticateAsync(PluginAuthenticationRequest request, CancellationToken ct);
    Task<PluginTokenValidationResult> ValidateTokenAsync(string opaqueToken, CancellationToken ct);
    Task RevokeAsync(string opaqueToken, CancellationToken ct);
}
```

- **`AuthenticateAsync`** — one call per login. Authenticate, then return `Success`, an `OpaqueToken`
  (any string you like; the server prefixes it `pt_{Id}_` before handing it to the client), an
  `ExpiresAt`, and a `PluginUserIdentity`.
- **`ValidateTokenAsync`** — called on *every* authenticated request for your tokens (behind the
  server's caches). Re-check the token and return the **current** identity + permissions.
- **`RevokeAsync`** — best-effort logout: evict your caches and call upstream revocation if available.

## Worked patterns

- **LDAP** — `AuthenticateAsync` does the bind, returns a self-generated UUID as the token.
  `ValidateTokenAsync` reads your in-memory cache (re-bind periodically to pick up group changes).
- **OIDC / OAuth2 password grant (Keycloak, Authelia, authentik)** — `AuthenticateAsync` POSTs
  `grant_type=password` to the token endpoint, returns the IdP access token verbatim (stash the refresh
  token internally). `ValidateTokenAsync` checks signature + expiry locally and transparently refreshes
  near expiry. `RevokeAsync` calls the IdP revocation endpoint.
- **Forward-auth headers (Authelia / oauth2-proxy)** — `Capabilities = HeaderTrust`.
  `AuthenticateAsync` verifies the trusted-proxy headers (`Remote-User`, `Remote-Groups`) from
  `request.RequestHeaders` and mints a short-lived signed token. `ValidateTokenAsync` re-verifies it.
- **Custom HTTP API** — POST credentials, get a token, validate later.

## Critical contract: identity is authoritative on every validation

`ValidateTokenAsync` returns the **current** identity, not a login-time snapshot. On every successful
validation Jellyfin reconciles the user record (`IsAdministrator`, `PermissionOverrides`,
`AdditionalScopes`) to match. **Returning a stale identity silently downgrades the user.** IdP group
changes propagate without re-login precisely because of this. `ShouldAutoProvision` governs creation
only — it has no effect on returning users.

## Caching, invalidation, performance

The server caches above your plugin: per-request (`HttpContext.Items`) and cross-request
(`IMemoryCache`, keyed on `SHA-256(token)`, TTL `min(30s, time-to-expiry)`). Your own caching sits
below. When your internal state changes (IdP webhook, external permission change), call
`IAuthenticationPluginContext.InvalidateTokenAsync` / `InvalidateUserAsync` to flush the server cache.

Budgets (guidance, not enforced): `ValidateTokenAsync` p99 ≤ 10 ms cache hit / ≤ 100 ms miss;
`AuthenticateAsync` p99 ≤ 2 s.

## Failure handling

A throwing `ValidateTokenAsync` yields **401** for that request (never 500). A per-plugin circuit
breaker opens at >50% failures over a ≥20-request, 60-second window, fails your tokens closed for 30 s
with `Retry-After: 30`, then half-opens. Fail fast and clearly; don't hang.

## Tokens and transport

Plugin tokens are **header-only** (`Authorization: Bearer pt_{id}_...`). They are full-power session
tokens and are refused in the `?token=` query string. Discovery: clients call `GET /Auth/Providers`
(anonymous) to render the provider picker, then `POST /Users/AuthenticateByName` with
`AuthProvider: "{id}"` (or `POST /Users/AuthenticateByName/{id}`).
