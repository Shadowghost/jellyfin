# Auth Migration Guide (client & plugin authors)

This release replaces the single legacy authentication handler with the standard ASP.NET Core
pipeline (multiple schemes, JWTs, scopes). This guide covers what changed and what was removed.

## For client developers

- **Login** (`POST /Users/AuthenticateByName`) now returns `AccessTokenJwt` alongside the existing
  `AccessToken`. Prefer sending `Authorization: Bearer <AccessTokenJwt>`.
- The legacy `Authorization: MediaBrowser Token="..."` header still works (transitional) but
  responses to legacy-authenticated requests now carry a `Deprecation: true` header and a
  deprecation `Link`. Migrate to the JWT.
- The secondary legacy forms — `X-Emby-Token` / `X-MediaBrowser-Token` headers, the `?api_key=`
  query parameter, and the `Emby` authorization scheme — are **disabled by default**
  (`EnableLegacyAuthorization`) and slated for removal.
- For media URLs that cannot set headers, mint a scoped temp token via `POST /Auth/TempTokens` and
  pass it as `?token=`. See [temp-tokens.md](temp-tokens.md).

## For plugin authors

**Removed APIs** (`MediaBrowser.Controller.Authentication`):
- `IAuthenticationProvider`
- `IRequiresResolvedUser`
- `IHasNewUserPolicy`
- `ProviderAuthenticationResult`

Plugins that implemented `IAuthenticationProvider` (e.g. LDAP, SSO) will **fail to load** until
updated. Re-target to:
- **`IAuthenticationPlugin`** — the plugin owns its auth flow end-to-end (authenticate, return an
  opaque token, validate it on each request). See [plugin-authoring.md](plugin-authoring.md).
- or the built-in password flow (no plugin needed) for local credentials.

On startup, the server logs a clear Error for any loaded plugin still implementing the removed
`IAuthenticationProvider` (it does not crash). Built-in user `AuthenticationProviderId` values are
normalized to `PasswordValidator`; values pointing at removed plugin types are parked as `orphaned`
(those users cannot log in until an admin reassigns or deletes them).

## Deprecation timeline

- **This release** — JWTs introduced; legacy MediaBrowser header still works (deprecated, with
  headers); secondary legacy forms off by default; `IAuthenticationProvider` removed.
- **Next major** — `EnableLegacyAuthorization` removed; `LegacyMediaBrowserToken` handler scheduled
  for removal once clients have migrated.
