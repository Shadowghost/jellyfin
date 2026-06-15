using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Api.Tests.Auth
{
    /// <summary>
    /// Fence test: every controller action must make an explicit authorization decision via either
    /// <see cref="AuthorizeAttribute"/> or <see cref="AllowAnonymousAttribute"/> (on the action or its
    /// declaring controller). Fails the build if a new endpoint ships without an explicit decision.
    /// </summary>
    public class EndpointAuthAuditTests
    {
        [Fact]
        public void EveryAction_HasExplicitAuthorizationDecision()
        {
            var violations = GetUndecidedActions().ToList();

            Assert.True(
                violations.Count == 0,
                "The following controller actions lack an explicit [Authorize] or [AllowAnonymous]:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }

        private static IEnumerable<string> GetUndecidedActions()
        {
            var controllerTypes = typeof(SystemController).Assembly
                .GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var controller in controllerTypes)
            {
                var controllerDecided = HasAuthDecision(controller);

                foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.IsSpecialName || method.GetCustomAttribute<NonActionAttribute>() is not null)
                    {
                        continue;
                    }

                    // Only consider routable action methods (those with an HTTP method attribute).
                    if (method.GetCustomAttributes<HttpMethodAttribute>().Any()
                        && !controllerDecided
                        && !HasAuthDecision(method))
                    {
                        yield return $"{controller.Name}.{method.Name}";
                    }
                }
            }
        }

        private static bool HasAuthDecision(MemberInfo member)
            => member.GetCustomAttributes<AuthorizeAttribute>().Any()
               || member.GetCustomAttributes<AllowAnonymousAttribute>().Any();
    }
}
