using Pulsebreak.Shared.Match;

namespace Pulsebreak.Server.Modes
{
    /// <summary>
    /// Team Deathmatch and Free-for-All: one long round, coming back a few seconds after
    /// going down. Team Deathmatch is won by the first team to the score limit in kills;
    /// Free-for-All by the first player to it. When time runs out, whoever is ahead wins.
    /// Zone modes and Gun Game build on this.
    /// </summary>
    public class RespawnMode : GameMode
    {
        public RespawnMode(GameServer server, uint seed) : base(server, seed) { }

        protected override float LiveSeconds => Settings.timeLimitSeconds;

        protected override void Scored(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot)
        {
            victim.RespawnTick = Tick + Ticks(Settings.respawnSeconds);
            if (killer == null) return;
            Award(victim, killer, weaponId);
        }

        /// <summary>Points for a kill. Only called when someone other than the victim gets the credit.</summary>
        protected virtual void Award(ServerPlayer victim, ServerPlayer killer, string weaponId)
        {
            if (Settings.TeamBased)
            {
                killer.Score += 100;
                _state.AddScore(killer.Team, 1);
                Server.BroadcastMatchState();
                if (_state.Score(killer.Team) >= Settings.scoreLimit) EnterMatchEnd(killer.Team);
            }
            else
            {
                killer.Score = killer.Kills;
                if (killer.Score >= Settings.scoreLimit) EnterMatchEnd(killer.Id);
            }
        }

        protected override void UpdateLive()
        {
            RespawnDue();
            if (_state.Phase == MatchPhase.Live && Tick >= _state.PhaseEndTick) EnterMatchEnd(Ahead());
        }

        /// <summary>Who wins if the match stopped now: the team or player in front, or nobody on a tie.</summary>
        protected int Ahead()
        {
            if (Settings.TeamBased)
                return _state.Score0 > _state.Score1 ? 0 : _state.Score1 > _state.Score0 ? 1 : MatchState.Nobody;
            ServerPlayer leader = Leader();
            return leader != null ? leader.Id : MatchState.Nobody;
        }
    }
}
