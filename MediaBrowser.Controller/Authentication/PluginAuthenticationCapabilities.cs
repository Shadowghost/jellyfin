using System;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// Declares which client-side input an authentication plugin requires at login.
    /// </summary>
    [Flags]
    public enum PluginAuthenticationCapabilities
    {
        /// <summary>No declared capability.</summary>
        None = 0,

        /// <summary>The plugin authenticates a username and password.</summary>
        UsernamePassword = 1 << 0,

        /// <summary>The plugin authenticates a username only (no password).</summary>
        UsernameOnly = 1 << 1,

        /// <summary>The plugin authenticates from trusted-proxy request headers (forward-auth) with no credentials in the body.</summary>
        HeaderTrust = 1 << 2
    }
}
