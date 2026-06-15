using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Api.Auth.ScopePolicy;

/// <summary>
/// Authorization requirement for an endpoint scope.
/// </summary>
public sealed class ScopeRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeRequirement"/> class.
    /// </summary>
    /// <param name="scope">The required scope.</param>
    public ScopeRequirement(string scope)
    {
        Scope = scope;
    }

    /// <summary>
    /// Gets the required scope.
    /// </summary>
    public string Scope { get; }
}
