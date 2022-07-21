#pragma warning disable CS1591

using System;
using System.Net;
using System.Net.Sockets;
using MediaBrowser.Model.Net;

namespace Emby.Server.Implementations.Net
{
    public class SocketFactory : ISocketFactory
    {
        /// <inheritdoc />
        public ISocket CreateUdpBroadcastSocket(int localPort)
        {
            if (localPort < 0)
            {
                throw new ArgumentException("localPort cannot be less than zero.", nameof(localPort));
            }

            var retVal = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                retVal.EnableBroadcast = true;
                retVal.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                retVal.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);

                return new UdpSocket(retVal, localPort, IPAddress.Any);
            }
            catch
            {
                retVal?.Dispose();

                throw;
            }
        }

        /// <inheritdoc />
        public ISocket CreateSsdpUdpSocket(IPAddress ipAddress, IPAddress localIp, int localPort, int? interfaceId)
        {
            if (localPort < 0)
            {
                throw new ArgumentException("localPort cannot be less than zero.", nameof(localPort));
            }

            var retVal = localIp.AddressFamily == AddressFamily.InterNetwork
                ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                retVal.EnableBroadcast = true;
                retVal.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                if (localIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    retVal.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 4);
                    retVal.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ipAddress, localIp));
                }

                if (localIp.AddressFamily == AddressFamily.InterNetworkV6 && interfaceId.HasValue)
                {
                    retVal.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 4);
                    retVal.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(ipAddress, Convert.ToInt64(interfaceId.Value)));
                }

                return new UdpSocket(retVal, localPort, localIp);
            }
            catch
            {
                retVal?.Dispose();

                throw;
            }
        }

        /// <inheritdoc />
        public ISocket CreateUdpMulticastSocket(IPAddress ipAddress, IPAddress bindIpAddress, int multicastTimeToLive, int localPort, int? interfaceId)
        {
            if (ipAddress == null)
            {
                throw new ArgumentNullException(nameof(ipAddress));
            }

            if (bindIpAddress == null)
            {
                bindIpAddress = ipAddress.AddressFamily == AddressFamily.InterNetwork
                ? IPAddress.Any
                : IPAddress.IPv6Any;
            }

            if (multicastTimeToLive <= 0)
            {
                throw new ArgumentException("multicastTimeToLive cannot be zero or less.", nameof(multicastTimeToLive));
            }

            if (localPort < 0)
            {
                throw new ArgumentException("localPort cannot be less than zero.", nameof(localPort));
            }

            var retVal = bindIpAddress.AddressFamily == AddressFamily.InterNetwork
                ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

            retVal.ExclusiveAddressUse = false;

            try
            {
                // seeing occasional exceptions thrown on qnap
                // System.Net.Sockets.SocketException (0x80004005): Protocol not available
                retVal.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            catch (SocketException)
            {
            }

            try
            {
                retVal.EnableBroadcast = true;

                if (bindIpAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    retVal.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, multicastTimeToLive);
                    if (interfaceId.HasValue)
                    {
                        retVal.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ipAddress, bindIpAddress));
                    }
                    else
                    {
                        retVal.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(ipAddress));
                    }
                }

                if (bindIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    retVal.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive, 4);
                    if (interfaceId.HasValue)
                    {
                        retVal.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(ipAddress, Convert.ToInt64(interfaceId.Value)));
                    }
                    else
                    {
                        retVal.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(ipAddress));
                    }
                }

                retVal.MulticastLoopback = true;

                return new UdpSocket(retVal, localPort, bindIpAddress);
            }
            catch
            {
                retVal?.Dispose();

                throw;
            }
        }
    }
}
