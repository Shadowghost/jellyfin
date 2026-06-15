using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;

namespace Jellyfin.Api.Auth;

/// <summary>
/// Records interactive login telemetry from the authentication events published by the session manager.
/// </summary>
public class AuthLoginMetricsConsumer :
    IEventConsumer<AuthenticationResultEventArgs>,
    IEventConsumer<AuthenticationRequestEventArgs>
{
    /// <summary>
    /// Records a successful login.
    /// </summary>
    /// <param name="eventArgs">The successful authentication result.</param>
    /// <returns>A completed task.</returns>
    public Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        AuthMetrics.RecordLogin(true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a failed login.
    /// </summary>
    /// <param name="eventArgs">The failed authentication request.</param>
    /// <returns>A completed task.</returns>
    public Task OnEvent(AuthenticationRequestEventArgs eventArgs)
    {
        AuthMetrics.RecordLogin(false);
        return Task.CompletedTask;
    }
}
