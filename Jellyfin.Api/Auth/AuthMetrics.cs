using Prometheus;

namespace Jellyfin.Api.Auth;

/// <summary>
/// Prometheus counters for authentication telemetry. Counters live in the global registry and are
/// always incremented; they are only exposed when <c>EnableMetrics</c> maps the <c>/metrics</c> endpoint.
/// </summary>
public static class AuthMetrics
{
    /// <summary>Scheme label for Jellyfin full-session JWTs.</summary>
    public const string SchemeJwt = "jwt";

    /// <summary>Scheme label for short-lived temp JWTs.</summary>
    public const string SchemeTemp = "temp";

    /// <summary>Scheme label for the legacy MediaBrowser token scheme.</summary>
    public const string SchemeLegacy = "legacy";

    /// <summary>Scheme label for plugin-owned token authentication.</summary>
    public const string SchemePlugin = "plugin";

    private const string Success = "success";
    private const string Failure = "failure";

    private static readonly Counter _attempts = Metrics.CreateCounter(
        "jellyfin_authentication_attempts_total",
        "Number of per-request authentication attempts, by scheme and result.",
        "scheme",
        "result");

    private static readonly Counter _logins = Metrics.CreateCounter(
        "jellyfin_authentication_logins_total",
        "Number of interactive login attempts, by result.",
        "result");

    private static readonly Counter _tempTokensIssued = Metrics.CreateCounter(
        "jellyfin_authentication_temp_tokens_issued_total",
        "Number of temp tokens issued.");

    private static readonly Counter _tempTokensRevoked = Metrics.CreateCounter(
        "jellyfin_authentication_temp_tokens_revoked_total",
        "Number of temp tokens revoked.");

    internal static Counter Attempts => _attempts;

    internal static Counter Logins => _logins;

    internal static Counter TempTokensIssued => _tempTokensIssued;

    internal static Counter TempTokensRevoked => _tempTokensRevoked;

    /// <summary>
    /// Records a per-request authentication attempt for a scheme.
    /// </summary>
    /// <param name="scheme">The scheme name (e.g. jwt, temp, legacy, external).</param>
    /// <param name="success">Whether authentication succeeded.</param>
    public static void RecordAttempt(string scheme, bool success)
        => _attempts.WithLabels(scheme, success ? Success : Failure).Inc();

    /// <summary>
    /// Records an interactive login outcome.
    /// </summary>
    /// <param name="success">Whether the login succeeded.</param>
    public static void RecordLogin(bool success)
        => _logins.WithLabels(success ? Success : Failure).Inc();

    /// <summary>
    /// Records that a temp token was issued.
    /// </summary>
    public static void TempTokenIssued() => _tempTokensIssued.Inc();

    /// <summary>
    /// Records that a temp token was revoked.
    /// </summary>
    public static void TempTokenRevoked() => _tempTokensRevoked.Inc();
}
