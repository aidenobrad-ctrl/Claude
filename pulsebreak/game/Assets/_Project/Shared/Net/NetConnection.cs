using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pulsebreak.Shared.Net
{
    /// <summary>The first byte of every packet.</summary>
    public enum PacketType : byte
    {
        /// <summary>Client: "I would like to join." Carries a number the client made up.</summary>
        ConnectRequest = 1,
        /// <summary>Server: "Prove you can receive at that address." Carries a number only the real sender will see.</summary>
        Challenge = 2,
        /// <summary>Client: both numbers combined. Only now does the server spend anything on this client.</summary>
        ChallengeResponse = 3,
        Accepted = 4,
        Denied = 5,
        /// <summary>Everything once connected.</summary>
        Data = 6,
        Disconnect = 7,
    }

    public enum DenyReason : byte
    {
        ServerFull = 1,
        WrongVersion = 2,
    }

    /// <summary>Why a server ended a connection. Sent with the goodbye so the player can be told.</summary>
    public enum DisconnectReason : byte
    {
        /// <summary>The server shut down, or gave no reason.</summary>
        None = 0,
        /// <summary>This server only admits signed-in players, and no valid ticket was shown.</summary>
        TicketRequired = 1,
        /// <summary>The same account is already in this match.</summary>
        AlreadyHere = 2,
        /// <summary>A ranked server, and the player's ticket is not good for ranked (their email is not verified).</summary>
        RankedOnly = 3,
        /// <summary>A matchmade server, and the player's ticket is for a different match.</summary>
        WrongMatch = 4,
    }

    public static class NetProtocol
    {
        /// <summary>Changes whenever anything about what goes over the wire changes, so mismatched builds refuse each other.</summary>
        public const uint Version = 0x50420800u;
        public const int DefaultPort = 7777;
        /// <summary>The most players in one match: five a side, or a twelve-player free-for-all.</summary>
        public const int MaxPlayers = 12;
        /// <summary>Small enough to cross the internet without being split up.</summary>
        public const int MaxPacketSize = 1200;
        /// <summary>A connect request is padded to this, so a server never answers with more than it was sent.</summary>
        public const int ConnectRequestSize = 64;

        public const float ConnectRetrySeconds = 0.25f;
        public const float ConnectTimeoutSeconds = 6f;
        /// <summary>Silence for this long means the other side is gone.</summary>
        public const float TimeoutSeconds = 8f;

        /// <summary>True if sequence number <paramref name="a"/> comes after <paramref name="b"/>, allowing for wrap-around.</summary>
        public static bool SequenceNewer(ushort a, ushort b) => (short)(ushort)(a - b) > 0;
    }

    /// <summary>
    /// One end of a connection over an unreliable socket. It does three jobs:
    ///
    ///  - numbers every packet and tells the other side which ones arrived, which also
    ///    measures the round trip;
    ///  - carries unreliable messages, sent once and forgotten (inputs, snapshots: a newer
    ///    one is on its way anyway);
    ///  - carries reliable messages, resent until acknowledged and handed over in the order
    ///    they were sent (a round ending, a kill: things that must not be missed).
    /// </summary>
    public sealed class NetConnection
    {
        const int Window = 256;
        const int HeaderSize = 1 + 4 + 2 + 2 + 4;
        const int MaxReliablePerPacket = 24;
        const int MaxPacketsPerFlush = 8;
        const int MaxQueuedReliable = 2048;
        const int MaxHeldReliable = 512;
        const byte FrameUnreliable = 0, FrameReliable = 1;

        /// <summary>The largest single message that fits in a packet.</summary>
        public const int MaxMessageSize = NetProtocol.MaxPacketSize - HeaderSize - 5;

        sealed class Outgoing
        {
            public ushort Id;
            public byte[] Data;
            public double LastSent = -1;
        }

        sealed class SentPacket
        {
            public bool Used, Acked;
            public ushort Sequence;
            public double Time;
            public readonly List<ushort> Reliable = new List<ushort>(4);
        }

        public NetAddress Remote { get; }
        /// <summary>Agreed during the handshake. A packet without it is not from this peer.</summary>
        public uint Token { get; }

        public double LastReceiveTime { get; private set; }
        /// <summary>Smoothed round-trip time in seconds. 0 until the first measurement.</summary>
        public float Rtt { get; private set; }
        /// <summary>Rough fraction of sent packets that were never acknowledged, 0..1.</summary>
        public float Loss { get; private set; }
        /// <summary>The reliable queue overflowed: the other side has stopped listening. Drop the connection.</summary>
        public bool Broken { get; private set; }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        // Outgoing.
        readonly List<Outgoing> _reliable = new List<Outgoing>();
        readonly NetWriter _unreliable = new NetWriter(512);
        readonly NetWriter _packet = new NetWriter(NetProtocol.MaxPacketSize);
        readonly SentPacket[] _sent = new SentPacket[Window];
        ushort _nextReliableId;
        ushort _localSequence = 1;

        // Incoming.
        readonly Dictionary<ushort, byte[]> _held = new Dictionary<ushort, byte[]>();
        readonly NetReader _reader = new NetReader();
        readonly NetReader _message = new NetReader();
        ushort _remoteSequence;
        uint _remoteBits;
        ushort _nextReliableIn;

        public NetConnection(NetAddress remote, uint token, double now)
        {
            Remote = remote;
            Token = token;
            LastReceiveTime = now;
            for (int i = 0; i < Window; i++) _sent[i] = new SentPacket();
        }

        // ------------------------------------------------------------------ sending

        /// <summary>
        /// Queues a message. Nothing goes on the wire until <see cref="Flush"/>.
        /// The writer's contents are copied, so it can be reused straight away.
        /// </summary>
        public void Send(NetWriter message, bool reliable)
        {
            if (message.Length == 0 || message.Length > MaxMessageSize)
            {
                Debug.LogWarning("NetConnection: dropped a message of " + message.Length + " bytes (limit " + MaxMessageSize + ").");
                return;
            }

            if (reliable)
            {
                if (_reliable.Count >= MaxQueuedReliable)
                {
                    Broken = true;
                    return;
                }
                var data = new byte[message.Length];
                Buffer.BlockCopy(message.Data, 0, data, 0, message.Length);
                _reliable.Add(new Outgoing { Id = _nextReliableId++, Data = data });
            }
            else
            {
                _unreliable.WriteByte(FrameUnreliable);
                _unreliable.WriteUShort((ushort)message.Length);
                _unreliable.WriteBytes(message.Data, 0, message.Length);
            }
        }

        /// <summary>
        /// Sends what is queued. Call it once per tick even with nothing queued: every packet
        /// also carries the acknowledgements the other side is waiting for.
        /// </summary>
        public void Flush(INetSocket socket, double now)
        {
            // A reliable message is repeated if its last copy has had time to be acknowledged and was not.
            double resendDelay = Mathf.Clamp(Rtt * 1.5f, 0.06f, 0.5f);
            int unreliableOffset = 0;
            int reliableIndex = 0;
            bool more;
            int packets = 0;

            do
            {
                SentPacket record = _sent[_localSequence % Window];
                if (record.Used) Loss = Mathf.Lerp(Loss, record.Acked ? 0f : 1f, 0.02f);
                record.Used = true;
                record.Acked = false;
                record.Sequence = _localSequence;
                record.Time = now;
                record.Reliable.Clear();

                _packet.Reset();
                _packet.WriteByte((byte)PacketType.Data);
                _packet.WriteUInt(Token);
                _packet.WriteUShort(_localSequence);
                _packet.WriteUShort(_remoteSequence);
                _packet.WriteUInt(_remoteBits);

                // Whole frames only: a message is never split across packets.
                while (unreliableOffset < _unreliable.Length)
                {
                    int frame = 3 + (_unreliable.Data[unreliableOffset + 1] | (_unreliable.Data[unreliableOffset + 2] << 8));
                    if (_packet.Length + frame > NetProtocol.MaxPacketSize) break;
                    _packet.WriteBytes(_unreliable.Data, unreliableOffset, frame);
                    unreliableOffset += frame;
                }

                for (; reliableIndex < _reliable.Count; reliableIndex++)
                {
                    Outgoing message = _reliable[reliableIndex];
                    if (message.LastSent >= 0 && now - message.LastSent < resendDelay) continue;
                    if (_packet.Length + 5 + message.Data.Length > NetProtocol.MaxPacketSize ||
                        record.Reliable.Count >= MaxReliablePerPacket) break;

                    _packet.WriteByte(FrameReliable);
                    _packet.WriteUShort(message.Id);
                    _packet.WriteUShort((ushort)message.Data.Length);
                    _packet.WriteBytes(message.Data, 0, message.Data.Length);
                    message.LastSent = now;
                    record.Reliable.Add(message.Id);
                }

                socket.Send(Remote, _packet.Data, _packet.Length);
                BytesSent += _packet.Length;

                // 0 is kept to mean "nothing received yet" in the acknowledgement field.
                if (++_localSequence == 0) _localSequence = 1;
                more = unreliableOffset < _unreliable.Length || reliableIndex < _reliable.Count;
            } while (more && ++packets < MaxPacketsPerFlush);

            _unreliable.Reset();
        }

        // ------------------------------------------------------------------ receiving

        /// <summary>
        /// Takes in one data packet. Each message in it is handed to <paramref name="handler"/>,
        /// reliable ones strictly in the order they were sent.
        /// </summary>
        /// <returns>False if the packet was not from this peer, was a repeat, or was malformed.</returns>
        public bool Receive(byte[] data, int length, double now, Action<NetReader> handler)
        {
            _reader.Reset(data, 0, length);
            if (_reader.ReadByte() != (byte)PacketType.Data || _reader.ReadUInt() != Token) return false;
            ushort sequence = _reader.ReadUShort();
            ushort ack = _reader.ReadUShort();
            uint ackBits = _reader.ReadUInt();
            if (_reader.Failed || sequence == 0) return false;

            if (!Track(sequence)) return false;
            LastReceiveTime = now;
            BytesReceived += length;

            if (ack != 0)
            {
                Acknowledge(ack, now);
                for (int i = 0; i < 32; i++)
                {
                    if ((ackBits & (1u << i)) != 0) Acknowledge((ushort)(ack - 1 - i), now);
                }
            }

            while (_reader.Remaining > 0)
            {
                byte frame = _reader.ReadByte();
                if (frame == FrameUnreliable)
                {
                    int size = _reader.ReadUShort();
                    if (size == 0 || size > _reader.Remaining) return false;
                    Deliver(data, _reader.Position, size, handler);
                    _reader.Skip(size);
                }
                else if (frame == FrameReliable)
                {
                    ushort id = _reader.ReadUShort();
                    int size = _reader.ReadUShort();
                    if (size == 0 || size > _reader.Remaining) return false;
                    ReceiveReliable(id, data, _reader.Position, size, handler);
                    _reader.Skip(size);
                }
                else
                {
                    return false;
                }
            }
            return !_reader.Failed;
        }

        /// <summary>Records that a packet arrived. False if it already had, or is too old to tell.</summary>
        bool Track(ushort sequence)
        {
            if (_remoteSequence == 0)
            {
                _remoteSequence = sequence;
                _remoteBits = 0;
                return true;
            }

            if (NetProtocol.SequenceNewer(sequence, _remoteSequence))
            {
                // Bit n means "the packet n + 1 before the latest arrived".
                int shift = (ushort)(sequence - _remoteSequence);
                _remoteBits = shift >= 32 ? 0u : _remoteBits << shift;
                if (shift <= 32) _remoteBits |= 1u << (shift - 1);
                _remoteSequence = sequence;
                return true;
            }

            int behind = (ushort)(_remoteSequence - sequence);
            if (behind == 0 || behind > 32) return false;
            uint bit = 1u << (behind - 1);
            if ((_remoteBits & bit) != 0) return false;
            _remoteBits |= bit;
            return true;
        }

        void Acknowledge(ushort sequence, double now)
        {
            SentPacket record = _sent[sequence % Window];
            if (!record.Used || record.Acked || record.Sequence != sequence) return;
            record.Acked = true;

            float sample = (float)(now - record.Time);
            Rtt = Rtt <= 0f ? sample : Mathf.Lerp(Rtt, sample, 0.1f);

            for (int i = 0; i < record.Reliable.Count; i++)
            {
                ushort id = record.Reliable[i];
                for (int j = 0; j < _reliable.Count; j++)
                {
                    if (_reliable[j].Id != id) continue;
                    _reliable.RemoveAt(j);
                    break;
                }
            }
        }

        void ReceiveReliable(ushort id, byte[] data, int offset, int size, Action<NetReader> handler)
        {
            int ahead = (ushort)(id - _nextReliableIn);
            if (ahead == 0)
            {
                Deliver(data, offset, size, handler);
                _nextReliableIn++;

                // Anything that arrived early and was waiting for this one.
                while (_held.TryGetValue(_nextReliableIn, out byte[] waiting))
                {
                    _held.Remove(_nextReliableIn);
                    Deliver(waiting, 0, waiting.Length, handler);
                    _nextReliableIn++;
                }
            }
            else if (ahead < MaxHeldReliable)
            {
                if (_held.ContainsKey(id)) return;
                var copy = new byte[size];
                Buffer.BlockCopy(data, offset, copy, 0, size);
                _held[id] = copy;
            }
            // Otherwise it is a repeat of something already delivered.
        }

        void Deliver(byte[] data, int offset, int size, Action<NetReader> handler)
        {
            _message.Reset(data, offset, size);
            handler(_message);
        }
    }
}
