using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Cosmetics;
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
        /// <summary>Reliable. A new look: suit, headgear, weapon skins. Everyone else is told.</summary>
        Look = 4,

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
        /// <summary>Reliable. A player's weapons were swapped where they stand (Gun Game).</summary>
        Rearm = 26,
        /// <summary>Reliable, to the victim only: you took damage, and from where.</summary>
        Hurt = 27,
    }

    public struct WelcomeMessage
    {
        public int PlayerId;
        public int ServerTick;
        /// <summary>The mode, the map and every limit of the match.</summary>
        public MatchSettings Settings;
    }

    public struct PlayerInfoMessage
    {
        public int PlayerId;
        public bool Present;
        public string Name;
        /// <summary>0 or 1, or <see cref="Teams.None"/> in a mode without teams.</summary>
        public int Team;
        public bool Bot;
        /// <summary>How they look. Never null when read.</summary>
        public CharacterLook Look;
    }

    public struct SpawnMessage
    {
        public int PlayerId;
        /// <summary>Goes up every time the player is placed. Snapshots carry it too, so old ones can be told apart.</summary>
        public byte SpawnCount;
        /// <summary>Reloads never run the reserve down (warm-up, Gun Game).</summary>
        public bool UnlimitedAmmo;
        /// <summary>Utility items come back over time (warm-up).</summary>
        public bool RechargeUtility;
        /// <summary>What of the loadout may be used: see <see cref="MatchLoadouts"/>. Everything, except in Gun Game.</summary>
        public byte SlotMask;
        /// <summary>The slot in hand when placed.</summary>
        public byte ActiveSlot;
        public LoadoutData Loadout;
        public Vector3 Position;
        public float Yaw;
    }

    public struct RearmMessage
    {
        public int PlayerId;
        /// <summary>Goes up like a spawn's, so snapshots of the old weapons are told apart from the new.</summary>
        public byte SpawnCount;
        public byte SlotMask;
        public byte ActiveSlot;
        public LoadoutData Loadout;
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

    /// <summary>Damage the receiver took: how much, and where it came from, for the hurt indicator.</summary>
    public struct HurtMessage
    {
        public const int NoAttacker = 255;

        /// <summary>Who dealt it, or <see cref="NoAttacker"/>.</summary>
        public int Attacker;
        public float Damage;
        public HitZone Zone;
        public bool Killed;
        /// <summary>Where it came from: the shooter's muzzle, or an explosion's centre.</summary>
        public Vector3 Source;
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
        public Stance Stance;
        public bool Grounded, Sprinting;
        public int ActiveSlot;
        public PlayerStats Stats;

        // Only in the snapshot sent to this player.
        public bool HasOwn;
        /// <summary>The last of this player's commands the server had run when it took the snapshot.</summary>
        public int LastCmdTick;
        public readonly PlayerSimState Own = new PlayerSimState();
    }

    /// <summary>A player's line on the scoreboard. Sent with every snapshot, so it is never stale.</summary>
    public struct PlayerStats
    {
        public int Team;
        public int Kills;
        public int Deaths;
        /// <summary>What ranks players: kills, objective points or a Gun Game level, depending on the mode.</summary>
        public int Score;
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
            writer.WriteString(data.utility);
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
            data.utility = reader.ReadString();
            data.perkSet = reader.ReadString();
            return data;
        }

        /// <param name="ticket">The join ticket from the backend, or empty when playing without an account.</param>
        public static void WriteHello(NetWriter writer, string name, LoadoutData loadout, string ticket = "", CharacterLook look = null)
        {
            writer.WriteByte((byte)NetMessage.Hello);
            writer.WriteString(name);
            writer.WriteLongString(ticket, GameTicket.MaxLength);
            WriteLoadout(writer, loadout);
            WriteLook(writer, look);
        }

        public static void WriteLookChange(NetWriter writer, CharacterLook look)
        {
            writer.WriteByte((byte)NetMessage.Look);
            WriteLook(writer, look);
        }

        // ------------------------------------------------------------------ looks

        /// <summary>
        /// A look, compactly: each slot's item by its position in the catalog, then the
        /// weapons with a skin of their own. At most about a kilobyte, with every weapon
        /// given its own skin.
        /// </summary>
        public static void WriteLook(NetWriter writer, CharacterLook look)
        {
            look = LookRules.Sanitize(look);
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots) writer.WriteByte((byte)look.Item(slot).Index);
            writer.WriteByte((byte)look.weaponSkins.Length);
            foreach (WeaponSkinChoice choice in look.weaponSkins)
            {
                writer.WriteString(choice.weaponId);
                writer.WriteByte((byte)CosmeticCatalog.Find(CosmeticSlot.WeaponSkin, choice.skinId).Index);
            }
        }

        /// <summary>Reads a look. Items this copy of the game does not have become defaults.</summary>
        public static CharacterLook ReadLook(NetReader reader)
        {
            var look = new CharacterLook();
            foreach (CosmeticSlot slot in CosmeticCatalog.Slots)
            {
                CosmeticItem item = CosmeticCatalog.At(slot, reader.ReadByte());
                look.Set(slot, (item ?? CosmeticCatalog.Default(slot)).Id);
            }
            int count = reader.ReadByte();
            if (count > LookRules.MaxWeaponSkins)
            {
                reader.Fail();
                return new CharacterLook();
            }
            var skins = new WeaponSkinChoice[count];
            for (int i = 0; i < count; i++)
            {
                string weaponId = reader.ReadString();
                CosmeticItem skin = CosmeticCatalog.At(CosmeticSlot.WeaponSkin, reader.ReadByte());
                skins[i] = new WeaponSkinChoice { weaponId = weaponId, skinId = skin != null ? skin.Id : "" };
            }
            look.weaponSkins = skins;
            return reader.Failed ? new CharacterLook() : LookRules.Sanitize(look);
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
            writer.WriteInt(message.ServerTick);
            WriteSettings(writer, message.Settings);
        }

        public static WelcomeMessage ReadWelcome(NetReader reader) => new WelcomeMessage
        {
            PlayerId = reader.ReadByte(),
            ServerTick = reader.ReadInt(),
            Settings = ReadSettings(reader),
        };

        public static void WriteSettings(NetWriter writer, MatchSettings s)
        {
            writer.WriteByte((byte)s.mode);
            writer.WriteString(s.arenaId);
            writer.WriteByte((byte)s.teamSize);
            writer.WriteByte((byte)s.maxPlayers);
            writer.WriteByte((byte)s.roundsToWin);
            writer.WriteFloat(s.roundSeconds);
            writer.WriteUShort((ushort)s.scoreLimit);
            writer.WriteFloat(s.timeLimitSeconds);
            writer.WriteFloat(s.respawnSeconds);
            writer.WriteFloat(s.countdownSeconds);
            writer.WriteFloat(s.roundEndSeconds);
            writer.WriteFloat(s.matchEndSeconds);
            writer.WriteFloat(s.maxHealth);
            writer.WriteFloat(s.captureSeconds);
            writer.WriteFloat(s.hardpointSeconds);
            writer.WriteBool(s.fillWithBots);
            writer.WriteByte((byte)s.botSkill);
        }

        public static MatchSettings ReadSettings(NetReader reader)
        {
            var s = new MatchSettings
            {
                mode = (GameModeKind)reader.ReadByte(),
                arenaId = reader.ReadString(),
                teamSize = reader.ReadByte(),
                maxPlayers = reader.ReadByte(),
                roundsToWin = reader.ReadByte(),
                roundSeconds = reader.ReadFloat(),
                scoreLimit = reader.ReadUShort(),
                timeLimitSeconds = reader.ReadFloat(),
                respawnSeconds = reader.ReadFloat(),
                countdownSeconds = reader.ReadFloat(),
                roundEndSeconds = reader.ReadFloat(),
                matchEndSeconds = reader.ReadFloat(),
                maxHealth = reader.ReadFloat(),
                captureSeconds = reader.ReadFloat(),
                hardpointSeconds = reader.ReadFloat(),
                fillWithBots = reader.ReadBool(),
                botSkill = reader.ReadByte(),
            };
            if (s.mode > GameModeKind.GunGame || s.teamSize < 1 || s.teamSize > 5 || s.maxPlayers < 2 || s.maxPlayers > NetProtocol.MaxPlayers) reader.Fail();
            return s;
        }

        public static void Write(NetWriter writer, in PlayerInfoMessage message)
        {
            writer.WriteByte((byte)NetMessage.PlayerInfo);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteBool(message.Present);
            writer.WriteString(message.Name);
            writer.WriteByte((byte)message.Team);
            writer.WriteBool(message.Bot);
            WriteLook(writer, message.Look);
        }

        public static PlayerInfoMessage ReadPlayerInfo(NetReader reader) => new PlayerInfoMessage
        {
            PlayerId = reader.ReadByte(),
            Present = reader.ReadBool(),
            Name = reader.ReadString(),
            Team = reader.ReadByte(),
            Bot = reader.ReadBool(),
            Look = ReadLook(reader),
        };

        public static void Write(NetWriter writer, in RearmMessage message)
        {
            writer.WriteByte((byte)NetMessage.Rearm);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteByte(message.SpawnCount);
            writer.WriteByte(message.SlotMask);
            writer.WriteByte(message.ActiveSlot);
            WriteLoadout(writer, message.Loadout);
        }

        public static RearmMessage ReadRearm(NetReader reader) => new RearmMessage
        {
            PlayerId = reader.ReadByte(),
            SpawnCount = reader.ReadByte(),
            SlotMask = reader.ReadByte(),
            ActiveSlot = reader.ReadByte(),
            Loadout = ReadLoadout(reader),
        };

        public static void Write(NetWriter writer, in SpawnMessage message)
        {
            writer.WriteByte((byte)NetMessage.Spawn);
            writer.WriteByte((byte)message.PlayerId);
            writer.WriteByte(message.SpawnCount);
            writer.WriteByte((byte)((message.UnlimitedAmmo ? 1 : 0) | (message.RechargeUtility ? 2 : 0)));
            writer.WriteByte(message.SlotMask);
            writer.WriteByte(message.ActiveSlot);
            WriteLoadout(writer, message.Loadout);
            writer.WriteVector3(message.Position);
            writer.WriteFloat(message.Yaw);
        }

        public static SpawnMessage ReadSpawn(NetReader reader)
        {
            var message = new SpawnMessage { PlayerId = reader.ReadByte(), SpawnCount = reader.ReadByte() };
            int flags = reader.ReadByte();
            message.UnlimitedAmmo = (flags & 1) != 0;
            message.RechargeUtility = (flags & 2) != 0;
            message.SlotMask = reader.ReadByte();
            message.ActiveSlot = reader.ReadByte();
            message.Loadout = ReadLoadout(reader);
            message.Position = reader.ReadVector3();
            message.Yaw = reader.ReadFloat();
            return message;
        }


        public static void Write(NetWriter writer, in MatchState state)
        {
            writer.WriteByte((byte)NetMessage.Match);
            writer.WriteByte((byte)state.Phase);
            writer.WriteByte((byte)state.Round);
            writer.WriteInt(state.PhaseEndTick);
            writer.WriteUShort((ushort)state.Score0);
            writer.WriteUShort((ushort)state.Score1);
            writer.WriteByte((byte)(sbyte)state.RoundWinner);
            writer.WriteByte((byte)(sbyte)state.MatchWinner);
            writer.WriteByte((byte)(sbyte)state.ActiveZone);
        }

        public static MatchState ReadMatch(NetReader reader)
        {
            var state = new MatchState
            {
                Phase = (MatchPhase)reader.ReadByte(),
                Round = reader.ReadByte(),
                PhaseEndTick = reader.ReadInt(),
                Score0 = reader.ReadUShort(),
                Score1 = reader.ReadUShort(),
                RoundWinner = (sbyte)reader.ReadByte(),
                MatchWinner = (sbyte)reader.ReadByte(),
                ActiveZone = (sbyte)reader.ReadByte(),
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

        public static void Write(NetWriter writer, in HurtMessage message)
        {
            writer.WriteByte((byte)NetMessage.Hurt);
            writer.WriteByte((byte)message.Attacker);
            writer.WriteFloat(message.Damage);
            writer.WriteByte((byte)message.Zone);
            writer.WriteBool(message.Killed);
            writer.WriteVector3(message.Source);
        }

        public static HurtMessage ReadHurt(NetReader reader) => new HurtMessage
        {
            Attacker = reader.ReadByte(),
            Damage = reader.ReadFloat(),
            Zone = (HitZone)reader.ReadByte(),
            Killed = reader.ReadBool(),
            Source = reader.ReadVector3(),
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

        public const int MaxZones = 8;

        /// <summary>The start of a snapshot: the tick, the zones (if the mode has any), then how many players follow.</summary>
        public static void WriteSnapshotHeader(NetWriter writer, int serverTick, int playerCount, ZoneState[] zones = null, int zoneCount = 0)
        {
            writer.WriteByte((byte)NetMessage.Snapshot);
            writer.WriteInt(serverTick);
            writer.WriteByte((byte)zoneCount);
            for (int i = 0; i < zoneCount; i++)
            {
                writer.WriteByte((byte)zones[i].Owner);
                writer.WriteByte((byte)zones[i].Capturer);
                writer.WriteByte((byte)Mathf.RoundToInt(Mathf.Clamp01(zones[i].Progress) * 255f));
                writer.WriteBool(zones[i].Contested);
            }
            writer.WriteByte((byte)playerCount);
        }

        /// <summary>Reads what <see cref="WriteSnapshotHeader"/> wrote after the message type: tick, zones, player count.</summary>
        public static void ReadSnapshotHeader(NetReader reader, out int serverTick, ZoneState[] zones, out int zoneCount, out int playerCount)
        {
            serverTick = reader.ReadInt();
            zoneCount = reader.ReadByte();
            if (zoneCount > MaxZones || zoneCount > zones.Length)
            {
                reader.Fail();
                playerCount = 0;
                return;
            }
            for (int i = 0; i < zoneCount; i++)
            {
                zones[i].Owner = reader.ReadByte();
                zones[i].Capturer = reader.ReadByte();
                zones[i].Progress = reader.ReadByte() / 255f;
                zones[i].Contested = reader.ReadBool();
            }
            playerCount = reader.ReadByte();
        }

        /// <param name="own">The player's full state, when this snapshot is going to that player. Otherwise null.</param>
        public static void WriteSnapshotPlayer(NetWriter writer, int id, bool spawned, bool alive, byte spawnCount, float health,
            in MotorState motor, int activeSlot, PlayerSimState own, int lastCmdTick, in PlayerStats stats = default)
        {
            writer.WriteByte((byte)id);
            writer.WriteByte((byte)((spawned ? 1 : 0) | (alive ? 2 : 0) | (motor.Grounded ? 4 : 0) |
                                    (motor.Sprinting ? 8 : 0) | (own != null ? 16 : 0)));
            writer.WriteByte(spawnCount);
            writer.WriteFloat(health);
            // Exact position; the rest only draws the player, so it is sent more coarsely.
            writer.WriteVector3(motor.Position);
            writer.WriteCentis(motor.Velocity.x);
            writer.WriteCentis(motor.Velocity.y);
            writer.WriteCentis(motor.Velocity.z);
            writer.WriteAngle(motor.Yaw);
            writer.WriteCentis(motor.Pitch);
            writer.WriteCentis(motor.EyeHeight);
            writer.WriteByte((byte)motor.Stance);
            writer.WriteByte((byte)activeSlot);
            writer.WriteByte((byte)stats.Team);
            writer.WriteByte((byte)Mathf.Clamp(stats.Kills, 0, 255));
            writer.WriteByte((byte)Mathf.Clamp(stats.Deaths, 0, 255));
            writer.WriteShort((short)Mathf.Clamp(stats.Score, short.MinValue, short.MaxValue));

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
            into.Velocity = new Vector3(reader.ReadCentis(), reader.ReadCentis(), reader.ReadCentis());
            into.Yaw = reader.ReadAngle();
            into.Pitch = reader.ReadCentis();
            into.EyeHeight = reader.ReadCentis();
            into.Stance = (Stance)reader.ReadByte();
            into.ActiveSlot = reader.ReadByte();
            into.Stats = new PlayerStats
            {
                Team = reader.ReadByte(),
                Kills = reader.ReadByte(),
                Deaths = reader.ReadByte(),
                Score = reader.ReadShort(),
            };

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
            float[] floats = Scratch;
            int count = Mathf.Min(state.Utilities.Length, 15);
            for (int i = 0; i < count; i++)
            {
                writer.WriteByte((byte)state.Utilities[i].Charges);
                floats[i * 2] = state.Utilities[i].Cooldown;
                floats[i * 2 + 1] = state.Utilities[i].RechargeTimer;
            }
            floats[count * 2] = state.ThrowTimer;
            writer.WriteSparseFloats(floats, count * 2 + 1);
            writer.WriteByte((byte)(sbyte)state.ThrowingSlot);
            writer.WriteUShort((ushort)state.BeltButtons);
        }

        public static void ReadSimState(NetReader reader, PlayerSimState state)
        {
            state.Motor = ReadMotor(reader);
            state.ActiveSlot = reader.ReadByte();
            state.PendingSlot = (sbyte)reader.ReadByte();
            for (int i = 0; i < state.Weapons.Length; i++) state.Weapons[i] = ReadWeapon(reader);
            float[] floats = Scratch;
            int count = Mathf.Min(state.Utilities.Length, 15);
            for (int i = 0; i < count; i++) state.Utilities[i].Charges = reader.ReadByte();
            reader.ReadSparseFloats(floats, count * 2 + 1);
            for (int i = 0; i < count; i++)
            {
                state.Utilities[i].Cooldown = floats[i * 2];
                state.Utilities[i].RechargeTimer = floats[i * 2 + 1];
            }
            state.ThrowTimer = floats[count * 2];
            state.ThrowingSlot = (sbyte)reader.ReadByte();
            state.BeltButtons = (InputButtons)reader.ReadUShort();
        }

        /// <summary>
        /// Somewhere to gather a group of floats before writing them in one go. One per thread:
        /// a host's server and client take turns on the same one, never at the same time.
        /// </summary>
        [System.ThreadStatic] static float[] _scratch;
        static float[] Scratch => _scratch ?? (_scratch = new float[32]);

        // The player's own state goes out exactly, because the client checks its prediction
        // against it, but packed: timers and offsets that are zero cost one bit each.

        static void WriteMotor(NetWriter writer, in MotorState s)
        {
            float[] f = Scratch;
            Put(f, 0, s.Position);
            Put(f, 3, s.Velocity);
            f[6] = s.Yaw;
            f[7] = s.Pitch;
            Put(f, 8, s.GroundNormal);
            f[11] = s.EyeHeight;
            f[12] = s.CoyoteTimer;
            f[13] = s.JumpBufferTimer;
            f[14] = s.SlideTimer;
            f[15] = s.SlideCooldown;
            f[16] = s.TimeSinceSprint;
            f[17] = s.MantleTimer;
            f[18] = s.MantleDuration;
            f[19] = s.MantleCooldown;
            Put(f, 20, s.MantleFrom);
            Put(f, 23, s.MantleTo);
            writer.WriteSparseFloats(f, MotorFloats);
            writer.WriteByte((byte)s.Stance);
            writer.WriteByte((byte)((s.Grounded ? 1 : 0) | (s.Sprinting ? 2 : 0) | (s.Mantling ? 4 : 0) | (s.JustLanded ? 8 : 0)));
            writer.WriteVarUInt((ushort)s.PrevButtons);
        }

        const int MotorFloats = 26, WeaponFloats = 15;

        static MotorState ReadMotor(NetReader reader)
        {
            float[] f = Scratch;
            reader.ReadSparseFloats(f, MotorFloats);
            var s = new MotorState
            {
                Position = Get(f, 0),
                Velocity = Get(f, 3),
                Yaw = f[6],
                Pitch = f[7],
                GroundNormal = Get(f, 8),
                EyeHeight = f[11],
                CoyoteTimer = f[12],
                JumpBufferTimer = f[13],
                SlideTimer = f[14],
                SlideCooldown = f[15],
                TimeSinceSprint = f[16],
                MantleTimer = f[17],
                MantleDuration = f[18],
                MantleCooldown = f[19],
                MantleFrom = Get(f, 20),
                MantleTo = Get(f, 23),
                Stance = (Stance)reader.ReadByte(),
            };
            int flags = reader.ReadByte();
            s.Grounded = (flags & 1) != 0;
            s.Sprinting = (flags & 2) != 0;
            s.Mantling = (flags & 4) != 0;
            s.JustLanded = (flags & 8) != 0;
            s.PrevButtons = (InputButtons)(ushort)reader.ReadVarUInt();
            if (s.Stance > Stance.Sliding) reader.Fail();
            return s;
        }

        static void WriteWeapon(NetWriter writer, in WeaponState s)
        {
            float[] f = Scratch;
            f[0] = s.PhaseTimer;
            f[1] = s.PhaseDuration;
            f[2] = s.ReloadElapsed;
            f[3] = s.ReloadTotal;
            f[4] = s.FireCooldown;
            f[5] = s.TriggerBuffer;
            f[6] = s.ChargeAmount;
            f[7] = s.MeleeTimer;
            f[8] = s.AdsAmount;
            f[9] = s.Bloom;
            f[10] = s.TimeSinceLastShot;
            f[11] = s.RecoilTarget.x;
            f[12] = s.RecoilTarget.y;
            f[13] = s.RecoilCurrent.x;
            f[14] = s.RecoilCurrent.y;
            writer.WriteSparseFloats(f, WeaponFloats);
            writer.WriteByte((byte)((byte)s.Phase | (s.MeleePending ? 0x80 : 0)));
            writer.WriteVarInt(s.AmmoInMagazine);
            writer.WriteVarInt(s.AmmoReserve);
            writer.WriteVarInt(s.BurstShotsRemaining);
            writer.WriteVarInt(s.SprayIndex);
            writer.WriteVarUInt(s.ShotCounter);
            writer.WriteVarUInt((ushort)s.PrevButtons);
        }

        static WeaponState ReadWeapon(NetReader reader)
        {
            float[] f = Scratch;
            reader.ReadSparseFloats(f, WeaponFloats);
            int phase = reader.ReadByte();
            var s = new WeaponState
            {
                PhaseTimer = f[0],
                PhaseDuration = f[1],
                ReloadElapsed = f[2],
                ReloadTotal = f[3],
                FireCooldown = f[4],
                TriggerBuffer = f[5],
                ChargeAmount = f[6],
                MeleeTimer = f[7],
                AdsAmount = f[8],
                Bloom = f[9],
                TimeSinceLastShot = f[10],
                RecoilTarget = new Vector2(f[11], f[12]),
                RecoilCurrent = new Vector2(f[13], f[14]),
                Phase = (WeaponPhase)(phase & 0x7F),
                MeleePending = (phase & 0x80) != 0,
                AmmoInMagazine = reader.ReadVarInt(),
                AmmoReserve = reader.ReadVarInt(),
                BurstShotsRemaining = reader.ReadVarInt(),
                SprayIndex = reader.ReadVarInt(),
                ShotCounter = reader.ReadVarUInt(),
                PrevButtons = (InputButtons)(ushort)reader.ReadVarUInt(),
            };
            if (s.Phase > WeaponPhase.Holstering) reader.Fail();
            return s;
        }

        static void Put(float[] into, int at, Vector3 v)
        {
            into[at] = v.x;
            into[at + 1] = v.y;
            into[at + 2] = v.z;
        }

        static Vector3 Get(float[] from, int at) => new Vector3(from[at], from[at + 1], from[at + 2]);
    }
}
