using System.Net;
using Microsoft.AspNetCore.Http;

namespace MediaBrowser.Controller.Authentication
{
    /// <summary>
    /// A login request handed to an authentication plugin.
    /// </summary>
    public sealed class PluginAuthenticationRequest
    {
        /// <summary>
        /// Gets the username, if supplied.
        /// </summary>
        public string? Username { get; init; }

        /// <summary>
        /// Gets the password, if supplied.
        /// </summary>
        public string? Password { get; init; }

        /// <summary>
        /// Gets the device id.
        /// </summary>
        public string? DeviceId { get; init; }

        /// <summary>
        /// Gets the device name.
        /// </summary>
        public string? DeviceName { get; init; }

        /// <summary>
        /// Gets the client app name.
        /// </summary>
        public string? ClientName { get; init; }

        /// <summary>
        /// Gets the client app version.
        /// </summary>
        public string? ClientVersion { get; init; }

        /// <summary>
        /// Gets the raw HTTP request headers, for plugins that authenticate from trusted-proxy headers.
        /// </summary>
        public IHeaderDictionary RequestHeaders { get; init; } = default!;

        /// <summary>
        /// Gets the remote IP address of the request.
        /// </summary>
        public IPAddress? RemoteIp { get; init; }
    }
}
