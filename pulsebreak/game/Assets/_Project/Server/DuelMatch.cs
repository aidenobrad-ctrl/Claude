using UnityEngine;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server
{
    /// <summary>
    /// The rules of a 1v1 duel: first to win enough rounds takes the match.
    ///
    ///   Waiting    one player, free to run around and shoot
    ///   Countdown  both placed on opposite sides, held still
    ///   Live       ends when someone is eliminated or the clock runs out
    ///   RoundEnd   a pause; loadouts changed here are used next round
    ///   MatchEnd   the result, then a fresh match if both are still here
    /// </summary>
    public sealed class DuelMatch
    {
        readonly GameServer _server;
        DeterministicRandom _random;
        MatchState _state = MatchState.Empty;

        public MatchState State => _state;

        /// <summary>Players may look around and swap weapons, nothing else.</summary>
        public bool Frozen => _state.Phase == MatchPhase.Countdown;

        public DuelMatch(GameServer server, uint seed)
        {
            _server = server;
            _random = new DeterministicRandom(seed ^ 0xD0E1u);
        }

        DuelConfig Rules => _server.Rules;
        static int Ticks(float seconds) => Mathf.CeilToInt(seconds * SimConstants.TickRate);

        // ------------------------------------------------------------------ people coming and going

        public void PlayerJoined(ServerPlayer player)
        {
            // Mid-result, the newcomer waits for it to finish. Otherwise two players means a match.
            if (_state.Phase == MatchPhase.MatchEnd) return;
            if (ReadyCount() >= GameServer.MaxPlayers) StartMatch();
            else Warmup(player);
        }

        public void PlayerLeft(ServerPlayer player)
        {
            ServerPlayer remaining = Other(player.Id);
            bool underway = _state.Phase == MatchPhase.Countdown || _state.Phase == MatchPhase.Live || _state.Phase == MatchPhase.RoundEnd;
            if (underway && remaining != null)
            {
                // Leaving a match in progress forfeits it.
                _state.RoundWinner = MatchState.Nobody;
                _state.MatchWinner = remaining.Id;
                EnterMatchEnd();
            }
            else if (_state.Phase != MatchPhase.MatchEnd)
            {
                EnterWaiting();
            }
        }

        /// <summary>A player left the level through the floor.</summary>
        public void FellOut(ServerPlayer player)
        {
            if (_state.Phase == MatchPhase.Live) _server.AnnounceDeath(player, KillMessage.NoKiller, "", false);
            else _server.Spawn(player, PickSpawn(player), _state.Phase == MatchPhase.Waiting);
        }

        // ------------------------------------------------------------------ every tick

        /// <summary>Call once per server tick, after everyone has moved and every shot has landed.</summary>
        public void Update()
        {
            int tick = _server.Tick;
            switch (_state.Phase)
            {
                case MatchPhase.Waiting:
                    if (ReadyCount() >= GameServer.MaxPlayers) StartMatch();
                    break;

                case MatchPhase.Countdown:
                    if (tick < _state.PhaseEndTick) break;
                    _state.Phase = MatchPhase.Live;
                    _state.PhaseEndTick = tick + Ticks(Rules.roundSeconds);
                    SetInvulnerable(false);
                    _server.BroadcastMatchState();
                    break;

                case MatchPhase.Live:
                {
                    ServerPlayer a = _server.Players[0], b = _server.Players[1];
                    if (a == null || b == null) break;      // PlayerLeft deals with it

                    if (!a.Alive || !b.Alive)
                    {
                        // Both down in the same tick is a draw.
                        EndRound(a.Alive ? a.Id : b.Alive ? b.Id : MatchState.Nobody);
                    }
                    else if (tick >= _state.PhaseEndTick)
                    {
                        // Out of time: whoever has more health left takes it.
                        float difference = a.Vitals.Current - b.Vitals.Current;
                        EndRound(difference > 0.01f ? a.Id : difference < -0.01f ? b.Id : MatchState.Nobody);
                    }
                    break;
                }

                case MatchPhase.RoundEnd:
                    if (tick < _state.PhaseEndTick) break;
                    _state.Round++;
                    BeginRound();
                    break;

                case MatchPhase.MatchEnd:
                    if (tick < _state.PhaseEndTick) break;
                    if (ReadyCount() >= GameServer.MaxPlayers) StartMatch();
                    else EnterWaiting();
                    break;
            }
        }

        // ------------------------------------------------------------------ transitions

        void StartMatch()
        {
            _state = MatchState.Empty;
            _state.Round = 1;
            _server.Log("Match started.");
            BeginRound();
        }

        void BeginRound()
        {
            _server.ClearProjectiles();
            _state.Phase = MatchPhase.Countdown;
            _state.PhaseEndTick = _server.Tick + Ticks(Rules.countdownSeconds);
            _state.RoundWinner = MatchState.Nobody;

            foreach (ServerPlayer player in _server.Players)
            {
                if (player != null && player.Ready) _server.Spawn(player, PickSpawn(player), warmup: false);
            }
            SetInvulnerable(true);
            _server.BroadcastMatchState();
        }

        void EndRound(int winner)
        {
            SetInvulnerable(true);
            _state.RoundWinner = winner;
            if (winner == 0) _state.Score0++;
            else if (winner == 1) _state.Score1++;

            if (winner != MatchState.Nobody && _state.Score(winner) >= Rules.roundsToWin)
            {
                _state.MatchWinner = winner;
                EnterMatchEnd();
                return;
            }

            _state.Phase = MatchPhase.RoundEnd;
            _state.PhaseEndTick = _server.Tick + Ticks(Rules.roundEndSeconds);
            _server.BroadcastMatchState();
        }

        void EnterMatchEnd()
        {
            SetInvulnerable(true);
            _state.Phase = MatchPhase.MatchEnd;
            _state.PhaseEndTick = _server.Tick + Ticks(Rules.matchEndSeconds);
            _server.Log("Match over: " + _state.Score0 + " - " + _state.Score1 + ".");
            _server.BroadcastMatchState();
        }

        void EnterWaiting()
        {
            _server.ClearProjectiles();
            _state = MatchState.Empty;
            _server.BroadcastMatchState();

            foreach (ServerPlayer player in _server.Players)
            {
                if (player != null && player.Ready) Warmup(player);
            }
        }

        void Warmup(ServerPlayer player)
        {
            _server.Spawn(player, PickSpawn(player), warmup: true);
            player.Vitals.Invulnerable = false;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The two players swap ends every round, and start from a different spot on their side each time.</summary>
        ArenaSpawn PickSpawn(ServerPlayer player)
        {
            ArenaSpawn[][] sides = _server.Arena.Sides;
            ArenaSpawn[] side = sides[(player.Id + _state.Round) % sides.Length];
            return side[(int)(_random.NextUInt() % (uint)side.Length)];
        }

        void SetInvulnerable(bool invulnerable)
        {
            foreach (ServerPlayer player in _server.Players)
            {
                if (player != null) player.Vitals.Invulnerable = invulnerable;
            }
        }

        int ReadyCount()
        {
            int count = 0;
            foreach (ServerPlayer player in _server.Players)
            {
                if (player != null && player.Ready) count++;
            }
            return count;
        }

        ServerPlayer Other(int playerId)
        {
            foreach (ServerPlayer player in _server.Players)
            {
                if (player != null && player.Ready && player.Id != playerId) return player;
            }
            return null;
        }
    }
}
