#pragma warning disable CS1591

using System.Collections.Generic;
using System.Net;

namespace MediaBrowser.Model.Net
{
    /// <summary>
    /// Implemented by components that can create a platform specific UDP socket implementation, and wrap it in the cross platform <see cref="ISocket"/> interface.
    /// </summary>
    public interface ISocketFactory
    {
        ISocket CreateUdpBroadcastSocket(int localPort);

        /// <summary>
        /// Creates a new unicast socket using the specified local port number.
        /// </summary>
        /// <param name="ipAddress">The multicast IP address to bind to.</param>
        /// <param name="localIp">The local IP address to bind to.</param>
        /// <param name="localPort">The local port to bind to.</param>
        /// <param name="interfaceId">The id of the interface providing bindIpAddress.</param>
        /// <returns>A new unicast socket using the specified local port number.</returns>
        ISocket CreateSsdpUdpSocket(IPAddress ipAddress, IPAddress localIp, int localPort, int? interfaceId);

        /// <summary>
        /// Creates a new multicast socket using the specified multicast IP address, multicast time to live and local port.
        /// </summary>
        /// <param name="ipAddress">The multicast IP address to bind to.</param>
        /// <param name="bindIpAddress">The bind IP address.</param>
        /// <param name="multicastTimeToLive">The multicast time to live value. Actually a maximum number of network hops for UDP packets.</param>
        /// <param name="localPort">The local port to bind to.</param>
        /// <param name="interfaceId">The id of the interface providing bindIpAddress.</param>
        /// <returns>A <see cref="ISocket"/> implementation.</returns>
        ISocket CreateUdpMulticastSocket(IPAddress ipAddress, IPAddress bindIpAddress, int multicastTimeToLive, int localPort, int? interfaceId);
    }
}
