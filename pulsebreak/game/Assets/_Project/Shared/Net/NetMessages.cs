using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Net
{
    /// <summary>The first byte of every message inside a data packet.</summary>
    public enum NetMessage : byte
    {
        // Client to server.
        /// <summary>Reliable. Name and loadout, sent once after connecting.</summary>
        Hello = 1,
        /// <summary>Reliable. A new loadout, used from the next time the player is placed.</summary>
        Loadout = 2,
        /// <summary>Unreliable. The newest input commands, repeated so a lost packet costs nothing.</summary>
        Input = 3,

        // Server to client.
        Welcome = 16,
        PlayerInfo = 17,
        Spawn = 18,
        /// <summary>Unreliable. Where everyone is, and the receiver's own full state for checking its prediction.</summary>
        Snapshot = 19,
        Match = 20,
        Kill = 21,
        /// <summary>Reliable, to the attacker only: a shot of yours did damage.</summary>
        Hit = 22,
        /// <summary>Unreliable, to everyone but the shooter: draw a tracer.</summary>
        Shot = 23,
        ProjectileSpawn = 24,
        ProjectileEnd = 25,
    }

    public struct WelcomeMessage
    {
        public int PlayerId;
        public string ArenaId;
        public int ServerTick;
        public DuelConfig Config;
    }

    public struct PlayerInfoMessage
    {
        public int PlayerId;
        public bool Present;
        public string Name;
    }

    public struct SpawnMessage
    {
        public int PlayerId;
        /// <summary>Goes up every time the player is placed. Snapshots carry it too, so old ones can be told apart.</summary>
        public byte SpawnCount;
        /// <summary>Placed with nobody to fight: ammunition and utility never run out.</summary>
        public bool Warmup;
        public LoadoutData Loadout;
        public Vector3 Position;
        public float Yaw;
    }

    public struct KillMessage
    {
        public const int NoKiller = 255;
        public int Killer;
        public int Victim;
        public string WeaponId;
        public bool Headshot;
    }

    public struct HitMessage
    {
        public int Victim;
        public float Damage;
        public HitZone Zone;
        public bool Killed;
        public Vector3 Point;
    }

    public struct ShotMessage
    {
        public int Shooter;
        public int Slot;
        public ShotKind Kind;
        /// <summary>The first pellet of a shot: play the sound and the flash once.</summary>
        public bool First;
        public Vector3 Origin;
        public Vector3 End;
        public Vector3 Normal;
        public bool HitWorld;
    }

    public struct ProjectileSpawnMessage
    {
        public int Id;
        public int Owner;
        /// <summary>0..2 a weapon slot, 3..4 a utility slot.</summary>
        public int Source;
        public Vector3 Origin;
        public Vector3 Direction;
        public int Tick;

        public const int UtilityBase = Loadout.WeaponSlots;
    }

    public struct ProjectileEndMessage
    {
        public int Id;
        public ProjectileEventKind Kind;
        public Vector3 Point;
        public Vector3 Normal;
        public float Radius;
        public bool HitTarget;
    }

    /// <summary>One player as seen in a snapshot.</summary>
    public sealed class SnapshotPlayer
    {
        public int Id;
        public bool Spawned;
        public bool Alive;
        public byte SpawnCount;
        public float Health;

        // What anyone can see.
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw, Pitch;
        public float EyeHeight;
        public float LeanOffset;
        public Stance Stance;
        public bool Grounded, Sprinting;
        public int ActiveSlot;

        // Only in the snapshot sent to this player.
        public bool HasOwn;
        /// <summary>The last of this player's commands the server had run when it took the snapshot.</summary>
        public int LastCmdTick;
        public readonly PlayerSimState Own = new PlayerSimState();
    }

    /// <summary>Turns messages into bytes and back. Every read is paired with the write above it.</summary>
    public static class NetMessages
    {
        public const int InputRedundancy = 6;
        public const int MaxInputsPerMessage = 16;
        const int MaxAttachments = 8;

        // ------------------------------------------------------------------ input

        /// <summary>
        /// Rounds a command to exactly what will survive the trip to the server. The client
        /// simulates the rounded command, so its prediction uses the very same numbers.
        /// </summary>
        public static void Quantize(ref PlayerInputCmd cmd)
        {
            cmd.Move = new Vector2(Unpack(Pack(cmd.Move.x)), Unpack(Pack(cmd.Move.y)));
            MatchRules.Sanitize(ref cmd);
        }

        static sbyte Pack(float axis) => (sbyte)Mathf.RoundToInt(Mathf.Clamp(axis, -1f, 1f) * 127f);
        static float Unpack(sbyte packed) => packed / 127f;

        /// <param name="cmds">A ring of recent commands, indexed by tick modulo its length.</param>
        public static void WriteInput(NetWriter writer, PlayerInputCmd[] cmds, int newestTick, int count)
        {
            writer.WriteByte((byte)NetMessage.Input);
            writer.WriteInt(newestTick);
            writer.WriteByte((byte)count);
            for (int tick = newestTick - count + 1; tick <= newestTick; tick++)
            {
                PlayerInputCmd cmd = cmds[tick % cmds.Length];
                writer.WriteByte((byte)Pack(cmd.Move.x));
                writer.WriteByte((byte)Pack(cmd.Move.y));
                writer.WriteFloat(cmd.Yaw);
                writer.WriteFloat(cmd.Pitch);
                writer.WriteUShort((ushort)cmd.Buttons);
                writer.WriteByte(cmd.SelectSlot);
                writer.WriteFloat(cmd.ViewTick);
            }
        }

        /// <returns>How many commands were read into <paramref name="into"/>, oldest first.</returns>
        public static int ReadInput(NetReader reader, PlayerInputCmd[] into)
        {
            int newestTick = reader.ReadInt();
            int count = reader.ReadByte();
            if (count > into.Length || count > MaxInputsPerMessage)
            {
                reader.Fail();
                return 0;
            }

            for (int i = 0; i < count; i++)
            {
                var cmd = new PlayerInputCmd
                {
                    Tick = newestTick - (count - 1 - i),
                    Move = new Vector2(Unpack((sbyte)reader.ReadByte()), Unpack((sbyte)reader.ReadByte())),
                    Yaw = reader.ReadFloat(),
                    Pitch = reader.ReadFloat(),
                    Buttons = (InputButtons)reader.ReadUShort(),
                    SelectSlot = reader.ReadByte(),
                    ViewTick = reader.ReadFloat(),
                };
                MatchRules.Sanitize(ref cmd);
                into[i] = cmd;
            }
            return reader.Failed ? 0 : count;
        }

        // ------------------------------------------------------------------ loadouts

        public static void WriteLoadout(NetWriter writer, LoadoutData data)
        {
            writer.WriteString(data.name);
            for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
            {
                WeaponBuildData weapon = data.GetWeapon(slot);
                writer.WriteString(weapon.weaponId);
                int count = Mathf.Min(weapon.attachments != null ? weapon.attachments.Length : 0, MaxAttachments);
                writer.WriteByte((byte)count);
                for (int i = 0; i < count; i++) writer.WriteString(weapon.attachments[i]);
            }
            writer.WriteString(data.utility1);
            writer.WriteString(data.utility2);
            writer.WriteString(data.perkSet);
        }

        public static LoadoutData ReadLoadout(NetReader reader)
        {
            var data = new LoadoutData { name = reader.ReadString() };
            for (int slot = 0; slot < Loadout.WeaponSlots; slot++)
            {
                WeaponBuildData weapon = data.GetWeapon(slot);
                weapon.weaponId = reader.ReadString();
                int count = reader.ReadByte();
                if (count > MaxAttachments)
                {
                    reader.Fail();
                    return data;
                }
                weapon.attachments = new string[count];
                for (int i = 0; i < count; i++) weapon.attachments[i] = reader.ReadString();
            }
            data.utility1 = reader.ReadString();
            data.utility2 = reader.ReadString();
            data.perkSet = reader.ReadString();
            return data;
        }

        public static void WriteHello(NetWriter writer, string name, LoadoutData loadout)
        {
            writer.WriteByte((byte)NetMessage.Hello);
            writer.WriteString(name);
            WriteLoadout(writer, loadout);
        }

        public static void WriteLoadoutChange(NetWriter writer, LoadoutData loadout)
        {
            writer.WriteByte((byte)NetMessage.Loadout);
            WriteLoadout(writer, loadout);
        }

        // ------------------------------------------------------------------ server events

        public static void Write(NetWriter writer, in WelcomeMessage message)
        {
            writer.WriteByte((byte)NetMessage.Welcome);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteString(message.ArenaId);
            writer.WriteInt(message.ServerTick);
            writer.WriteByte((byte)message.Config.roundsToWin);
            writer.WriteFloat(message.Config.countdownSeconds);
            writer.WriteFloat(message.Config.roundSeconds);
            writer.WriteFloat(message.Config.roundEndSeconds);
            writer.WriteFloat(message.Config.matchEndSeconds);
            writer.WriteFloat(message.Config.maxHealth);
        }

        public static WelcomeMessage ReadWelcome(NetReader reader) => new WelcomeMessage
        {
            PlayerId = reader.ReadByte(),
            ArenaId = reader.ReadString(),
            ServerTick = reader.ReadInt(),
            Config = new DuelConfig
            {
                roundsToWin = reader.ReadByte(),
                countdownSeconds = reader.ReadFloat(),
                roundSeconds = reader.ReadFloat(),
                roundEndSeconds = reader.ReadFloat(),
                matchEndSeconds = reader.ReadFloat(),
                maxHealth = reader.ReadFloat(),
            },
        };

        public static void Write(NetWriter writer, in PlayerInfoMessage message)
        {
            writer.WriteByte((byte)NetMessage.PlayerInfo);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteBool(message.Present);
            writer.WriteString(message.Name);
        }

        public static PlayerInfoMessage ReadPlayerInfo(NetReader reader) => new PlayerInfoMessage
        {
            PlayerId = reader.ReadByte(),
            Present = reader.ReadBool(),
            Name = reader.ReadString(),
        };

        public static void Write(NetWriter writer, in SpawnMessage message)
        {
            writer.WriteByte((byte)NetMessage.Spawn);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteByte(message.SpawnCount);
            writer.WriteBool(message.Warmup);
            WriteLoadout(writer, message.Loadout);
            writer.WriteVector3(message.Position);
            writer.WriteFloat(message.Yaw);
        }

        public static SpawnMessage ReadSpawn(NetReader reader) => new SpawnMessage
        {
            PlayerId = reader.ReadByte(),
            SpawnCount = reader.ReadByte(),
            Warmup = reader.ReadBool(),
            Loadout = ReadLoadout(reader),
            Position = reader.ReadVector3(),
            Yaw = reader.ReadFloat(),
        };

        public static void Write(NetWriter writer, in MatchState state)
        {
            writer.WriteByte((byte)NetMessage.Match);
            writer.WriteByte((byte)state.Phase);
            writer.WriteByte((byte)state.Round);
            writer.WriteInt(state.PhaseEndTick);
            writer.WriteByte((byte)state.Score0);
            writer.WriteByte((byte)state.Score1);
            writer.WriteByte((byte)(sbyte)state.RoundWinner);
            writer.WriteByte((byte)(sbyte)state.MatchWinner);
        }

        public static MatchState ReadMatch(NetReader reader)
        {
            var state = new MatchState
            {
                Phase = (MatchPhase)reader.ReadByte(),
                Round = reader.ReadByte(),
                PhaseEndTick = reader.ReadInt(),
                Score0 = reader.ReadByte(),
                Score1 = reader.ReadByte(),
                RoundWinner = (sbyte)reader.ReadByte(),
                MatchWinner = (sbyte)reader.ReadByte(),
            };
            if (state.Phase > MatchPhase.MatchEnd) reader.Fail();
            return state;
        }

        public static void Write(NetWriter writer, in KillMessage message)
        {
            writer.WriteByte((byte)NetMessage.Kill);
            writer.WriteByte((byte)message.Killer);
            writer.WriteByte((byte)message.Victim);
            writer.WriteString(message.WeaponId);
            writer.WriteBool(message.Headshot);
        }

        public static KillMessage ReadKill(NetReader reader) => new KillMessage
        {
            Killer = reader.ReadByte(),
            Victim = reader.ReadByte(),
            WeaponId = reader.ReadString(),
            Headshot = reader.ReadBool(),
        };

        public static void Write(NetWriter writer, in HitMessage message)
        {
            writer.WriteByte((byte)NetMessage.Hit);
            writer.WriteByte((byte)message.Victim);
            writer.WriteFloat(message.Damage);
            writer.WriteByte((byte)message.Zone);
            writer.WriteBool(message.Killed);
            writer.WriteVector3(message.Point);
        }

        public static HitMessage ReadHit(NetReader reader) => new HitMessage
        {
            Victim = reader.ReadByte(),
            Damage = reader.ReadFloat(),
            Zone = (HitZone)reader.ReadByte(),
            Killed = reader.ReadBool(),
            Point = reader.ReadVector3(),
        };

        public static void Write(NetWriter writer, in ShotMessage message)
        {
            writer.WriteByte((byte)NetMessage.Shot);
            writer.WriteByte((byte)message.Shooter);
            writer.WriteByte((byte)message.Slot);
            writer.WriteByte((byte)message.Kind);
            writer.WriteBool(message.First);
            writer.WriteVector3(message.Origin);
            writer.WriteVector3(message.End);
            writer.WriteVector3(message.Normal);
            writer.WriteBool(message.HitWorld);
        }

        public static ShotMessage ReadShot(NetReader reader) => new ShotMessage
        {
            Shooter = reader.ReadByte(),
            Slot = reader.ReadByte(),
            Kind = (ShotKind)reader.ReadByte(),
            First = reader.ReadBool(),
            Origin = reader.ReadVector3(),
            End = reader.ReadVector3(),
            Normal = reader.ReadVector3(),
            HitWorld = reader.ReadBool(),
        };

        public static void Write(NetWriter writer, in ProjectileSpawnMessage message)
        {
            writer.WriteByte((byte)NetMessage.ProjectileSpawn);
            writer.WriteInt(message.Id);
            writer.WriteByte((byte)message.Owner);
            writer.WriteByte((byte)message.Source);
            writer.WriteVector3(message.Origin);
            writer.WriteVector3(message.Direction);
            writer.WriteInt(message.Tick);
        }

        public static ProjectileSpawnMessage ReadProjectileSpawn(NetReader reader) => new ProjectileSpawnMessage
        {
            Id = reader.ReadInt(),
            Owner = reader.ReadByte(),
            Source = reader.ReadByte(),
            Origin = reader.ReadVector3(),
            Direction = reader.ReadVector3(),
            Tick = reader.ReadInt(),
        };

        public static void Write(NetWriter writer, in ProjectileEndMessage message)
        {
            writer.WriteByte((byte)NetMessage.ProjectileEnd);
            writer.WriteInt(message.Id);
            writer.WriteByte((byte)message.Kind);
            writer.WriteVector3(message.Point);
            writer.WriteVector3(message.Normal);
            writer.WriteFloat(message.Radius);
            writer.WriteBool(message.HitTarget);
        }

        public static ProjectileEndMessage ReadProjectileEnd(NetReader reader) => new ProjectileEndMessage
        {
            Id = reader.ReadInt(),
            Kind = (ProjectileEventKind)reader.ReadByte(),
            Point = reader.ReadVector3(),
            Normal = reader.ReadVector3(),
            Radius = reader.ReadFloat(),
            HitTarget = reader.ReadBool(),
        };

        // ------------------------------------------------------------------ snapshots

        public static void WriteSnapshotHeader(NetWriter writer, int serverTick, int playerCount)
        {
            writer.WriteByte((byte)NetMessage.Snapshot);
            writer.WriteInt(serverTick);
            writer.WriteByte((byte)playerCount);
        }

        /// <param name="own">The player's full state, when this snapshot is going to that player. Otherwise null.</param>
        public static void WriteSnapshotPlayer(NetWriter writer, int id, bool spawned, bool alive, byte spawnCount, float health,
            in MotorState motor, int activeSlot, PlayerSimState own, int lastCmdTick)
        {
            writer.WriteByte((byte)id);
            writer.WriteByte((byte)((spawned ? 1 : 0) | (alive ? 2 : 0) | (motor.Grounded ? 4 : 0) |
                                    (motor.Sprinting ? 8 : 0) | (own != null ? 16 : 0)));
            writer.WriteByte(spawnCount);
            writer.WriteFloat(health);
            writer.WriteVector3(motor.Position);
            writer.WriteVector3(motor.Velocity);
            writer.WriteFloat(motor.Yaw);
            writer.WriteFloat(motor.Pitch);
            writer.WriteFloat(motor.EyeHeight);
            writer.WriteFloat(motor.LeanOffset);
            writer.WriteByte((byte)motor.Stance);
            writer.WriteByte((byte)activeSlot);

            if (own == null) return;
            writer.WriteInt(lastCmdTick);
            WriteSimState(writer, own);
        }

        public static void ReadSnapshotPlayer(NetReader reader, SnapshotPlayer into)
        {
            into.Id = reader.ReadByte();
            int flags = reader.ReadByte();
            into.Spawned = (flags & 1) != 0;
            into.Alive = (flags & 2) != 0;
            into.Grounded = (flags & 4) != 0;
            into.Sprinting = (flags & 8) != 0;
            into.HasOwn = (flags & 16) != 0;
            into.SpawnCount = reader.ReadByte();
            into.Health = reader.ReadFloat();
            into.Position = reader.ReadVector3();
            into.Velocity = reader.ReadVector3();
            into.Yaw = reader.ReadFloat();
            into.Pitch = reader.ReadFloat();
            into.EyeHeight = reader.ReadFloat();
            into.LeanOffset = reader.ReadFloat();
            into.Stance = (Stance)reader.ReadByte();
            into.ActiveSlot = reader.ReadByte();

            if (!into.HasOwn) return;
            into.LastCmdTick = reader.ReadInt();
            ReadSimState(reader, into.Own);
        }

        public static void WriteSimState(NetWriter writer, PlayerSimState state)
        {
            WriteMotor(writer, state.Motor);
            writer.WriteByte((byte)state.ActiveSlot);
            writer.WriteByte((byte)(sbyte)state.PendingSlot);
            for (int i = 0; i < state.Weapons.Length; i++) WriteWeapon(writer, state.Weapons[i]);
            for (int i = 0; i < state.Utilities.Length; i++)
            {
                writer.WriteByte((byte)state.Utilities[i].Charges);
                writer.WriteFloat(state.Utilities[i].Cooldown);
                writer.WriteFloat(state.Utilities[i].RechargeTimer);
            }
            writer.WriteByte((byte)(sbyte)state.ThrowingSlot);
            writer.WriteFloat(state.ThrowTimer);
            writer.WriteUShort((ushort)state.BeltButtons);
        }

        public static void ReadSimState(NetReader reader, PlayerSimState state)
        {
            state.Motor = ReadMotor(reader);
            state.ActiveSlot = reader.ReadByte();
            state.PendingSlot = (sbyte)reader.ReadByte();
            for (int i = 0; i < state.Weapons.Length; i++) state.Weapons[i] = ReadWeapon(reader);
            for (int i = 0; i < state.Utilities.Length; i++)
            {
                state.Utilities[i].Charges = reader.ReadByte();
                state.Utilities[i].Cooldown = reader.ReadFloat();
                state.Utilities[i].RechargeTimer = reader.ReadFloat();
            }
            state.ThrowingSlot = (sbyte)reader.ReadByte();
            state.ThrowTimer = reader.ReadFloat();
            state.BeltButtons = (InputButtons)reader.ReadUShort();
        }

        static void WriteMotor(NetWriter writer, in MotorState s)
        {
            writer.WriteVector3(s.Position);
            writer.WriteVector3(s.Velocity);
            writer.WriteFloat(s.Yaw);
            writer.WriteFloat(s.Pitch);
            writer.WriteByte((byte)s.Stance);
            writer.WriteByte((byte)((s.Grounded ? 1 : 0) | (s.Sprinting ? 2 : 0) | (s.Mantling ? 4 : 0) | (s.JustLanded ? 8 : 0)));
            writer.WriteVector3(s.GroundNormal);
            writer.WriteFloat(s.EyeHeight);
            writer.WriteFloat(s.Lean);
            writer.WriteFloat(s.LeanOffset);
            writer.WriteFloat(s.CoyoteTimer);
            writer.WriteFloat(s.JumpBufferTimer);
            writer.WriteFloat(s.SlideTimer);
            writer.WriteFloat(s.SlideCooldown);
            writer.WriteFloat(s.TimeSinceSprint);
            writer.WriteFloat(s.MantleTimer);
            writer.WriteFloat(s.MantleDuration);
            writer.WriteFloat(s.MantleCooldown);
            writer.WriteVector3(s.MantleFrom);
            writer.WriteVector3(s.MantleTo);
            writer.WriteUShort((ushort)s.PrevButtons);
        }

        static MotorState ReadMotor(NetReader reader)
        {
            var s = new MotorState
            {
                Position = reader.ReadVector3(),
                Velocity = reader.ReadVector3(),
                Yaw = reader.ReadFloat(),
                Pitch = reader.ReadFloat(),
                Stance = (Stance)reader.ReadByte(),
            };
            int flags = reader.ReadByte();
            s.Grounded = (flags & 1) != 0;
            s.Sprinting = (flags & 2) != 0;
            s.Mantling = (flags & 4) != 0;
            s.JustLanded = (flags & 8) != 0;
            s.GroundNormal = reader.ReadVector3();
            s.EyeHeight = reader.ReadFloat();
            s.Lean = reader.ReadFloat();
            s.LeanOffset = reader.ReadFloat();
            s.CoyoteTimer = reader.ReadFloat();
            s.JumpBufferTimer = reader.ReadFloat();
            s.SlideTimer = reader.ReadFloat();
            s.SlideCooldown = reader.ReadFloat();
            s.TimeSinceSprint = reader.ReadFloat();
            s.MantleTimer = reader.ReadFloat();
            s.MantleDuration = reader.ReadFloat();
            s.MantleCooldown = reader.ReadFloat();
            s.MantleFrom = reader.ReadVector3();
            s.MantleTo = reader.ReadVector3();
            s.PrevButtons = (InputButtons)reader.ReadUShort();
            if (s.Stance > Stance.Sliding) reader.Fail();
            return s;
        }

        static void WriteWeapon(NetWriter writer, in WeaponState s)
        {
            writer.WriteByte((byte)s.Phase);
            writer.WriteFloat(s.PhaseTimer);
            writer.WriteFloat(s.PhaseDuration);
            writer.WriteFloat(s.ReloadElapsed);
            writer.WriteFloat(s.ReloadTotal);
            writer.WriteInt(s.AmmoInMagazine);
            writer.WriteInt(s.AmmoReserve);
            writer.WriteFloat(s.FireCooldown);
            writer.WriteFloat(s.TriggerBuffer);
            writer.WriteInt(s.BurstShotsRemaining);
            writer.WriteFloat(s.ChargeAmount);
            writer.WriteBool(s.MeleePending);
            writer.WriteFloat(s.MeleeTimer);
            writer.WriteFloat(s.AdsAmount);
            writer.WriteFloat(s.Bloom);
            writer.WriteInt(s.SprayIndex);
            writer.WriteFloat(s.TimeSinceLastShot);
            writer.WriteVector2(s.RecoilTarget);
            writer.WriteVector2(s.RecoilCurrent);
            writer.WriteUInt(s.ShotCounter);
            writer.WriteUShort((ushort)s.PrevButtons);
        }

        static WeaponState ReadWeapon(NetReader reader)
        {
            var s = new WeaponState
            {
                Phase = (WeaponPhase)reader.ReadByte(),
                PhaseTimer = reader.ReadFloat(),
                PhaseDuration = reader.ReadFloat(),
                ReloadElapsed = reader.ReadFloat(),
                ReloadTotal = reader.ReadFloat(),
                AmmoInMagazine = reader.ReadInt(),
                AmmoReserve = reader.ReadInt(),
                FireCooldown = reader.ReadFloat(),
                TriggerBuffer = reader.ReadFloat(),
                BurstShotsRemaining = reader.ReadInt(),
                ChargeAmount = reader.ReadFloat(),
                MeleePending = reader.ReadBool(),
                MeleeTimer = reader.ReadFloat(),
                AdsAmount = reader.ReadFloat(),
                Bloom = reader.ReadFloat(),
                SprayIndex = reader.ReadInt(),
                TimeSinceLastShot = reader.ReadFloat(),
                RecoilTarget = reader.ReadVector2(),
                RecoilCurrent = reader.ReadVector2(),
                ShotCounter = reader.ReadUInt(),
                PrevButtons = (InputButtons)reader.ReadUShort(),
            };
            if (s.Phase > WeaponPhase.Holstering) reader.Fail();
            return s;
        }
    }
}
