using Pulsebreak.Shared.Match;

namespace Pulsebreak.Server.Modes
{
    /// <summary>
    /// Duel and Elimination: rounds with no coming back. A round ends when one side has
    /// nobody standing, or when the clock runs out (then the side with more players left
    /// wins it, and failing that the side with more health). The first side to win enough
    /// rounds takes the match. Sides swap ends every round.
    /// </summary>
    public sealed class RoundMode : GameMode
    {
        public RoundMode(GameServer server, uint seed) : base(server, seed) { }

        protected override float LiveSeconds => Settings.roundSeconds;

        /// <summary>Arriving mid-round means watching until the next one.</summary>
        protected override void JoinedLive(ServerPlayer player) { }

        protected override void Scored(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot)
        {
            if (killer != null) killer.Score += 100;
        }

        protected override void UpdateLive()
        {
            int alive0 = AliveOn(0), alive1 = AliveOn(1);
            if (alive0 == 0 || alive1 == 0)
            {
                // Both down in the same tick is a draw.
                EndRound(alive0 > 0 ? 0 : alive1 > 0 ? 1 : MatchState.Nobody);
                return;
            }
            if (Tick < _state.PhaseEndTick) return;

            if (alive0 != alive1)
            {
                EndRound(alive0 > alive1 ? 0 : 1);
                return;
            }
            float difference = HealthOf(0) - HealthOf(1);
            EndRound(difference > 0.01f ? 0 : difference < -0.01f ? 1 : MatchState.Nobody);
        }

        float HealthOf(int team)
        {
            float total = 0f;
            foreach (ServerPlayer player in Members(team))
            {
                if (player.Alive) total += player.Vitals.Current;
            }
            return total;
        }

        void EndRound(int winner)
        {
            SetInvulnerable(true);
            _state.RoundWinner = winner;
            _state.AddScore(winner, 1);
            foreach (ServerPlayer player in Players)
            {
                if (player != null && player.Ready && player.Team == winner) player.RoundsWon++;
            }

            if (winner != MatchState.Nobody && _state.Score(winner) >= Settings.roundsToWin)
            {
                EnterMatchEnd(winner);
                return;
            }

            _state.Phase = MatchPhase.RoundEnd;
            _state.PhaseEndTick = Tick + Ticks(Settings.roundEndSeconds);
            Server.BroadcastMatchState();
        }
    }
}
