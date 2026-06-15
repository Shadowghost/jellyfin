using System;
using System.Threading.Tasks;
using Jellyfin.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Api.Auth.ScopePolicy;

/// <summary>
/// Authorizes a request against a required endpoint scope.
/// </summary>
/// <remarks>
/// Unscoped principals (legacy MediaBrowser tokens and full-session JWTs that carry no explicit
/// scope narrowing) are treated as unrestricted, preserving backwards compatibility. Tokens that
/// carry scope claims must include the required scope (or the <c>full</c> scope). Tokens bound to a
/// specific item (resource binding) may only act on that item.
/// </remarks>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeAuthorizationHandler"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public ScopeAuthorizationHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var scopes = context.User.GetScopes();

        // Unscoped principals are unrestricted; scoped principals must hold the required (or full) scope.
        if (scopes.Count != 0 && !context.User.HasScope(requirement.Scope))
        {
            return Task.CompletedTask;
        }

        var boundItemId = context.User.GetBoundItemId();
        if (!string.IsNullOrEmpty(boundItemId))
        {
            var httpContext = context.Resource as HttpContext ?? _httpContextAccessor.HttpContext;
            var routeItemId = httpContext?.Request.RouteValues.TryGetValue("itemId", out var value) == true
                ? value?.ToString()
                : null;

            if (!string.Equals(boundItemId, routeItemId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
