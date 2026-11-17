using System;
using System.Collections.Generic;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Net
{
    /// <summary>How bad a connection to pretend to have. All values are one-way.</summary>
    [Serializable]
    public sealed class NetConditions
    {
        /// <summary>Delay added to every packet, in milliseconds.</summary>
        public float latencyMs;
        /// <summary>Extra random delay per packet, 0 to this many milliseconds. Makes packets arrive out of order.</summary>
        public float jitterMs;
        /// <summary>Chance that a packet vanishes, 0 to 100.</summary>
        public float lossPercent;
        /// <summary>Chance that a packet arrives twice, 0 to 100.</summary>
        public float duplicatePercent;

        public bool Any => latencyMs > 0f || jitterMs > 0f || lossPercent > 0f || duplicatePercent > 0f;
    }

    /// <summary>
    /// A network that lives inside one process. Sockets opened on it deliver straight to
    /// each other. Tests use it to run a server and several clients together, and it is
    /// what a single-process game would use to talk to its own server.
    /// </summary>
    public sealed class MemoryNetwork
    {
        readonly Dictionary<NetAddress, MemorySocket> _sockets = new Dictionary<NetAddress, MemorySocket>();
        uint _nextHost = 0x0A000001u;

        public INetSocket Open(int port = NetProtocol.DefaultPort)
        {
            var socket = new MemorySocket(this, new NetAddress(_nextHost++, port));
            _sockets[socket.LocalAddress] = socket;
            return socket;
        }

        void Deliver(NetAddress from, NetAddress to, byte[] data, int length)
        {
            if (!_sockets.TryGetValue(to, out MemorySocket socket)) return;
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            socket.Inbox.Enqueue(new KeyValuePair<NetAddress, byte[]>(from, copy));
        }

        sealed class MemorySocket : INetSocket
        {
            readonly MemoryNetwork _network;
            public readonly Queue<KeyValuePair<NetAddress, byte[]>> Inbox = new Queue<KeyValuePair<NetAddress, byte[]>>();

            public NetAddress LocalAddress { get; }

            public MemorySocket(MemoryNetwork network, NetAddress address)
            {
                _network = network;
                LocalAddress = address;
            }

            public bool Receive(byte[] buffer, out int length, out NetAddress from)
            {
                length = 0;
                from = default;
                if (Inbox.Count == 0) return false;

                KeyValuePair<NetAddress, byte[]> packet = Inbox.Dequeue();
                from = packet.Key;
                length = Math.Min(packet.Value.Length, buffer.Length);
                Buffer.BlockCopy(packet.Value, 0, buffer, 0, length);
                return true;
            }

            public void Send(NetAddress to, byte[] data, int length) => _network.Deliver(LocalAddress, to, data, length);

            public void Dispose() => _network._sockets.Remove(LocalAddress);
        }
    }

    /// <summary>
    /// Wraps a socket and makes it behave like a bad connection: packets in both directions
    /// are delayed, reordered, dropped and duplicated according to <see cref="NetConditions"/>.
    /// Put it around a client's socket to feel what the game is like at a given ping.
    /// </summary>
    public sealed class ConditionedSocket : INetSocket
    {
        struct Held
        {
            public double Due;
            public NetAddress Address;
            public byte[] Data;
        }

        readonly INetSocket _inner;
        readonly Func<double> _clock;
        readonly List<Held> _outgoing = new List<Held>();
        readonly List<Held> _incoming = new List<Held>();
        readonly byte[] _scratch = new byte[2048];
        DeterministicRandom _random;

        public NetConditions Conditions { get; }
        public NetAddress LocalAddress => _inner.LocalAddress;

        /// <param name="clock">The current time in seconds.</param>
        public ConditionedSocket(INetSocket inner, NetConditions conditions, Func<double> clock, uint seed = 1)
        {
            _inner = inner;
            Conditions = conditions;
            _clock = clock;
            _random = new DeterministicRandom(seed);
        }

        public void Send(NetAddress to, byte[] data, int length)
        {
            Schedule(_outgoing, to, data, length);
            Pump();
        }

        public bool Receive(byte[] buffer, out int length, out NetAddress from)
        {
            Pump();
            int index = EarliestDue(_incoming, _clock());
            if (index < 0)
            {
                length = 0;
                from = default;
                return false;
            }

            Held packet = _incoming[index];
            _incoming.RemoveAt(index);
            from = packet.Address;
            length = Math.Min(packet.Data.Length, buffer.Length);
            Buffer.BlockCopy(packet.Data, 0, buffer, 0, length);
            return true;
        }

        /// <summary>Moves packets whose delay is up onto the real socket, and newly arrived ones into the delay queue.</summary>
        void Pump()
        {
            double now = _clock();
            for (int index = EarliestDue(_outgoing, now); index >= 0; index = EarliestDue(_outgoing, now))
            {
                Held packet = _outgoing[index];
                _outgoing.RemoveAt(index);
                _inner.Send(packet.Address, packet.Data, packet.Data.Length);
            }

            while (_inner.Receive(_scratch, out int length, out NetAddress from))
                Schedule(_incoming, from, _scratch, length);
        }

        void Schedule(List<Held> queue, NetAddress address, byte[] data, int length)
        {
            if (Roll(Conditions.lossPercent)) return;
            int copies = Roll(Conditions.duplicatePercent) ? 2 : 1;
            for (int i = 0; i < copies; i++)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                double delay = (Conditions.latencyMs + Conditions.jitterMs * _random.NextFloat()) / 1000.0;
                queue.Add(new Held { Due = _clock() + delay, Address = address, Data = copy });
            }
        }

        bool Roll(float percent) => percent > 0f && _random.NextFloat() * 100f < percent;

        static int EarliestDue(List<Held> queue, double now)
        {
            int best = -1;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].Due > now) continue;
                if (best < 0 || queue[i].Due < queue[best].Due) best = i;
            }
            return best;
        }

        public void Dispose() => _inner.Dispose();
    }
}
