# Temp Tokens

Temp tokens are short-lived, narrowly-scoped JWTs for third-party tools that cannot set an
`Authorization` header on media URLs (Chromecast, mpv, IPTV apps). They are delivered via the
`?token=` query parameter.

## Issuance

```
POST /Auth/TempTokens          (requires a full session; rate-limited 10/min/user)
{
  "scopes": ["media.stream"],
  "itemId": "abc123",          // optional resource binding
  "ttlSeconds": 300,           // clamped to [60, 3600]
  "label": "AppleTV cast"
}
→ { "token": "<jwt>", "expiresAt": "<ISO8601>" }
```

- Only catalog scopes are grantable — the `full` scope and unknown scopes are rejected (no
  privilege escalation).
- A temp token may not mint further temp tokens.
- `GET /Auth/TempTokens` lists the caller's outstanding tokens; `DELETE /Auth/TempTokens/{id}`
  revokes one (admins may revoke any).

## Validation

The `JellyfinTempJwt` scheme (`aud=urn:jellyfin:temp`):
- accepts the token from `?token=` (or a Bearer header),
- requires at least one scope claim,
- enforces a 1-hour lifetime ceiling,
- consults the revocation store on every request (`ITempTokenStore.IsRevokedAsync`).

Authorization then applies the token's scopes (and item binding) via `ScopeAuthorizationHandler`.

## Security notes

- Long-lived session tokens must **never** travel in the query string (logging/proxy exposure). The
  selector only routes `?token=` to the temp scheme, and the temp scheme only accepts temp-audience
  tokens.
- Temp tokens are short-lived, scope-narrow, optionally item-bound, and revocable — the explicit
  trade-off for query-string transport.
