using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Tests
{
    public class NetBufferTests
    {
        [Test]
        public void EverythingWritten_ReadsBackTheSame()
        {
            var writer = new NetWriter(4);
            writer.WriteByte(200);
            writer.WriteBool(true);
            writer.WriteUShort(65000);
            writer.WriteShort(-1234);
            writer.WriteInt(-123456789);
            writer.WriteUInt(4000000000u);
            writer.WriteFloat(-3.25f);
            writer.WriteVector2(new Vector2(1.5f, -2.5f));
            writer.WriteVector3(new Vector3(1f, 2f, 3f));
            writer.WriteString("Relay Yard ✓");
            writer.WriteString(null);

            var reader = new NetReader();
            reader.Reset(writer.Data, 0, writer.Length);
            Assert.That(reader.ReadByte(), Is.EqualTo(200));
            Assert.That(reader.ReadBool(), Is.True);
            Assert.That(reader.ReadUShort(), Is.EqualTo(65000));
            Assert.That(reader.ReadShort(), Is.EqualTo(-1234));
            Assert.That(reader.ReadInt(), Is.EqualTo(-123456789));
            Assert.That(reader.ReadUInt(), Is.EqualTo(4000000000u));
            Assert.That(reader.ReadFloat(), Is.EqualTo(-3.25f));
            Assert.That(reader.ReadVector2().y, Is.EqualTo(-2.5f));
            Assert.That(reader.ReadVector3().z, Is.EqualTo(3f));
            Assert.That(reader.ReadString(), Is.EqualTo("Relay Yard ✓"));
            Assert.That(reader.ReadString(), Is.EqualTo(""));
            Assert.That(reader.Failed, Is.False);
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }

        [Test]
        public void ReadingPastTheEnd_FailsQuietly_AndStaysFailed()
        {
            var reader = new NetReader();
            reader.Reset(new byte[] { 1, 2, 3 }, 0, 3);
            Assert.That(reader.ReadUShort(), Is.EqualTo(0x0201));
            Assert.That(reader.ReadInt(), Is.EqualTo(0), "not enough bytes left");
            Assert.That(reader.Failed, Is.True);
            Assert.That(reader.ReadByte(), Is.EqualTo(0), "nothing more comes out once it has failed");
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }

        [Test]
        public void NumbersThatAreNotNumbers_AreRefused()
        {
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var writer = new NetWriter();
                writer.WriteFloat(bad);
                var reader = new NetReader();
                reader.Reset(writer.Data, 0, writer.Length);
                Assert.That(reader.ReadFloat(), Is.EqualTo(0f));
                Assert.That(reader.Failed, Is.True);
            }
        }

        [Test]
        public void AStringLongerThanItsLengthPrefixAllows_Fails()
        {
            var reader = new NetReader();
            reader.Reset(new byte[] { 50, (byte)'a', (byte)'b' }, 0, 3);
            Assert.That(reader.ReadString(), Is.EqualTo(""));
            Assert.That(reader.Failed, Is.True);
        }

        [Test]
        public void LongText_IsCutToFit()
        {
            var writer = new NetWriter();
            writer.WriteString(new string('é', 300));
            var reader = new NetReader();
            reader.Reset(writer.Data, 0, writer.Length);
            string back = reader.ReadString();
            Assert.That(reader.Failed, Is.False);
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(back), Is.LessThanOrEqualTo(255));
            Assert.That(back.Length, Is.GreaterThan(100));
        }

        [Test]
        public void Addresses_Parse()
        {
            Assert.That(NetAddress.TryParse("192.168.1.20", 7777, out NetAddress plain));
            Assert.That(plain.ToString(), Is.EqualTo("192.168.1.20:7777"));
            Assert.That(NetAddress.TryParse(" 10.0.0.5:9000 ", 7777, out NetAddress withPort));
            Assert.That(withPort.Port, Is.EqualTo(9000));
            Assert.That(NetAddress.TryParse("localhost", 7777, out NetAddress local));
            Assert.That(local, Is.EqualTo(NetAddress.Loopback(7777)));

            Assert.That(NetAddress.TryParse("", 7777, out _), Is.False);
            Assert.That(NetAddress.TryParse("1.2.3.4:notaport", 7777, out _), Is.False);
            Assert.That(NetAddress.TryParse("1.2.3.4:70000", 7777, out _), Is.False);
            Assert.That(NetAddress.TryParse("1.2.3.999:80", 7777, out _), Is.False);
        }
    }

    public class NetConnectionTests
    {
        const uint Token = 0xABCD1234u;

        /// <summary>Two ends of a connection, joined by an in-process network that can be made to misbehave.</summary>
        sealed class Link
        {
            public readonly INetSocket SocketA, SocketB;
            public readonly NetConnection A, B;
            public readonly List<string> ReceivedByA = new List<string>(), ReceivedByB = new List<string>();
            public double Now;
            readonly byte[] _buffer = new byte[2048];
            readonly NetWriter _writer = new NetWriter();

            public Link(NetConditions conditions = null)
            {
                var network = new MemoryNetwork();
                SocketA = network.Open(1);
                SocketB = network.Open(2);
                NetAddress addressA = SocketA.LocalAddress, addressB = SocketB.LocalAddress;
                if (conditions != null)
                {
                    SocketA = new ConditionedSocket(SocketA, conditions, () => Now, 11);
                    SocketB = new ConditionedSocket(SocketB, conditions, () => Now, 22);
                }
                A = new NetConnection(addressB, Token, 0);
                B = new NetConnection(addressA, Token, 0);
            }

            public void Send(NetConnection from, string text, bool reliable)
            {
                _writer.Reset();
                _writer.WriteString(text);
                from.Send(_writer, reliable);
            }

            /// <summary>Both ends flush and receive, as they would once per tick.</summary>
            public void Pump(int ticks = 1)
            {
                for (int i = 0; i < ticks; i++)
                {
                    Now += 1.0 / 60.0;
                    A.Flush(SocketA, Now);
                    B.Flush(SocketB, Now);
                    while (SocketA.Receive(_buffer, out int length, out _)) A.Receive(_buffer, length, Now, r => ReceivedByA.Add(r.ReadString()));
                    while (SocketB.Receive(_buffer, out int length, out _)) B.Receive(_buffer, length, Now, r => ReceivedByB.Add(r.ReadString()));
                }
            }
        }

        [Test]
        public void OnAGoodLine_EverythingArrives_Once()
        {
            var link = new Link();
            link.Send(link.A, "unreliable", false);
            link.Send(link.A, "reliable", true);
            link.Send(link.B, "back", true);
            link.Pump(5);
            Assert.That(link.ReceivedByB, Is.EqualTo(new[] { "unreliable", "reliable" }));
            Assert.That(link.ReceivedByA, Is.EqualTo(new[] { "back" }));
        }

        [Test]
        public void ReliableMessages_SurviveATerribleLine_InOrder_WithNoRepeats()
        {
            var link = new Link(new NetConditions { latencyMs = 40, jitterMs = 60, lossPercent = 40, duplicatePercent = 20 });
            var expected = new List<string>();
            for (int i = 0; i < 300; i++)
            {
                string text = "message " + i;
                expected.Add(text);
                link.Send(link.A, text, true);
                link.Pump(i % 3 == 0 ? 2 : 1);
            }
            link.Pump(600);
            Assert.That(link.ReceivedByB, Is.EqualTo(expected));
        }

        [Test]
        public void UnreliableMessages_MayBeLost_ButAreNeverRepeated()
        {
            var link = new Link(new NetConditions { latencyMs = 10, jitterMs = 30, lossPercent = 30, duplicatePercent = 50 });
            for (int i = 0; i < 400; i++)
            {
                link.Send(link.A, "u" + i, false);
                link.Pump();
            }
            link.Pump(60);

            Assert.That(link.ReceivedByB.Count, Is.LessThan(400), "some were lost");
            Assert.That(link.ReceivedByB.Count, Is.GreaterThan(100), "most of the rest got through");
            Assert.That(new HashSet<string>(link.ReceivedByB).Count, Is.EqualTo(link.ReceivedByB.Count), "none arrived twice");
        }

        [Test]
        public void RoundTripTime_IsMeasured()
        {
            var link = new Link(new NetConditions { latencyMs = 50 });
            link.Pump(120);
            // 50 ms out and 50 ms in at each end makes 200 ms for the round trip. On top of that a
            // packet only moves when an end is pumped, once a tick, at each of its four stops.
            Assert.That(link.A.Rtt, Is.InRange(0.2f, 0.3f));
            Assert.That(link.A.Loss, Is.LessThan(0.05f));
        }

        [Test]
        public void SequenceNumbers_WrapAround_WithoutLosingAnything()
        {
            var link = new Link();
            // More packets than a 16-bit sequence number can count.
            for (int i = 0; i < 70000; i++)
            {
                if (i % 500 == 0) link.Send(link.A, "r" + i, true);
                link.Pump();
            }
            link.Pump(10);
            Assert.That(link.ReceivedByB.Count, Is.EqualTo(140));
            Assert.That(link.ReceivedByB[139], Is.EqualTo("r69500"));
        }

        [Test]
        public void APacketWithTheWrongToken_IsIgnored()
        {
            var network = new MemoryNetwork();
            INetSocket a = network.Open(1), b = network.Open(2);
            var sender = new NetConnection(b.LocalAddress, 111u, 0);
            var receiver = new NetConnection(a.LocalAddress, 222u, 0);
            var writer = new NetWriter();
            writer.WriteString("hello");
            sender.Send(writer, true);
            sender.Flush(a, 0.1);

            var buffer = new byte[2048];
            int handled = 0;
            Assert.That(b.Receive(buffer, out int length, out _));
            Assert.That(receiver.Receive(buffer, length, 0.1, _ => handled++), Is.False);
            Assert.That(handled, Is.EqualTo(0));
        }

        [Test]
        public void Garbage_NeverThrows()
        {
            var connection = new NetConnection(NetAddress.Loopback(1), Token, 0);
            var random = new System.Random(7);
            var buffer = new byte[600];
            for (int i = 0; i < 5000; i++)
            {
                random.NextBytes(buffer);
                // A believable header, so the junk gets as far as the message parser.
                buffer[0] = (byte)PacketType.Data;
                uint token = Token;
                buffer[1] = (byte)token;
                buffer[2] = (byte)(token >> 8);
                buffer[3] = (byte)(token >> 16);
                buffer[4] = (byte)(token >> 24);
                buffer[5] = (byte)(i + 1);
                buffer[6] = (byte)((i + 1) >> 8);
                int length = random.Next(1, buffer.Length);
                Assert.DoesNotThrow(() => connection.Receive(buffer, length, i, r =>
                {
                    r.ReadString();
                    r.ReadVector3();
                }));
            }
        }

        [Test]
        public void AFloodOfUnansweredReliableMessages_MarksTheConnectionBroken()
        {
            var connection = new NetConnection(NetAddress.Loopback(1), Token, 0);
            var writer = new NetWriter();
            writer.WriteString("nobody is listening");
            for (int i = 0; i < 5000 && !connection.Broken; i++) connection.Send(writer, true);
            Assert.That(connection.Broken, Is.True);
        }
    }

    public class NetMessageTests
    {
        static readonly NetWriter Writer = new NetWriter();
        static readonly NetReader Reader = new NetReader();

        /// <summary>Positions the shared reader just after the message type byte, as a handler would see it.</summary>
        static NetReader Written(NetMessage expected)
        {
            Reader.Reset(Writer.Data, 0, Writer.Length);
            Assert.That((NetMessage)Reader.ReadByte(), Is.EqualTo(expected));
            return Reader;
        }

        [Test]
        public void Commands_AreRoundedOnce_AndThenSurviveTheTripExactly()
        {
            var cmds = new PlayerInputCmd[64];
            var random = new System.Random(3);
            for (int tick = 1; tick <= 40; tick++)
            {
                var cmd = new PlayerInputCmd
                {
                    Tick = tick,
                    Move = new Vector2((float)random.NextDouble() * 3f - 1.5f, (float)random.NextDouble() * 3f - 1.5f),
                    Yaw = (float)random.NextDouble() * 720f - 360f,
                    Pitch = (float)random.NextDouble() * 240f - 120f,
                    Buttons = (InputButtons)random.Next(0, 65536),
                    SelectSlot = (byte)random.Next(0, 12),
                    ViewTick = tick * 1.37f,
                };
                NetMessages.Quantize(ref cmd);

                // Rounding again changes nothing: what the client simulates is what it sends.
                PlayerInputCmd again = cmd;
                NetMessages.Quantize(ref again);
                Assert.That(again.Move, Is.EqualTo(cmd.Move));
                Assert.That(Mathf.Abs(cmd.Move.x), Is.LessThanOrEqualTo(1f));
                Assert.That(Mathf.Abs(cmd.Pitch), Is.LessThan(90f));
                cmds[tick % cmds.Length] = cmd;
            }

            Writer.Reset();
            NetMessages.WriteInput(Writer, cmds, 40, NetMessages.InputRedundancy);
            var received = new PlayerInputCmd[NetMessages.MaxInputsPerMessage];
            int count = NetMessages.ReadInput(Written(NetMessage.Input), received);

            Assert.That(count, Is.EqualTo(NetMessages.InputRedundancy));
            for (int i = 0; i < count; i++)
            {
                PlayerInputCmd sent = cmds[(40 - count + 1 + i) % cmds.Length];
                Assert.That(received[i].Tick, Is.EqualTo(sent.Tick));
                Assert.That(received[i].Move, Is.EqualTo(sent.Move), "movement, bit for bit");
                Assert.That(received[i].Yaw, Is.EqualTo(sent.Yaw));
                Assert.That(received[i].Pitch, Is.EqualTo(sent.Pitch));
                Assert.That(received[i].Buttons, Is.EqualTo(sent.Buttons));
                Assert.That(received[i].SelectSlot, Is.EqualTo(sent.SelectSlot));
                Assert.That(received[i].ViewTick, Is.EqualTo(sent.ViewTick));
            }
        }

        [Test]
        public void ACommand_ClaimingMoreInputsThanAllowed_IsRefused()
        {
            Writer.Reset();
            Writer.WriteByte((byte)NetMessage.Input);
            Writer.WriteInt(100);
            Writer.WriteByte(200);
            var received = new PlayerInputCmd[NetMessages.MaxInputsPerMessage];
            Assert.That(NetMessages.ReadInput(Written(NetMessage.Input), received), Is.EqualTo(0));
            Assert.That(Reader.Failed, Is.True);
        }

        [Test]
        public void Freezing_KeepsTheView_AndDropsEverythingElse()
        {
            var cmd = new PlayerInputCmd { Move = new Vector2(1, 1), Yaw = 42f, Pitch = -10f, Buttons = InputButtons.Fire | InputButtons.Jump, SelectSlot = 2 };
            MatchRules.Freeze(ref cmd);
            Assert.That(cmd.Move, Is.EqualTo(Vector2.zero));
            Assert.That(cmd.Buttons, Is.EqualTo(InputButtons.None));
            Assert.That(cmd.Yaw, Is.EqualTo(42f));
            Assert.That(cmd.Pitch, Is.EqualTo(-10f));
            Assert.That(cmd.SelectSlot, Is.EqualTo(2), "swapping weapons is still allowed");
        }

        [Test]
        public void APlayersFullState_SurvivesTheTrip()
        {
            var state = new PlayerSimState
            {
                Motor = new MotorState
                {
                    Position = new Vector3(1.25f, 2.5f, -3.75f), Velocity = new Vector3(4f, -5f, 6f), Yaw = 123f, Pitch = -45f,
                    Stance = Stance.Sliding, Grounded = true, Sprinting = true, Mantling = true, JustLanded = true,
                    GroundNormal = new Vector3(0f, 1f, 0f), EyeHeight = 0.82f,
                    CoyoteTimer = 0.01f, JumpBufferTimer = 0.02f, SlideTimer = 0.3f, SlideCooldown = 0.4f, TimeSinceSprint = 0.5f,
                    MantleTimer = 0.1f, MantleDuration = 0.3f, MantleCooldown = 0.2f,
                    MantleFrom = new Vector3(7f, 8f, 9f), MantleTo = new Vector3(10f, 11f, 12f),
                    PrevButtons = InputButtons.Crouch | InputButtons.Utility2,
                },
                ActiveSlot = 2, PendingSlot = 1, ThrowingSlot = 1, ThrowTimer = 0.12f, BeltButtons = InputButtons.Utility1,
            };
            for (int i = 0; i < state.Weapons.Length; i++)
            {
                state.Weapons[i] = new WeaponState
                {
                    Phase = WeaponPhase.Reloading, PhaseTimer = 0.5f + i, PhaseDuration = 2f, ReloadElapsed = 0.25f, ReloadTotal = 1.5f,
                    AmmoInMagazine = 17 + i, AmmoReserve = 90 + i, FireCooldown = -0.003f, TriggerBuffer = 0.05f,
                    BurstShotsRemaining = 2, ChargeAmount = 0.75f, MeleePending = i == 2, MeleeTimer = 0.11f,
                    AdsAmount = 0.6f, Bloom = 1.2f, SprayIndex = 9, TimeSinceLastShot = 0.07f,
                    RecoilTarget = new Vector2(0.4f, 2.5f), RecoilCurrent = new Vector2(0.3f, 2.1f),
                    ShotCounter = 123456u + (uint)i, PrevButtons = InputButtons.Fire,
                };
            }
            state.Utilities[0] = new UtilitySlotState { Charges = 2, Cooldown = 0.3f, RechargeTimer = 1.5f };

            Writer.Reset();
            NetMessages.WriteSimState(Writer, state);
            Reader.Reset(Writer.Data, 0, Writer.Length);
            var back = new PlayerSimState();
            NetMessages.ReadSimState(Reader, back);

            Assert.That(Reader.Failed, Is.False);
            Assert.That(Reader.Remaining, Is.EqualTo(0));
            // Structs of plain values: equal means every field came back.
            Assert.That(back.Motor, Is.EqualTo(state.Motor));
            for (int i = 0; i < state.Weapons.Length; i++) Assert.That(back.Weapons[i], Is.EqualTo(state.Weapons[i]), "weapon " + i);
            for (int i = 0; i < state.Utilities.Length; i++) Assert.That(back.Utilities[i], Is.EqualTo(state.Utilities[i]), "utility " + i);
            Assert.That(back.ActiveSlot, Is.EqualTo(2));
            Assert.That(back.PendingSlot, Is.EqualTo(1));
            Assert.That(back.ThrowingSlot, Is.EqualTo(1));
            Assert.That(back.ThrowTimer, Is.EqualTo(0.12f));
            Assert.That(back.BeltButtons, Is.EqualTo(InputButtons.Utility1));
            Assert.That(back.Matches(state), Is.True);
        }

        [Test]
        public void NoSwitchAndNoThrow_ComeBackAsMinusOne()
        {
            var state = new PlayerSimState();
            Writer.Reset();
            NetMessages.WriteSimState(Writer, state);
            Reader.Reset(Writer.Data, 0, Writer.Length);
            var back = new PlayerSimState { PendingSlot = 5, ThrowingSlot = 5 };
            NetMessages.ReadSimState(Reader, back);
            Assert.That(back.PendingSlot, Is.EqualTo(-1));
            Assert.That(back.ThrowingSlot, Is.EqualTo(-1));
        }

        [Test]
        public void PredictionCheck_NamesWhatDiffers_AndForgivesRoundingNoise()
        {
            var mine = new PlayerSimState();
            var server = new PlayerSimState();
            Assert.That(mine.Difference(server), Is.Null);

            server.Motor.Position = new Vector3(0.0005f, 0f, 0f);
            Assert.That(mine.Matches(server), Is.True, "half a millimetre is rounding, not a disagreement");

            server.Motor.Position = new Vector3(0.05f, 0f, 0f);
            Assert.That(mine.Difference(server), Is.EqualTo("position"));

            server.Motor.Position = Vector3.zero;
            server.Weapons[1].AmmoInMagazine = 3;
            Assert.That(mine.Difference(server), Is.EqualTo("ammunition"));

            server.Weapons[1].AmmoInMagazine = 0;
            server.Utilities[0].Charges = 1;
            Assert.That(mine.Difference(server), Is.EqualTo("utility charges"));
        }

        [Test]
        public void Snapshots_CarryTheOwnersFullState_AndOnlyTheOutlineOfOthers()
        {
            var motor = new MotorState { Position = new Vector3(3f, 0f, 4f), Velocity = new Vector3(1f, 0f, 0f), Yaw = 90f, Pitch = 5f, EyeHeight = 1.62f, Grounded = true, Stance = Stance.Crouching };
            var own = new PlayerSimState { Motor = motor, ActiveSlot = 1 };
            own.Weapons[1].AmmoInMagazine = 11;

            var zones = new[]
            {
                new ZoneState { Owner = 0, Capturer = Teams.None, Progress = 0f },
                new ZoneState { Owner = Teams.None, Capturer = 1, Progress = 0.5f, Contested = true },
            };

            Writer.Reset();
            NetMessages.WriteSnapshotHeader(Writer, 5000, 2, zones, 2);
            NetMessages.WriteSnapshotPlayer(Writer, 0, true, true, 3, 74.5f, motor, 1, own, 4321,
                new PlayerStats { Team = 1, Kills = 12, Deaths = 3, Score = 1450 });
            int ownerBytes = Writer.Length;
            NetMessages.WriteSnapshotPlayer(Writer, 1, true, false, 7, 0f, motor, 2, null, 0);
            int otherBytes = Writer.Length - ownerBytes;

            NetReader reader = Written(NetMessage.Snapshot);
            var heard = new ZoneState[NetMessages.MaxZones];
            NetMessages.ReadSnapshotHeader(reader, out int tick, heard, out int zoneCount, out int playerCount);
            Assert.That(tick, Is.EqualTo(5000));
            Assert.That(playerCount, Is.EqualTo(2));
            Assert.That(zoneCount, Is.EqualTo(2));
            Assert.That(heard[0].Owner, Is.EqualTo(0));
            Assert.That(heard[1].Owner, Is.EqualTo(Teams.None));
            Assert.That(heard[1].Capturer, Is.EqualTo(1));
            Assert.That(heard[1].Progress, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(heard[1].Contested, Is.True);

            var player = new SnapshotPlayer();
            NetMessages.ReadSnapshotPlayer(reader, player);
            Assert.That(player.Id, Is.EqualTo(0));
            Assert.That(player.Alive && player.Spawned && player.Grounded);
            Assert.That(player.SpawnCount, Is.EqualTo(3));
            Assert.That(player.Health, Is.EqualTo(74.5f));
            Assert.That(player.Stance, Is.EqualTo(Stance.Crouching));
            Assert.That(player.HasOwn, Is.True);
            Assert.That(player.LastCmdTick, Is.EqualTo(4321));
            Assert.That(player.Own.Weapons[1].AmmoInMagazine, Is.EqualTo(11));
            Assert.That(player.Stats.Team, Is.EqualTo(1));
            Assert.That(player.Stats.Kills, Is.EqualTo(12));
            Assert.That(player.Stats.Deaths, Is.EqualTo(3));
            Assert.That(player.Stats.Score, Is.EqualTo(1450));

            NetMessages.ReadSnapshotPlayer(reader, player);
            Assert.That(player.Id, Is.EqualTo(1));
            Assert.That(player.Alive, Is.False);
            Assert.That(player.HasOwn, Is.False, "nobody is sent an opponent's ammunition or cooldowns");
            Assert.That(player.Position, Is.EqualTo(motor.Position), "where they stand is exact");
            Assert.That(player.Yaw, Is.EqualTo(90f).Within(0.01f), "what only draws them is close enough");
            Assert.That(player.Pitch, Is.EqualTo(5f).Within(0.01f));
            Assert.That(player.EyeHeight, Is.EqualTo(1.62f).Within(0.01f));
            Assert.That(player.Velocity.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(player.ActiveSlot, Is.EqualTo(2));
            Assert.That(reader.Failed, Is.False);
            Assert.That(reader.Remaining, Is.EqualTo(0));
            Assert.That(otherBytes, Is.LessThanOrEqualTo(40), "another player is a few dozen bytes");
            Assert.That(otherBytes, Is.LessThan(ownerBytes / 2));
        }

        [Test]
        public void PackedNumbers_ComeBackExactly()
        {
            Writer.Reset();
            Writer.WriteByte((byte)NetMessage.Snapshot);
            uint[] whole = { 0, 1, 127, 128, 16383, 16384, 3000000000u, uint.MaxValue };
            int[] signed = { 0, -1, 1, -64, 63, -1000000, int.MinValue, int.MaxValue };
            foreach (uint value in whole) Writer.WriteVarUInt(value);
            foreach (int value in signed) Writer.WriteVarInt(value);
            var floats = new float[32];
            floats[0] = 1.5f; floats[3] = -0f; floats[17] = 1e-30f; floats[31] = -123456.789f;
            Writer.WriteSparseFloats(floats, 32);
            int sparseStart = Writer.Length;
            Writer.WriteSparseFloats(new float[5], 5);
            Assert.That(Writer.Length - sparseStart, Is.EqualTo(1), "five zeros cost one byte");
            Writer.WriteAngle(-90f);
            Writer.WriteCentis(-3.14159f);

            NetReader reader = Written(NetMessage.Snapshot);
            foreach (uint value in whole) Assert.That(reader.ReadVarUInt(), Is.EqualTo(value));
            foreach (int value in signed) Assert.That(reader.ReadVarInt(), Is.EqualTo(value));
            var back = new float[32];
            reader.ReadSparseFloats(back, 32);
            for (int i = 0; i < 32; i++)
                Assert.That(BitConverter.SingleToInt32Bits(back[i]), Is.EqualTo(BitConverter.SingleToInt32Bits(floats[i])), "float " + i);
            reader.ReadSparseFloats(back, 5);
            Assert.That(reader.ReadAngle(), Is.EqualTo(270f).Within(0.01f));
            Assert.That(reader.ReadCentis(), Is.EqualTo(-3.14f).Within(0.001f));
            Assert.That(reader.Failed, Is.False);
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }

        [Test]
        public void PackedNumbers_ThatAreBroken_FailTheRead()
        {
            Writer.Reset();
            Writer.WriteByte((byte)NetMessage.Snapshot);
            for (int i = 0; i < 6; i++) Writer.WriteByte(0xFF);     // a number that never ends
            NetReader reader = Written(NetMessage.Snapshot);
            reader.ReadVarUInt();
            Assert.That(reader.Failed, Is.True);

            Writer.Reset();
            Writer.WriteByte((byte)NetMessage.Snapshot);
            Writer.WriteVarUInt(1u << 9);                           // says float 9 follows, of 5
            reader = Written(NetMessage.Snapshot);
            reader.ReadSparseFloats(new float[5], 5);
            Assert.That(reader.Failed, Is.True);
        }

        [Test]
        public void Loadouts_AndEveryEvent_SurviveTheTrip()
        {
            var loadout = new LoadoutData
            {
                name = "Close quarters",
                primary = new WeaponBuildData { weaponId = "smg_a", attachments = new[] { "sight_a", "grip_b" } },
                secondary = new WeaponBuildData { weaponId = "pistol_a" },
                melee = new WeaponBuildData { weaponId = "knife_a" },
                utility = "impact", perkSet = "mover",
            };

            Writer.Reset();
            NetMessages.Write(Writer, new SpawnMessage
            {
                PlayerId = 1, SpawnCount = 9, UnlimitedAmmo = true, RechargeUtility = false, SlotMask = 0b0101, ActiveSlot = 2,
                Loadout = loadout, Position = new Vector3(1f, 2f, 3f), Yaw = 270f,
            });
            SpawnMessage spawn = NetMessages.ReadSpawn(Written(NetMessage.Spawn));
            Assert.That(Reader.Failed, Is.False);
            Assert.That(spawn.PlayerId, Is.EqualTo(1));
            Assert.That(spawn.SpawnCount, Is.EqualTo(9));
            Assert.That(spawn.UnlimitedAmmo, Is.True);
            Assert.That(spawn.RechargeUtility, Is.False);
            Assert.That(spawn.SlotMask, Is.EqualTo(0b0101));
            Assert.That(spawn.ActiveSlot, Is.EqualTo(2));
            Assert.That(spawn.Yaw, Is.EqualTo(270f));
            Assert.That(spawn.Loadout.name, Is.EqualTo("Close quarters"));
            Assert.That(spawn.Loadout.primary.weaponId, Is.EqualTo("smg_a"));
            Assert.That(spawn.Loadout.primary.attachments, Is.EqualTo(new[] { "sight_a", "grip_b" }));
            Assert.That(spawn.Loadout.secondary.attachments, Is.Empty);
            Assert.That(spawn.Loadout.utility, Is.EqualTo("impact"));
            Assert.That(spawn.Loadout.perkSet, Is.EqualTo("mover"));

            Writer.Reset();
            NetMessages.Write(Writer, new MatchState { Phase = MatchPhase.RoundEnd, Round = 4, PhaseEndTick = 123456, Score0 = 2, Score1 = 1, RoundWinner = MatchState.Nobody, MatchWinner = MatchState.Nobody, ActiveZone = -1 });
            MatchState match = NetMessages.ReadMatch(Written(NetMessage.Match));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.RoundEnd));
            Assert.That(match.Round, Is.EqualTo(4));
            Assert.That(match.PhaseEndTick, Is.EqualTo(123456));
            Assert.That(match.Score(0), Is.EqualTo(2));
            Assert.That(match.Score(1), Is.EqualTo(1));
            Assert.That(match.RoundWinner, Is.EqualTo(MatchState.Nobody), "a draw comes back as nobody, not as player 255");
            Assert.That(match.MatchWinner, Is.EqualTo(MatchState.Nobody));
            Assert.That(match.ActiveZone, Is.EqualTo(-1));

            Writer.Reset();
            NetMessages.Write(Writer, new MatchState { Phase = MatchPhase.Live, Score0 = 1999, Score1 = 250, RoundWinner = MatchState.Nobody, MatchWinner = 7, ActiveZone = 2 });
            match = NetMessages.ReadMatch(Written(NetMessage.Match));
            Assert.That(match.Score0, Is.EqualTo(1999), "zone modes score into the hundreds");
            Assert.That(match.MatchWinner, Is.EqualTo(7), "without teams the winner is a player");
            Assert.That(match.ActiveZone, Is.EqualTo(2));

            Writer.Reset();
            MatchSettings rules = MatchSettings.For(GameModeKind.Domination);
            rules.teamSize = 4;
            rules.scoreLimit = 150;
            rules.fillWithBots = true;
            rules.botSkill = 2;
            NetMessages.Write(Writer, new WelcomeMessage { PlayerId = 1, ServerTick = 99, Settings = rules });
            WelcomeMessage welcome = NetMessages.ReadWelcome(Written(NetMessage.Welcome));
            Assert.That(welcome.Settings.mode, Is.EqualTo(GameModeKind.Domination));
            Assert.That(welcome.Settings.arenaId, Is.EqualTo(rules.arenaId));
            Assert.That(welcome.Settings.teamSize, Is.EqualTo(4));
            Assert.That(welcome.Settings.scoreLimit, Is.EqualTo(150));
            Assert.That(welcome.Settings.captureSeconds, Is.EqualTo(rules.captureSeconds));
            Assert.That(welcome.Settings.fillWithBots, Is.True);
            Assert.That(welcome.Settings.botSkill, Is.EqualTo(2));

            Writer.Reset();
            NetMessages.Write(Writer, new PlayerInfoMessage { PlayerId = 6, Present = true, Name = "Bot Cedar", Team = 1, Bot = true });
            PlayerInfoMessage info = NetMessages.ReadPlayerInfo(Written(NetMessage.PlayerInfo));
            Assert.That(info.PlayerId, Is.EqualTo(6));
            Assert.That(info.Name, Is.EqualTo("Bot Cedar"));
            Assert.That(info.Team, Is.EqualTo(1));
            Assert.That(info.Bot, Is.True);

            Writer.Reset();
            NetMessages.Write(Writer, new RearmMessage { PlayerId = 3, SpawnCount = 41, SlotMask = 0b0110, ActiveSlot = 1, Loadout = loadout });
            RearmMessage rearm = NetMessages.ReadRearm(Written(NetMessage.Rearm));
            Assert.That(rearm.PlayerId, Is.EqualTo(3));
            Assert.That(rearm.SpawnCount, Is.EqualTo(41));
            Assert.That(rearm.SlotMask, Is.EqualTo(0b0110));
            Assert.That(rearm.ActiveSlot, Is.EqualTo(1));
            Assert.That(rearm.Loadout.primary.weaponId, Is.EqualTo("smg_a"));

            Writer.Reset();
            NetMessages.Write(Writer, new KillMessage { Killer = KillMessage.NoKiller, Victim = 1, WeaponId = "ar_x", Headshot = true });
            KillMessage kill = NetMessages.ReadKill(Written(NetMessage.Kill));
            Assert.That(kill.Killer, Is.EqualTo(KillMessage.NoKiller));
            Assert.That(kill.WeaponId, Is.EqualTo("ar_x"));
            Assert.That(kill.Headshot, Is.True);

            Writer.Reset();
            NetMessages.Write(Writer, new HitMessage { Victim = 1, Damage = 37.5f, Zone = HitZone.Head, Killed = true, Point = new Vector3(4f, 5f, 6f) });
            HitMessage hit = NetMessages.ReadHit(Written(NetMessage.Hit));
            Assert.That(hit.Damage, Is.EqualTo(37.5f));
            Assert.That(hit.Zone, Is.EqualTo(HitZone.Head));
            Assert.That(hit.Killed, Is.True);
            Assert.That(hit.Point, Is.EqualTo(new Vector3(4f, 5f, 6f)));

            Writer.Reset();
            NetMessages.Write(Writer, new ShotMessage { Shooter = 1, Slot = 2, Kind = ShotKind.Melee, First = true, Origin = Vector3.up, End = Vector3.forward, Normal = Vector3.back, HitWorld = true });
            ShotMessage shot = NetMessages.ReadShot(Written(NetMessage.Shot));
            Assert.That(shot.Kind, Is.EqualTo(ShotKind.Melee));
            Assert.That(shot.Slot, Is.EqualTo(2));
            Assert.That(shot.First && shot.HitWorld);
            Assert.That(shot.End, Is.EqualTo(Vector3.forward));

            Writer.Reset();
            NetMessages.Write(Writer, new ProjectileSpawnMessage { Id = 77, Owner = 1, Source = ProjectileSpawnMessage.UtilityBase + 1, Origin = Vector3.one, Direction = Vector3.forward, Tick = 4242 });
            ProjectileSpawnMessage launched = NetMessages.ReadProjectileSpawn(Written(NetMessage.ProjectileSpawn));
            Assert.That(launched.Id, Is.EqualTo(77));
            Assert.That(launched.Source, Is.EqualTo(ProjectileSpawnMessage.UtilityBase + 1));
            Assert.That(launched.Tick, Is.EqualTo(4242));

            Writer.Reset();
            NetMessages.Write(Writer, new ProjectileEndMessage { Id = 77, Kind = ProjectileEventKind.Explosion, Point = Vector3.one, Normal = Vector3.up, Radius = 4.5f, HitTarget = true });
            ProjectileEndMessage ended = NetMessages.ReadProjectileEnd(Written(NetMessage.ProjectileEnd));
            Assert.That(ended.Kind, Is.EqualTo(ProjectileEventKind.Explosion));
            Assert.That(ended.Radius, Is.EqualTo(4.5f));
            Assert.That(ended.HitTarget, Is.True);
            Assert.That(Reader.Failed, Is.False);
        }

        [Test]
        public void TruncatedMessages_FailInsteadOfThrowing()
        {
            Writer.Reset();
            NetMessages.Write(Writer, new SpawnMessage { PlayerId = 0, Loadout = new LoadoutData { name = "x" }, Position = Vector3.one });
            for (int length = 1; length < Writer.Length; length++)
            {
                Reader.Reset(Writer.Data, 1, length - 1);
                Assert.DoesNotThrow(() => NetMessages.ReadSpawn(Reader));
                Assert.That(Reader.Failed, Is.True, "cut to " + length + " bytes");
            }
        }
    }

    public class MatchRuleTests
    {
        [Test]
        public void Health_IsTakenOnlyWhenTheMatchAllowsIt()
        {
            var vitals = new PlayerVitals(1) { Max = 100f };
            int events = 0;
            vitals.Damaged += (_, __, ___) => events++;
            Assert.That(vitals.IsAlive, Is.False, "nobody is alive until they are placed");

            vitals.Revive();
            vitals.Invulnerable = true;
            Assert.That(vitals.ApplyDamage(new DamageInfo { Amount = 40f }).Applied, Is.EqualTo(0f));
            Assert.That(vitals.Current, Is.EqualTo(100f));

            vitals.Invulnerable = false;
            Assert.That(vitals.ApplyDamage(new DamageInfo { Amount = 40f }).Applied, Is.EqualTo(40f));
            DamageResult last = vitals.ApplyDamage(new DamageInfo { Amount = 500f });
            Assert.That(last.Applied, Is.EqualTo(60f), "overkill only counts what was left");
            Assert.That(last.Killed, Is.True);
            Assert.That(vitals.IsAlive, Is.False);
            Assert.That(vitals.ApplyDamage(new DamageInfo { Amount = 10f }).Applied, Is.EqualTo(0f), "the dead take no more");
            Assert.That(events, Is.EqualTo(2));
        }

        [Test]
        public void Angles_BlendTheShortWayRound()
        {
            Assert.That(BodyPose.LerpAngle(350f, 10f, 0.5f), Is.EqualTo(360f).Within(0.001f));
            Assert.That(BodyPose.LerpAngle(10f, 350f, 0.5f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(BodyPose.LerpAngle(0f, 90f, 0.25f), Is.EqualTo(22.5f).Within(0.001f));
            Assert.That(BodyPose.LerpAngle(-170f, 170f, 0.5f), Is.EqualTo(-180f).Within(0.001f));
        }

        [Test]
        public void TheArena_IsTheSameFromEitherEnd()
        {
            ArenaLayout arena = ArenaLayout.RelayYard();
            Assert.That(ArenaLayout.Find("anything"), Is.Not.Null);
            Assert.That(arena.Sides.Length, Is.EqualTo(2));
            Assert.That(arena.Sides[0].Length, Is.EqualTo(arena.Sides[1].Length));

            // Turn the whole arena half a turn about its centre: every block must land on a block.
            foreach (ArenaBox box in arena.Boxes)
            {
                var mirrored = new Vector3(-box.Center.x, box.Center.y, -box.Center.z);
                bool found = false;
                foreach (ArenaBox other in arena.Boxes)
                    found |= (other.Center - mirrored).sqrMagnitude < 0.0001f && (other.Size - box.Size).sqrMagnitude < 0.0001f;
                Assert.That(found, Is.True, "nothing mirrors the block at " + box.Center);
            }

            for (int i = 0; i < arena.Sides[0].Length; i++)
            {
                ArenaSpawn west = arena.Sides[0][i], east = arena.Sides[1][i];
                Assert.That(east.Position.x, Is.EqualTo(-west.Position.x).Within(0.001f));
                Assert.That(east.Position.z, Is.EqualTo(-west.Position.z).Within(0.001f));
                Assert.That(east.Yaw - west.Yaw, Is.EqualTo(180f).Within(0.001f));
            }
        }

        [Test]
        public void SpawnPoints_StandOnSomething_InTheOpen_FacingTheMiddle()
        {
            ArenaLayout arena = ArenaLayout.RelayYard();
            const float radius = 0.35f, height = 1.8f;

            foreach (ArenaSpawn[] side in arena.Sides)
            {
                foreach (ArenaSpawn spawn in side)
                {
                    Vector3 feet = spawn.Position;
                    bool supported = false;
                    foreach (ArenaBox box in arena.Boxes)
                    {
                        Vector3 min = box.Center - box.Size * 0.5f, max = box.Center + box.Size * 0.5f;
                        bool over = feet.x > min.x && feet.x < max.x && feet.z > min.z && feet.z < max.z;
                        if (over && Mathf.Abs(max.y - feet.y) < 0.001f) supported = true;

                        // No block may overlap the space the player's capsule takes up.
                        bool overlaps = feet.x + radius > min.x && feet.x - radius < max.x &&
                                        feet.z + radius > min.z && feet.z - radius < max.z &&
                                        feet.y + height > min.y + 0.001f && feet.y < max.y - 0.001f;
                        Assert.That(overlaps, Is.False, "spawn at " + feet + " is inside the block at " + box.Center);
                    }
                    Assert.That(supported, Is.True, "spawn at " + feet + " has nothing under it");

                    Vector3 facing = AimMath.YawForward(spawn.Yaw);
                    Vector3 toCentre = new Vector3(-feet.x, 0f, -feet.z).normalized;
                    Assert.That(Vector3.Dot(facing, toCentre), Is.GreaterThan(0.7f), "spawn at " + feet + " faces away from the fight");
                }
            }
        }

        [Test]
        public void ABadConnection_BehavesAsAsked()
        {
            double now = 0;
            var network = new MemoryNetwork();
            INetSocket receiver = network.Open(1);
            var sender = new ConditionedSocket(network.Open(2), new NetConditions { latencyMs = 100, lossPercent = 25 }, () => now, 5);
            var buffer = new byte[64];

            for (int i = 0; i < 2000; i++) sender.Send(receiver.LocalAddress, buffer, 8);
            Assert.That(receiver.Receive(buffer, out _, out _), Is.False, "nothing arrives before the delay is up");

            now = 0.099;
            sender.Receive(buffer, out _, out _);
            Assert.That(receiver.Receive(buffer, out _, out _), Is.False);

            now = 0.101;
            sender.Receive(buffer, out _, out _);       // gives the wrapper a chance to release what is due
            int arrived = 0;
            while (receiver.Receive(buffer, out _, out _)) arrived++;
            Assert.That(arrived, Is.InRange(1350, 1650), "about a quarter went missing");
        }
    }
}
