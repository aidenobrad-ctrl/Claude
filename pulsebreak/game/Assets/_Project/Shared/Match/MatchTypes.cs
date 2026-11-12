using System;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Shared.Match
{
    public enum MatchPhase : byte
    {
        /// <summary>Not enough players yet. Whoever is here can run around and shoot.</summary>
        Waiting,
        /// <summary>Everyone is placed and held still until the round starts.</summary>
        Countdown,
        Live,
        /// <summary>A round has just been decided. Nobody can be hurt.</summary>
        RoundEnd,
        MatchEnd,
    }

    public static class Teams
    {
        public const int Count = 2;
        /// <summary>The team of a player in a mode without teams.</summary>
        public const int None = 255;
    }

    /// <summary>
    /// The rules of a match: which mode, on which map, with how many players and to what
    /// limits. The server owns them and tells each client when it joins. Defaults for each
    /// mode come from <see cref="For"/>.
    /// </summary>
    [Serializable]
    public sealed class MatchSettings
    {
        public GameModeKind mode = GameModeKind.Duel;
        public string arenaId = ArenaLayout.DefaultId;
        /// <summary>Players on each team, 1 to 5, in team modes.</summary>
        [Range(1, 5)] public int teamSize = 1;
        /// <summary>Players in a mode without teams.</summary>
        [Range(2, 12)] public int maxPlayers = 8;

        /// <summary>Round modes: rounds a side needs. 3 makes a best of five.</summary>
        [Min(1)] public int roundsToWin = 3;
        [Min(5f)] public float roundSeconds = 90f;

        /// <summary>Respawning modes: the score that ends the match.</summary>
        [Min(1)] public int scoreLimit = 50;
        /// <summary>Respawning modes: how long a match lasts at most.</summary>
        [Min(30f)] public float timeLimitSeconds = 600f;
        [Min(0f)] public float respawnSeconds = 3f;

        [Min(0f)] public float countdownSeconds = 3f;
        [Min(0f)] public float roundEndSeconds = 4f;
        [Min(0f)] public float matchEndSeconds = 8f;
        [Min(1f)] public float maxHealth = 100f;

        /// <summary>Zone modes: seconds for one player to take a zone. More players take it faster.</summary>
        [Min(0.5f)] public float captureSeconds = 5f;
        /// <summary>Hardpoint: seconds before the zone moves.</summary>
        [Min(10f)] public float hardpointSeconds = 60f;

        /// <summary>Empty places are taken by bots, and given back when people join.</summary>
        public bool fillWithBots;
        /// <summary>0 easy, 1 normal, 2 hard.</summary>
        [Range(0, 2)] public int botSkill = 1;

        public bool TeamBased => mode != GameModeKind.FreeForAll && mode != GameModeKind.GunGame;
        public bool RoundBased => mode == GameModeKind.Duel || mode == GameModeKind.Elimination;
        public bool UsesZones => mode == GameModeKind.Domination || mode == GameModeKind.Hardpoint;
        /// <summary>How many players a full match has.</summary>
        public int Capacity => TeamBased ? Mathf.Clamp(teamSize, 1, 5) * Teams.Count : Mathf.Clamp(maxPlayers, 2, 12);

        public MatchSettings Clone() => (MatchSettings)MemberwiseClone();

        /// <summary>Sensible rules for a mode: the map it plays best on, team size and limits.</summary>
        public static MatchSettings For(GameModeKind mode)
        {
            var s = new MatchSettings { mode = mode };
            switch (mode)
            {
                case GameModeKind.Duel:
                    s.teamSize = 1; s.roundsToWin = 3; s.roundSeconds = 90f; s.arenaId = ArenaLayout.DefaultId;
                    break;
                case GameModeKind.Elimination:
                    s.teamSize = 3; s.roundsToWin = 4; s.roundSeconds = 120f; s.arenaId = ArenaLayout.CrossroadsId;
                    break;
                case GameModeKind.TeamDeathmatch:
                    s.teamSize = 5; s.scoreLimit = 50; s.timeLimitSeconds = 600f; s.respawnSeconds = 3f; s.arenaId = ArenaLayout.CrossroadsId;
                    break;
                case GameModeKind.FreeForAll:
                    s.maxPlayers = 8; s.scoreLimit = 25; s.timeLimitSeconds = 600f; s.respawnSeconds = 2f; s.arenaId = ArenaLayout.CrossroadsId;
                    break;
                case GameModeKind.Domination:
                    s.teamSize = 5; s.scoreLimit = 200; s.timeLimitSeconds = 600f; s.respawnSeconds = 4f; s.arenaId = ArenaLayout.CrossroadsId;
                    break;
                case GameModeKind.Hardpoint:
                    s.teamSize = 5; s.scoreLimit = 150; s.timeLimitSeconds = 600f; s.respawnSeconds = 4f; s.arenaId = ArenaLayout.CrossroadsId;
                    break;
                case GameModeKind.GunGame:
                    s.maxPlayers = 8; s.timeLimitSeconds = 600f; s.respawnSeconds = 2f; s.arenaId = ArenaLayout.CrossroadsId;
                    s.scoreLimit = GunGameLadder.Length;
                    break;
            }
            return s;
        }

        public static string Title(GameModeKind mode)
        {
            switch (mode)
            {
                case GameModeKind.TeamDeathmatch: return "Team Deathmatch";
                case GameModeKind.FreeForAll: return "Free-for-All";
                case GameModeKind.GunGame: return "Gun Game";
                default: return mode.ToString();
            }
        }
    }

    /// <summary>The weapons of Gun Game, in the order they are earned. A kill with the last one wins.</summary>
    public static class GunGameLadder
    {
        public static readonly string[] Weapons =
        {
            "ar_hadron", "smg_sprocket", "sg_breaker", "mr_verdict", "lmg_bulwark", "ar_triad", "sp_carom",
            "sp_volley", "pistol_magnate", "sg_sawed_off", "pistol_notch", "sp_flicker", "melee_field_knife",
        };

        /// <summary>Carried alongside every step; a kill with it sends the victim down a step.</summary>
        public const string Knife = "melee_field_knife";

        public static int Length => Weapons.Length;
    }

    /// <summary>
    /// What part of a loadout a player may use, for modes that limit it. Bits 0 to 2 are the
    /// weapon slots (primary, secondary, melee), bit 3 the utility item. The server sends the
    /// mask with every spawn, and client and server both apply it to the resolved loadout, so
    /// a prediction never draws a weapon the server will not let the player have.
    /// </summary>
    public static class MatchLoadouts
    {
        public const byte AllSlots = 0x0F;
        public const byte UtilityBit = 1 << Loadout.WeaponSlots;
        const byte WeaponBits = (1 << Loadout.WeaponSlots) - 1;

        /// <summary>Empties every slot the mask leaves out. A mask without a weapon leaves the weapons alone.</summary>
        public static Loadout Apply(Loadout loadout, int slotMask)
        {
            if ((slotMask & WeaponBits) != 0)
            {
                for (int i = 0; i < Loadout.WeaponSlots; i++)
                {
                    if ((slotMask & (1 << i)) == 0) loadout.Weapons[i] = null;
                }
            }
            if ((slotMask & UtilityBit) == 0)
            {
                for (int i = 0; i < loadout.Utilities.Length; i++) loadout.Utilities[i] = null;
            }
            return loadout;
        }

        /// <summary>True if the slot is usable under the mask.</summary>
        public static bool Allows(int slotMask, int slot) => (slotMask & WeaponBits) == 0 || (slotMask & (1 << slot)) != 0;

        /// <summary>
        /// Gun Game's loadout for a step of the ladder: that weapon, unmodified, and a knife.
        /// Nothing else is usable.
        /// </summary>
        public static LoadoutData GunGame(int level, LoadoutCatalog catalog, out byte slotMask, out int activeSlot)
        {
            level = Mathf.Clamp(level, 0, GunGameLadder.Length - 1);
            WeaponCatalog weapons = catalog.weapons;
            WeaponDefinition weapon = weapons.FindWeapon(GunGameLadder.Weapons[level]) ?? weapons.FirstForSlot(WeaponSlotType.Primary);
            int slot = weapon != null ? (int)weapon.slot : 0;

            var data = new LoadoutData { id = "gun_game", name = "Gun Game" };
            for (int i = 0; i < Loadout.WeaponSlots; i++)
            {
                WeaponDefinition filler = weapons.FirstForSlot((WeaponSlotType)i);
                data.GetWeapon(i).weaponId = filler != null ? filler.weaponId : "";
            }
            if (weapon != null) data.GetWeapon(slot).weaponId = weapon.weaponId;
            if (slot != (int)WeaponSlotType.Melee) data.melee.weaponId = GunGameLadder.Knife;

            slotMask = (byte)((1 << slot) | (1 << (int)WeaponSlotType.Melee));
            activeSlot = slot;
            return data;
        }
    }

    /// <summary>One capture zone, as everyone sees it.</summary>
    public struct ZoneState
    {
        /// <summary>The team that holds it, or <see cref="Teams.None"/>.</summary>
        public int Owner;
        /// <summary>The team taking it (or taking it back), or <see cref="Teams.None"/>.</summary>
        public int Capturer;
        /// <summary>0..1 towards the capturer owning it.</summary>
        public float Progress;
        /// <summary>Both teams are standing in it.</summary>
        public bool Contested;
    }

    /// <summary>Where the match stands. Small enough to send whole whenever it changes.</summary>
    public struct MatchState
    {
        public const int Nobody = -1;

        public MatchPhase Phase;
        /// <summary>Counts from 1. 0 before a match has started.</summary>
        public int Round;
        /// <summary>The server tick at which the current phase ends. 0 if it has no end.</summary>
        public int PhaseEndTick;
        /// <summary>Team scores: rounds won, kills or points, depending on the mode.</summary>
        public int Score0, Score1;
        /// <summary>The team (or in a mode without teams, the player) that won the round that just ended, or <see cref="Nobody"/>.</summary>
        public int RoundWinner;
        /// <summary>The team, or in a mode without teams the player, that won the match, or <see cref="Nobody"/>.</summary>
        public int MatchWinner;
        /// <summary>Hardpoint: the zone in play. -1 in other modes.</summary>
        public int ActiveZone;

        public int Score(int team) => team == 0 ? Score0 : team == 1 ? Score1 : 0;

        public void AddScore(int team, int amount)
        {
            if (team == 0) Score0 += amount;
            else if (team == 1) Score1 += amount;
        }

        public static MatchState Empty => new MatchState { RoundWinner = Nobody, MatchWinner = Nobody, ActiveZone = -1 };
    }

    public static class MatchRules
    {
        /// <summary>
        /// Removes everything from a command except where the player is looking and which
        /// weapon they want in hand. Applied by the server during the countdown, and by the
        /// client to its own prediction so the two agree.
        /// </summary>
        public static void Freeze(ref PlayerInputCmd cmd)
        {
            cmd.Move = Vector2.zero;
            cmd.Buttons = InputButtons.None;
        }

        /// <summary>
        /// Makes a command safe to simulate, whoever sent it: each movement axis within -1..1,
        /// sane view angles, only real buttons. Client and server both run a command through
        /// this, so they simulate exactly the same thing.
        ///
        /// It must give the same result when applied twice, because the client's cleaned
        /// command is what gets packed and sent. That is why it limits each axis on its own
        /// and leaves limiting the overall length to the motor: rescaling a diagonal here
        /// would round differently on the second pass.
        /// </summary>
        public static void Sanitize(ref PlayerInputCmd cmd)
        {
            cmd.Move = new Vector2(Mathf.Clamp(cmd.Move.x, -1f, 1f), Mathf.Clamp(cmd.Move.y, -1f, 1f));
            if (cmd.Yaw > 100000f || cmd.Yaw < -100000f) cmd.Yaw = 0f;
            cmd.Pitch = Mathf.Clamp(cmd.Pitch, -89.9f, 89.9f);
            cmd.Buttons &= AllButtons;
            if (cmd.SelectSlot > 8) cmd.SelectSlot = 0;
        }

        const InputButtons AllButtons =
            InputButtons.Jump | InputButtons.Sprint | InputButtons.Crouch | InputButtons.Fire | InputButtons.Ads |
            InputButtons.Reload | InputButtons.Utility1;
    }

    /// <summary>
    /// A player's health while a match is running. It lives in the match rather than on an
    /// object in the scene, so the server can run without any scene at all.
    /// </summary>
    public sealed class PlayerVitals : IDamageable
    {
        public readonly int PlayerId;
        public float Max = 100f;
        public float Current { get; private set; }
        /// <summary>Set between rounds and during the countdown.</summary>
        public bool Invulnerable;

        /// <summary>
        /// Decides whether a given attacker may hurt this player. The server sets it to keep
        /// teammates from hurting each other. Null lets everyone.
        /// </summary>
        public Func<IDamageable, bool> CanBeHurtBy;

        /// <summary>Raised when damage was actually taken.</summary>
        public event Action<PlayerVitals, DamageInfo, DamageResult> Damaged;

        public PlayerVitals(int playerId)
        {
            PlayerId = playerId;
        }

        public bool IsAlive => Current > 0f;

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || Invulnerable || info.Amount <= 0f) return default;
            if (CanBeHurtBy != null && info.Attacker != null && !CanBeHurtBy(info.Attacker)) return default;

            float applied = Mathf.Min(info.Amount, Current);
            Current -= applied;
            var result = new DamageResult { Applied = applied, Killed = Current <= 0f };
            Damaged?.Invoke(this, info, result);
            return result;
        }

        public void Revive() => Current = Max;

        /// <summary>Removes the player without anyone getting credit, for example when they leave.</summary>
        public void Clear() => Current = 0f;
    }

    /// <summary>Everything needed to stand a body up somewhere: enough to draw it and to shoot it.</summary>
    public struct BodyPose
    {
        /// <summary>Feet position.</summary>
        public Vector3 Position;
        public float Yaw;
        public float EyeHeight;

        public static BodyPose From(in MotorState state) => new BodyPose
        {
            Position = state.Position,
            Yaw = state.Yaw,
            EyeHeight = state.EyeHeight,
        };

        public static BodyPose Lerp(in BodyPose a, in BodyPose b, float t) => new BodyPose
        {
            Position = Vector3.Lerp(a.Position, b.Position, t),
            Yaw = LerpAngle(a.Yaw, b.Yaw, t),
            EyeHeight = Mathf.Lerp(a.EyeHeight, b.EyeHeight, t),
        };

        /// <summary>Blends two angles in degrees the short way round.</summary>
        public static float LerpAngle(float a, float b, float t)
        {
            float delta = (b - a) % 360f;
            if (delta > 180f) delta -= 360f;
            else if (delta < -180f) delta += 360f;
            return a + delta * Mathf.Clamp01(t);
        }
    }

    /// <summary>
    /// A character's physical presence: the capsule that moves it and the body parts that can
    /// be shot. The match only ever talks to this interface, so the same server code runs on
    /// Unity physics in the game and on a stand-in world in tests.
    /// </summary>
    public interface ICharacterBody
    {
        /// <summary>The movement capsule. Null for a body that is only ever posed.</summary>
        CharacterController Controller { get; }

        /// <summary>Who takes the damage when a body part is hit.</summary>
        void SetOwner(IDamageable owner);

        /// <summary>Moves the body parts. The capsule is moved by the simulation, not by this.</summary>
        void SetPose(in BodyPose pose);

        /// <summary>A body that cannot be hit is not found by shots at all.</summary>
        void SetHittable(bool hittable);

        void Destroy();
    }

    /// <summary>The physical world a match is played in.</summary>
    public interface IGameWorld
    {
        /// <summary>Builds the level's collision. Asking again for the same arena does nothing.</summary>
        void BuildArena(ArenaLayout layout);

        ICharacterBody CreateBody(string name, MovementConfig movement, bool withController);

        /// <summary>
        /// Makes moved bodies visible to shots. Unity only updates its physics scene between
        /// frames, so this must be called after posing bodies and before tracing against them.
        /// </summary>
        void SyncPhysics();
    }
}
