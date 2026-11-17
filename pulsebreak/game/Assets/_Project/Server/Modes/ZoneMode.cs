using UnityEngine;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server.Modes
{
    /// <summary>
    /// Domination and Hardpoint: respawning teams fighting over ground.
    ///
    /// Domination has three zones, all in play. Standing in one with no enemy in it takes it,
    /// faster with more of your team there; an enemy's zone has to be pushed back before it is
    /// yours. Every zone held earns its team a point a second.
    ///
    /// Hardpoint has one zone at a time, moving to the next every minute or so. A team earns a
    /// point for every second it has the zone to itself.
    ///
    /// Kills earn players points but not their team: the zones decide the match.
    /// </summary>
    public sealed class ZoneMode : RespawnMode
    {
        /// <summary>Points for the players who take a zone.</summary>
        const int CapturePoints = 200;
        /// <summary>Points each second for every player holding the hardpoint.</summary>
        const int HoldPoints = 10;

        readonly ArenaZone[] _areas;
        readonly bool _hardpoint;
        int _nextScoreTick;
        int _rotateTick;
        int _rotationStartTick;

        public ZoneMode(GameServer server, uint seed) : base(server, seed)
        {
            _hardpoint = Settings.mode == GameModeKind.Hardpoint;
            _areas = _hardpoint ? server.Arena.Hardpoints : server.Arena.Zones;
            _zoneCount = Mathf.Min(_areas.Length, NetMessages.MaxZones);
            ResetZones();
        }

        void ResetZones()
        {
            for (int i = 0; i < _zoneCount; i++) _zones[i] = new ZoneState { Owner = Teams.None, Capturer = Teams.None };
        }

        protected override void MatchStarting()
        {
            ResetZones();
            if (_hardpoint && _zoneCount > 0)
            {
                _state.ActiveZone = 0;
                _zones[0].Progress = 1f;
            }
        }

        protected override void WentLive()
        {
            _nextScoreTick = Tick + SimConstants.TickRate;
            _rotationStartTick = Tick;
            _rotateTick = Tick + Ticks(Settings.hardpointSeconds);
        }

        protected override void Award(ServerPlayer victim, ServerPlayer killer, string weaponId) => killer.Score += 100;

        protected override void UpdateLive()
        {
            if (_zoneCount > 0)
            {
                if (_hardpoint) UpdateHardpoint();
                else UpdateDomination();

                if (Tick >= _nextScoreTick)
                {
                    _nextScoreTick += SimConstants.TickRate;
                    ScoreSecond();
                    if (_state.Phase != MatchPhase.Live) return;
                }
            }
            base.UpdateLive();
        }

        // ------------------------------------------------------------------ Domination

        void UpdateDomination()
        {
            float dt = SimConstants.TickDelta;
            for (int z = 0; z < _zoneCount; z++)
            {
                Count(_areas[z], out int n0, out int n1);
                ref ZoneState zone = ref _zones[z];
                zone.Contested = n0 > 0 && n1 > 0;
                if (zone.Contested) continue;

                if (n0 == 0 && n1 == 0)
                {
                    // Left alone, a half-taken zone slowly goes back to how it was.
                    if (zone.Capturer != Teams.None) PushBack(ref zone, dt / (Settings.captureSeconds * 2f));
                    continue;
                }

                int team = n0 > 0 ? 0 : 1;
                int standing = Mathf.Min(Mathf.Max(n0, n1), 3);
                float rate = dt / Settings.captureSeconds * (1f + 0.5f * (standing - 1));

                if (team == zone.Owner)
                {
                    // Holders standing in their own zone undo an enemy's progress.
                    if (zone.Capturer != Teams.None) PushBack(ref zone, rate);
                    continue;
                }

                if (zone.Capturer != Teams.None && zone.Capturer != team)
                {
                    PushBack(ref zone, rate);
                    continue;
                }

                zone.Capturer = team;
                zone.Progress += rate;
                if (zone.Progress < 1f) continue;

                zone.Owner = team;
                zone.Capturer = Teams.None;
                zone.Progress = 0f;
                foreach (ServerPlayer player in Players)
                {
                    if (player != null && player.Alive && player.Team == team && _areas[z].Contains(player.State.Motor.Position))
                    {
                        player.Score += CapturePoints;
                        player.Objective += CapturePoints;
                    }
                }
                Server.Log("Team " + (team + 1) + " took " + _areas[z].Name + ".");
            }
        }

        static void PushBack(ref ZoneState zone, float amount)
        {
            zone.Progress -= amount;
            if (zone.Progress > 0f) return;
            zone.Progress = 0f;
            zone.Capturer = Teams.None;
        }

        // ------------------------------------------------------------------ Hardpoint

        void UpdateHardpoint()
        {
            if (Tick >= _rotateTick)
            {
                _state.ActiveZone = (_state.ActiveZone + 1) % _zoneCount;
                _rotationStartTick = Tick;
                _rotateTick = Tick + Ticks(Settings.hardpointSeconds);
                ResetZones();
                Server.Log("The hardpoint moved to " + _areas[_state.ActiveZone].Name + ".");
                Server.BroadcastMatchState();
            }

            int active = Mathf.Clamp(_state.ActiveZone, 0, _zoneCount - 1);
            Count(_areas[active], out int n0, out int n1);
            ref ZoneState zone = ref _zones[active];
            zone.Contested = n0 > 0 && n1 > 0;
            zone.Owner = zone.Contested ? Teams.None : n0 > 0 ? 0 : n1 > 0 ? 1 : Teams.None;
            zone.Capturer = Teams.None;
            // For a hardpoint, progress is the share of its time left.
            int length = Mathf.Max(1, _rotateTick - _rotationStartTick);
            zone.Progress = Mathf.Clamp01((_rotateTick - Tick) / (float)length);
        }

        // ------------------------------------------------------------------ scoring

        void ScoreSecond()
        {
            bool changed = false;
            if (_hardpoint)
            {
                int active = Mathf.Clamp(_state.ActiveZone, 0, _zoneCount - 1);
                int holder = _zones[active].Owner;
                if (holder != Teams.None)
                {
                    _state.AddScore(holder, 1);
                    changed = true;
                    foreach (ServerPlayer player in Players)
                    {
                        if (player != null && player.Alive && player.Team == holder && _areas[active].Contains(player.State.Motor.Position))
                        {
                            player.Score += HoldPoints;
                            player.Objective += HoldPoints;
                        }
                    }
                }
            }
            else
            {
                for (int z = 0; z < _zoneCount; z++)
                {
                    if (_zones[z].Owner == Teams.None) continue;
                    _state.AddScore(_zones[z].Owner, 1);
                    changed = true;
                }
            }
            if (!changed) return;

            Server.BroadcastMatchState();
            bool over0 = _state.Score0 >= Settings.scoreLimit, over1 = _state.Score1 >= Settings.scoreLimit;
            if (over0 || over1) EnterMatchEnd(over0 && over1 ? Ahead() : over0 ? 0 : 1);
        }

        /// <summary>Living players of each team standing in a zone.</summary>
        void Count(in ArenaZone area, out int team0, out int team1)
        {
            team0 = team1 = 0;
            foreach (ServerPlayer player in Players)
            {
                if (player == null || !player.Alive || !area.Contains(player.State.Motor.Position)) continue;
                if (player.Team == 0) team0++;
                else if (player.Team == 1) team1++;
            }
        }
    }
}
