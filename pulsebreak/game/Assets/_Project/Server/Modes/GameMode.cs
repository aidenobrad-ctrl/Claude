using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server.Modes
{
    /// <summary>
    /// The rules of a match, on top of the server that runs it. Every mode follows the same
    /// outline and fills in what differs:
    ///
    ///   Waiting    not enough players yet; whoever is here can warm up. Once there are
    ///              enough, a short wait for more (none when the match is full)
    ///   Countdown  everyone placed and held still
    ///   Live       the mode's rules decide when this ends
    ///   RoundEnd   round modes only: a pause between rounds
    ///   MatchEnd   the result, then a fresh match if there are still enough players
    ///
    /// Teams are 0 and 1. In a mode without teams every player's team is
    /// <see cref="Teams.None"/>, and a winner is a player id rather than a team.
    /// </summary>
    public abstract class GameMode
    {
        /// <summary>How long a player waits to come back during warm-up.</summary>
        protected const float WarmupRespawnSeconds = 1.5f;

        protected readonly GameServer Server;
        protected DeterministicRandom Random;
        protected MatchState _state = MatchState.Empty;
        protected readonly ZoneState[] _zones = new ZoneState[NetMessages.MaxZones];
        protected int _zoneCount;

        readonly List<ServerPlayer> _scratch = new List<ServerPlayer>(GameServer.MaxPlayers);

        protected GameMode(GameServer server, uint seed)
        {
            Server = server;
            Random = new DeterministicRandom(seed ^ 0x6A3Eu);
        }

        public static GameMode Create(GameServer server, uint seed)
        {
            switch (server.Settings.mode)
            {
                case GameModeKind.Duel:
                case GameModeKind.Elimination:
                    return new RoundMode(server, seed);
                case GameModeKind.Domination:
                case GameModeKind.Hardpoint:
                    return new ZoneMode(server, seed);
                case GameModeKind.GunGame:
                    return new GunGameMode(server, seed);
                default:
                    return new RespawnMode(server, seed);
            }
        }

        public MatchState State => _state;
        public ZoneState[] Zones => _zones;
        public int ZoneCount => _zoneCount;

        /// <summary>Players may look around and swap weapons, nothing else.</summary>
        public bool Frozen => _state.Phase == MatchPhase.Countdown;

        /// <summary>Nobody can be hurt: between rounds, during the countdown and after the match.</summary>
        public bool Protected =>
            _state.Phase == MatchPhase.Countdown || _state.Phase == MatchPhase.RoundEnd || _state.Phase == MatchPhase.MatchEnd;

        /// <summary>A match is under way: leaving it now is leaving a game, not a lobby.</summary>
        public bool Underway =>
            _state.Phase == MatchPhase.Countdown || _state.Phase == MatchPhase.Live || _state.Phase == MatchPhase.RoundEnd;

        protected MatchSettings Settings => Server.Settings;
        protected int Tick => Server.Tick;
        protected IReadOnlyList<ServerPlayer> Players => Server.Players;
        protected static int Ticks(float seconds) => Mathf.CeilToInt(seconds * SimConstants.TickRate);

        // ------------------------------------------------------------------ people coming and going

        /// <summary>Puts a newcomer on the team with fewer players. Called before anyone is told they are here.</summary>
        public void AssignTeam(ServerPlayer player)
        {
            if (!Settings.TeamBased)
            {
                player.Team = Teams.None;
                return;
            }

            // Matchmaking may have picked the sides already (ranked balances them by rating).
            int preset = Server.PresetTeam(player);
            if (preset == 0 || preset == 1)
            {
                player.Team = preset;
                return;
            }

            int size0 = 0, size1 = 0, humans0 = 0, humans1 = 0;
            foreach (ServerPlayer other in Players)
            {
                if (other == null || other == player || !other.Ready) continue;
                if (other.Team == 0) { size0++; if (!other.IsBot) humans0++; }
                else if (other.Team == 1) { size1++; if (!other.IsBot) humans1++; }
            }

            if (size0 != size1) player.Team = size0 < size1 ? 0 : 1;
            else if (humans0 != humans1) player.Team = humans0 < humans1 ? 0 : 1;
            else player.Team = _state.Score0 <= _state.Score1 ? 0 : 1;
        }

        public virtual void PlayerJoined(ServerPlayer player)
        {
            switch (_state.Phase)
            {
                case MatchPhase.Waiting:
                    Place(player, PickSpawn(player));
                    break;
                case MatchPhase.Countdown:
                    // The round has not started: there is still time to take part.
                    Place(player, PickSpawn(player));
                    break;
                case MatchPhase.Live:
                    JoinedLive(player);
                    break;
                // Between rounds and after the match, newcomers wait to be placed with everyone else.
            }
        }

        /// <summary>Someone arrived while the match was being played.</summary>
        protected virtual void JoinedLive(ServerPlayer player) => Place(player, PickSpawn(player));

        public virtual void PlayerLeft(ServerPlayer player)
        {
            if (!Underway) return;

            if (Settings.TeamBased)
            {
                // A team with nobody left loses, if a person walked out. If a bot left, a person
                // is about to take its place, so the match simply carries on.
                for (int team = 0; team < Teams.Count; team++)
                {
                    if (Server.TeamSize(team) > 0) continue;
                    if (player.IsBot || Server.TeamSize(1 - team) == 0) EnterWaiting();
                    else EnterMatchEnd(1 - team);
                    return;
                }
            }
            else if (ReadyCount() < 2)
            {
                ServerPlayer remaining = Leader();
                if (remaining != null && !player.IsBot) EnterMatchEnd(remaining.Id);
                else EnterWaiting();
            }
        }

        /// <summary>A player left the level through the floor.</summary>
        public virtual void FellOut(ServerPlayer player)
        {
            if (_state.Phase == MatchPhase.Live || _state.Phase == MatchPhase.Waiting) Server.Kill(player, null, "", false);
            else Place(player, PickSpawn(player));
        }

        /// <summary>May <paramref name="attacker"/> hurt <paramref name="victim"/>? Teammates cannot hurt each other.</summary>
        public virtual bool CanHurt(IDamageable attacker, ServerPlayer victim)
        {
            if (!Settings.TeamBased || !(attacker is PlayerVitals vitals) || vitals.PlayerId == victim.Id) return true;
            ServerPlayer from = Players[vitals.PlayerId];
            return from == null || from.Team != victim.Team;
        }

        /// <summary>
        /// Someone went down. <paramref name="killer"/> is null when nobody gets the credit, and
        /// the victim themselves if they caught their own explosion.
        /// </summary>
        public void OnKill(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot)
        {
            if (_state.Phase == MatchPhase.Waiting)
            {
                victim.RespawnTick = Tick + Ticks(WarmupRespawnSeconds);
                return;
            }
            if (_state.Phase != MatchPhase.Live) return;

            victim.Deaths++;
            bool credited = killer != null && killer != victim;
            if (credited)
            {
                killer.Kills++;
                if (headshot) killer.Headshots++;
                if (!string.IsNullOrEmpty(weaponId))
                {
                    killer.WeaponKills.TryGetValue(weaponId, out (int Kills, int Headshots) tally);
                    killer.WeaponKills[weaponId] = (tally.Kills + 1, tally.Headshots + (headshot ? 1 : 0));
                }
            }
            Scored(victim, credited ? killer : null, weaponId, headshot);
        }

        /// <summary>A death during play, after kills and deaths have been counted. The mode scores it.</summary>
        protected abstract void Scored(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot);

        // ------------------------------------------------------------------ every tick

        /// <summary>Call once per server tick, after everyone has moved and every shot has landed.</summary>
        public void Update()
        {
            int tick = Tick;
            switch (_state.Phase)
            {
                case MatchPhase.Waiting:
                    RespawnDue();
                    UpdateLobby();
                    break;

                case MatchPhase.Countdown:
                    if (tick < _state.PhaseEndTick) break;
                    _state.Phase = MatchPhase.Live;
                    _state.PhaseEndTick = tick + Ticks(LiveSeconds);
                    SetInvulnerable(false);
                    WentLive();
                    Server.BroadcastMatchState();
                    break;

                case MatchPhase.Live:
                    UpdateLive();
                    break;

                case MatchPhase.RoundEnd:
                    if (tick < _state.PhaseEndTick) break;
                    _state.Round++;
                    BeginRound();
                    break;

                case MatchPhase.MatchEnd:
                    if (tick < _state.PhaseEndTick) break;
                    // A matchmade match is one match: then everyone goes back to their menus.
                    if (Server.MatchId.Length > 0) Server.EndSession();
                    else if (Enough()) StartMatch();
                    else EnterWaiting();
                    break;
            }
        }

        /// <summary>How long the live phase lasts: a round, or the whole match.</summary>
        protected abstract float LiveSeconds { get; }

        /// <summary>One tick of play. Decides when the round or match is over.</summary>
        protected abstract void UpdateLive();

        /// <summary>The countdown just finished.</summary>
        protected virtual void WentLive() { }

        void UpdateLobby()
        {
            if (!Enough())
            {
                if (_state.PhaseEndTick == 0) return;
                _state.PhaseEndTick = 0;
                Server.BroadcastMatchState();
                return;
            }

            if (ReadyCount() >= Settings.Capacity || Server.LobbySeconds <= 0f || Server.EveryoneAssignedIsHere)
            {
                StartMatch();
                return;
            }
            if (_state.PhaseEndTick == 0)
            {
                // Enough to play: give anyone still on their way a moment to arrive.
                _state.PhaseEndTick = Tick + Ticks(Server.LobbySeconds);
                Server.BroadcastMatchState();
            }
            else if (Tick >= _state.PhaseEndTick)
            {
                StartMatch();
            }
        }

        // ------------------------------------------------------------------ transitions

        /// <summary>The tick the current match started, for its length in the report.</summary>
        public int StartedTick { get; private set; }

        /// <summary>
        /// How a match ended for a player: won, drawn or lost. With teams it is their team's
        /// result; without, only the winner wins.
        /// </summary>
        public Pulsebreak.Shared.Progression.MatchOutcome OutcomeFor(int team, int playerId)
        {
            int winner = _state.MatchWinner;
            if (winner == MatchState.Nobody) return Pulsebreak.Shared.Progression.MatchOutcome.Draw;
            bool won = Settings.TeamBased ? winner == team : winner == playerId;
            return won ? Pulsebreak.Shared.Progression.MatchOutcome.Won : Pulsebreak.Shared.Progression.MatchOutcome.Lost;
        }

        protected void StartMatch()
        {
            StartedTick = Tick;
            _state = MatchState.Empty;
            _state.Round = 1;
            foreach (ServerPlayer player in Players)
            {
                if (player != null) player.ResetStats();
            }
            BalanceTeams();
            MatchStarting();
            Server.Log(MatchSettings.Title(Settings.mode) + " started.");
            BeginRound();
        }

        /// <summary>Scores are zero and teams even; nobody has been placed yet.</summary>
        protected virtual void MatchStarting() { }

        /// <summary>Everyone to their starting places, held still for the countdown.</summary>
        protected void BeginRound()
        {
            Server.ClearProjectiles();
            _state.Phase = MatchPhase.Countdown;
            _state.PhaseEndTick = Tick + Ticks(Settings.countdownSeconds);
            _state.RoundWinner = MatchState.Nobody;
            PlaceEveryone();
            SetInvulnerable(true);
            Server.BroadcastMatchState();
        }

        protected void EnterMatchEnd(int winner)
        {
            SetInvulnerable(true);
            _state.MatchWinner = winner;
            _state.Phase = MatchPhase.MatchEnd;
            _state.PhaseEndTick = Tick + Ticks(Settings.matchEndSeconds);
            Server.Log("Match over: " + Describe(winner) + ".");
            Server.BroadcastMatchState();
            Server.MatchFinished();
        }

        protected void EnterWaiting()
        {
            Server.ClearProjectiles();
            _state = MatchState.Empty;
            for (int i = 0; i < _zoneCount; i++) _zones[i] = new ZoneState { Owner = Teams.None, Capturer = Teams.None };
            Server.BroadcastMatchState();

            foreach (ServerPlayer player in Players)
            {
                if (player != null && player.Ready) Place(player, PickSpawn(player));
            }
        }

        string Describe(int winner)
        {
            if (winner == MatchState.Nobody) return "a draw";
            if (Settings.TeamBased) return "team " + (winner + 1) + " won " + _state.Score0 + " - " + _state.Score1;
            ServerPlayer player = winner >= 0 && winner < Players.Count ? Players[winner] : null;
            return (player != null ? player.Name : "player " + winner) + " won";
        }

        // ------------------------------------------------------------------ placing players

        /// <summary>Puts a player into the world now, with the protection the phase calls for.</summary>
        protected void Place(ServerPlayer player, in ArenaSpawn spawn)
        {
            Server.Spawn(player, spawn, OptionsFor(player));
            player.Vitals.Invulnerable = Protected;
        }

        /// <summary>What a player carries when placed. Warm-up never runs out of anything.</summary>
        protected virtual SpawnOptions OptionsFor(ServerPlayer player) =>
            _state.Phase == MatchPhase.Waiting ? SpawnOptions.Warmup : SpawnOptions.Normal;

        /// <summary>
        /// The start of a round or match. Teams start on their own side, each player on their
        /// own spot; in round modes the sides swap every round. Without teams, everyone is
        /// spread out as far from each other as the map allows.
        /// </summary>
        protected virtual void PlaceEveryone()
        {
            ArenaSpawn[][] sides = Server.Arena.Sides;
            int swap = Settings.RoundBased ? _state.Round + 1 : 0;
            if (Settings.TeamBased)
            {
                for (int team = 0; team < Teams.Count; team++)
                {
                    ArenaSpawn[] side = sides[(team + swap) % sides.Length];
                    int offset = (int)(Random.NextUInt() % (uint)side.Length);
                    int k = 0;
                    foreach (ServerPlayer player in Players)
                    {
                        if (player == null || !player.Ready || player.Team != team) continue;
                        Place(player, side[(offset + k++) % side.Length]);
                    }
                }
            }
            else
            {
                // Withdrawn first, so nobody's spot from last match counts against anyone.
                foreach (ServerPlayer player in Players)
                {
                    if (player != null && player.Ready) Server.Withdraw(player);
                }
                foreach (ServerPlayer player in Players)
                {
                    if (player != null && player.Ready) Place(player, PickSpawn(player));
                }
            }
        }

        /// <summary>
        /// Where to bring a player back: the spot furthest from every living enemy, avoiding
        /// any an enemy can see. With teams, a player's own side is slightly preferred.
        /// </summary>
        protected ArenaSpawn PickSpawn(ServerPlayer player)
        {
            ArenaLayout arena = Server.Arena;
            ArenaSpawn best = default;
            float bestScore = float.MinValue;

            if (Settings.TeamBased && player.Team != Teams.None)
            {
                int ownSide = player.Team % arena.Sides.Length;
                for (int s = 0; s < arena.Sides.Length; s++)
                {
                    foreach (ArenaSpawn spawn in arena.Sides[s]) Consider(spawn, s == ownSide ? 6f : 0f);
                }
            }
            foreach (ArenaSpawn spawn in arena.FreeSpawns) Consider(spawn, 0f);
            return best;

            void Consider(in ArenaSpawn spawn, float bonus)
            {
                float score = SpawnSafety(player, spawn.Position) + bonus + Random.NextFloat() * 2f;
                if (score <= bestScore) return;
                bestScore = score;
                best = spawn;
            }
        }

        /// <summary>Distance to the nearest living enemy (up to a point), much lower if one can see the spot.</summary>
        float SpawnSafety(ServerPlayer player, Vector3 position)
        {
            const float Enough = 30f;
            float nearest = Enough;
            bool seen = false;
            Vector3 eye = position + Vector3.up * 1.6f;
            foreach (ServerPlayer other in Players)
            {
                if (other == null || other == player || !other.Alive) continue;
                bool enemy = !Settings.TeamBased || other.Team != player.Team;
                Vector3 there = other.State.Motor.Position;
                float distance = Vector3.Distance(there, position);
                // Never on top of anyone, teammate or not.
                if (distance < 1.5f) return -100f;
                if (!enemy) continue;
                nearest = Mathf.Min(nearest, distance);
                if (!seen && distance < 45f)
                    seen = !Physics.Linecast(eye, there + Vector3.up * 1.6f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
            }
            return nearest - (seen ? 25f : 0f);
        }

        /// <summary>Brings back everyone whose wait is over.</summary>
        protected void RespawnDue()
        {
            foreach (ServerPlayer player in Players)
            {
                if (player == null || !player.Ready || player.RespawnTick < 0 || Tick < player.RespawnTick) continue;
                if (player.Alive) { player.RespawnTick = -1; continue; }
                Place(player, PickSpawn(player));
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Enough players for a match: someone on each team, or two people without teams.</summary>
        protected bool Enough()
        {
            if (!Settings.TeamBased) return ReadyCount() >= 2;
            return Server.TeamSize(0) > 0 && Server.TeamSize(1) > 0;
        }

        /// <summary>Evens the teams up at the start of a match, moving bots first.</summary>
        void BalanceTeams()
        {
            if (!Settings.TeamBased) return;
            while (true)
            {
                int size0 = Server.TeamSize(0), size1 = Server.TeamSize(1);
                if (Mathf.Abs(size0 - size1) < 2) return;
                int from = size0 > size1 ? 0 : 1;

                ServerPlayer moving = null;
                foreach (ServerPlayer player in Players)
                {
                    if (player == null || !player.Ready || player.Team != from) continue;
                    if (moving == null || (player.IsBot && !moving.IsBot)) moving = player;
                }
                if (moving == null) return;
                moving.Team = 1 - from;
                Server.BroadcastPlayerInfo(moving);
            }
        }

        protected void SetInvulnerable(bool invulnerable)
        {
            foreach (ServerPlayer player in Players)
            {
                if (player != null) player.Vitals.Invulnerable = invulnerable;
            }
        }

        protected int ReadyCount()
        {
            int count = 0;
            foreach (ServerPlayer player in Players)
            {
                if (player != null && player.Ready) count++;
            }
            return count;
        }

        /// <summary>Living players on a team.</summary>
        protected int AliveOn(int team)
        {
            int count = 0;
            foreach (ServerPlayer player in Players)
            {
                if (player != null && player.Ready && player.Team == team && player.Alive) count++;
            }
            return count;
        }

        /// <summary>The player with the best score, or null if nobody is here. Null too if two share it.</summary>
        protected ServerPlayer Leader()
        {
            ServerPlayer best = null;
            bool shared = false;
            foreach (ServerPlayer player in Players)
            {
                if (player == null || !player.Ready) continue;
                if (best == null || player.Score > best.Score)
                {
                    best = player;
                    shared = false;
                }
                else if (player.Score == best.Score)
                {
                    shared = true;
                }
            }
            return shared ? null : best;
        }

        /// <summary>The ready players of a team, in id order.</summary>
        protected List<ServerPlayer> Members(int team)
        {
            _scratch.Clear();
            foreach (ServerPlayer player in Players)
            {
                if (player != null && player.Ready && player.Team == team) _scratch.Add(player);
            }
            return _scratch;
        }
    }
}
