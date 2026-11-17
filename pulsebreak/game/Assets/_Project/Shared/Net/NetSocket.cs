using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Pulsebreak.Shared.Net
{
    /// <summary>Where a packet came from or is going: an IPv4 address and a port.</summary>
    public readonly struct NetAddress : IEquatable<NetAddress>
    {
        /// <summary>a.b.c.d packed as a &lt;&lt; 24 | b &lt;&lt; 16 | c &lt;&lt; 8 | d.</summary>
        public readonly uint Host;
        public readonly ushort Port;

        public NetAddress(uint host, int port)
        {
            Host = host;
            Port = (ushort)port;
        }

        public bool IsValid => Port != 0;

        public static NetAddress Loopback(int port) => new NetAddress(0x7F000001u, port);

        public bool Equals(NetAddress other) => Host == other.Host && Port == other.Port;
        public override bool Equals(object obj) => obj is NetAddress other && Equals(other);
        public override int GetHashCode() => unchecked((int)(Host * 397u) ^ Port);
        public static bool operator ==(NetAddress a, NetAddress b) => a.Equals(b);
        public static bool operator !=(NetAddress a, NetAddress b) => !a.Equals(b);

        public override string ToString() =>
            (Host >> 24) + "." + ((Host >> 16) & 255) + "." + ((Host >> 8) & 255) + "." + (Host & 255) + ":" + Port;

        /// <summary>
        /// Reads "host" or "host:port". The host can be a dotted address, "localhost" or a
        /// name to look up. Looking a name up can block for a moment.
        /// </summary>
        public static bool TryParse(string text, int defaultPort, out NetAddress address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            int port = defaultPort;
            int colon = text.LastIndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(text.Substring(colon + 1), out port)) return false;
                text = text.Substring(0, colon);
            }
            if (port <= 0 || port > 65535 || text.Length == 0) return false;

            if (text == "localhost")
            {
                address = Loopback(port);
                return true;
            }
            if (TryParseDotted(text, out uint host))
            {
                address = new NetAddress(host, port);
                return true;
            }

            try
            {
                foreach (IPAddress candidate in Dns.GetHostAddresses(text))
                {
                    if (candidate.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] bytes = candidate.GetAddressBytes();
                    address = new NetAddress((uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]), port);
                    return true;
                }
            }
            catch (Exception)
            {
                // Unknown name, or no network: treated as "could not parse".
            }
            return false;
        }

        static bool TryParseDotted(string text, out uint host)
        {
            host = 0;
            string[] parts = text.Split('.');
            if (parts.Length != 4) return false;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3 || !uint.TryParse(part, out uint value) || value > 255) return false;
                host = (host << 8) | value;
            }
            return true;
        }
    }

    /// <summary>
    /// Sends and receives datagrams: small packets that may arrive late, twice, out of
    /// order or not at all. Everything above this (connections, reliability) is the same
    /// whether the packets travel over a real network or inside one process for a test.
    /// </summary>
    public interface INetSocket : IDisposable
    {
        NetAddress LocalAddress { get; }

        /// <summary>Never blocks. Returns false when nothing is waiting.</summary>
        bool Receive(byte[] buffer, out int length, out NetAddress from);

        void Send(NetAddress to, byte[] data, int length);
    }

    /// <summary>A real UDP socket (IPv4).</summary>
    public sealed class UdpSocket : INetSocket
    {
        readonly Socket _socket;
        readonly Dictionary<NetAddress, IPEndPoint> _endpoints = new Dictionary<NetAddress, IPEndPoint>();
        EndPoint _from = new IPEndPoint(IPAddress.Any, 0);

        public NetAddress LocalAddress { get; }

        /// <param name="port">The port to listen on, or 0 to let the system pick one (what a client wants).</param>
        public UdpSocket(int port = 0)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            try
            {
                _socket.ReceiveBufferSize = 1 << 18;
                _socket.SendBufferSize = 1 << 18;
            }
            catch (SocketException)
            {
                // The defaults will do.
            }

            try
            {
                // Windows only: without this, a packet sent to a peer that has gone away makes
                // the next receive fail with "connection reset".
                const int SioUdpConnReset = -1744830452;
                _socket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
            }
            catch (Exception)
            {
                // Not Windows, or not supported: nothing to switch off.
            }

            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
            LocalAddress = new NetAddress(0x7F000001u, ((IPEndPoint)_socket.LocalEndPoint).Port);
        }

        public bool Receive(byte[] buffer, out int length, out NetAddress from)
        {
            length = 0;
            from = default;

            // A failed receive (for example a reset from a peer that left) is skipped, not fatal.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (_socket.Available <= 0) return false;
                    length = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref _from);
                }
                catch (SocketException)
                {
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }

                var endpoint = (IPEndPoint)_from;
                byte[] bytes = endpoint.Address.GetAddressBytes();
                if (bytes.Length != 4) continue;
                from = new NetAddress((uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]), endpoint.Port);
                return true;
            }
            return false;
        }

        public void Send(NetAddress to, byte[] data, int length)
        {
            if (!_endpoints.TryGetValue(to, out IPEndPoint endpoint))
            {
                if (_endpoints.Count > 256) _endpoints.Clear();
                var bytes = new[] { (byte)(to.Host >> 24), (byte)(to.Host >> 16), (byte)(to.Host >> 8), (byte)to.Host };
                endpoint = new IPEndPoint(new IPAddress(bytes), to.Port);
                _endpoints[to] = endpoint;
            }

            try
            {
                _socket.SendTo(data, 0, length, SocketFlags.None, endpoint);
            }
            catch (SocketException)
            {
                // A full send buffer or an unreachable peer: the packet is simply lost, which
                // everything above already copes with.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            _socket.Close();
        }
    }
}
