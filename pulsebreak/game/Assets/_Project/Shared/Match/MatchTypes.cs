using System;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Shared.Match
{
    public enum MatchPhase : byte
    {
        /// <summary>Fewer than two players. Whoever is here can run around and shoot.</summary>
        Waiting,
        /// <summary>Both players are placed and held still until the round starts.</summary>
        Countdown,
        Live,
        /// <summary>A round has just been decided. Nobody can be hurt.</summary>
        RoundEnd,
        MatchEnd,
    }

    /// <summary>The rules of a duel. The server owns them and tells each client when it joins.</summary>
    [Serializable]
    public sealed class DuelConfig
    {
        /// <summary>3 makes it a best of five.</summary>
        [Min(1)] public int roundsToWin = 3;
        [Min(0f)] public float countdownSeconds = 3f;
        [Min(5f)] public float roundSeconds = 90f;
        [Min(0f)] public float roundEndSeconds = 4f;
        [Min(0f)] public float matchEndSeconds = 8f;
        [Min(1f)] public float maxHealth = 100f;
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
        /// <summary>Rounds won, by player id.</summary>
        public int Score0, Score1;
        /// <summary>Who won the round that just ended, or <see cref="Nobody"/> for a draw.</summary>
        public int RoundWinner;
        public int MatchWinner;

        public int Score(int playerId) => playerId == 0 ? Score0 : Score1;

        public static MatchState Empty => new MatchState { RoundWinner = Nobody, MatchWinner = Nobody };
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
            InputButtons.Reload | InputButtons.LeanLeft | InputButtons.LeanRight | InputButtons.Utility1 | InputButtons.Utility2;
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
        /// <summary>Signed sideways shift of the head, in metres.</summary>
        public float LeanOffset;

        public static BodyPose From(in MotorState state) => new BodyPose
        {
            Position = state.Position,
            Yaw = state.Yaw,
            EyeHeight = state.EyeHeight,
            LeanOffset = state.LeanOffset,
        };

        public static BodyPose Lerp(in BodyPose a, in BodyPose b, float t) => new BodyPose
        {
            Position = Vector3.Lerp(a.Position, b.Position, t),
            Yaw = LerpAngle(a.Yaw, b.Yaw, t),
            EyeHeight = Mathf.Lerp(a.EyeHeight, b.EyeHeight, t),
            LeanOffset = Mathf.Lerp(a.LeanOffset, b.LeanOffset, t),
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
