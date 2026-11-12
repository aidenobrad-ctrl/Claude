using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Server.Bots
{
    /// <summary>
    /// Decides what a bot presses each tick. A bot plays by the same rules as a person: it
    /// produces an input command, and the server simulates that exactly as it would one sent
    /// over the network. It cannot move faster, see through walls or aim perfectly; how well
    /// it plays comes from how quickly it reacts, how far off its aim is and how fast it
    /// turns.
    ///
    /// What it does, roughly:
    ///   sees an enemy       turns to them, waits out its reaction time, fires when its aim
    ///                       has settled; strafes to keep at its weapon's best range
    ///   gets shot           turns towards where it came from
    ///   sees nobody         heads for the objective (a zone that is not its team's, or the
    ///                       hardpoint), or else towards the nearest enemy
    ///   out of ammunition   reloads, or switches to something that still has some
    /// </summary>
    public sealed class BotBrain
    {
        // Per skill: easy, normal, hard.
        static readonly float[] ReactionSeconds = { 0.55f, 0.32f, 0.18f };
        static readonly float[] AimErrorDegrees = { 5f, 2.6f, 1.1f };
        static readonly float[] TurnDegreesPerTick = { 5f, 9f, 14f };
        static readonly float[] TurnSmoothing = { 0.25f, 0.4f, 0.6f };
        /// <summary>How far ahead of a moving target it aims, to make up for turning a little behind it.</summary>
        static readonly float[] LeadSeconds = { 0.03f, 0.07f, 0.1f };
        /// <summary>How far off the body, beyond its width, it is still willing to pull the trigger.</summary>
        static readonly float[] FireToleranceDegrees = { 2.5f, 1.2f, 0.5f };
        static readonly float[] BurstSeconds = { 0.35f, 0.55f, 0.85f };
        static readonly float[] ViewDegrees = { 60f, 70f, 80f };
        /// <summary>How much of the weapon's kick it pulls back against.</summary>
        static readonly float[] RecoilControl = { 0.35f, 0.7f, 0.95f };

        const float SightRange = 70f;
        const float MemorySeconds = 4f;

        readonly int _skill;
        DeterministicRandom _random;

        readonly List<Vector3> _path = new List<Vector3>(32);
        int _pathIndex;
        Vector3 _goal;
        bool _hasGoal;
        int _repathTick;

        int _targetId = -1;
        bool _targetVisible;
        int _targetSeenTick = int.MinValue;
        Vector3 _lastSeen;
        int _reactionTick;
        int _acquiredTick;

        float _yaw, _pitch;
        Vector2 _aimError;
        int _aimErrorTick;

        int _strafeSign = 1;
        int _strafeStart, _strafeUntil;
        int _burstUntil, _pauseUntil;
        bool _pulse;
        int _nextUtilityTick;

        int _hurtBy = -1;
        bool _hurtFresh;
        int _hurtTick = int.MinValue;

        // Cover: a spot the current threat cannot see, looked for when hurt or reloading.
        Vector3 _cover;
        bool _hasCover;
        int _coverCheckTick, _coverUntil;
        // Callouts: when this bot last told its team anything, and whom it last reported.
        int _lastCalloutTick = -1000000;
        int _calledTarget = -1;

        Vector3 _stuckFrom;
        int _stuckCheckTick;
        int _stuckCount;
        int _unstickUntil;

        public BotBrain(int skill, uint seed)
        {
            _skill = Mathf.Clamp(skill, 0, 2);
            _random = new DeterministicRandom(seed | 1u);
        }

        public int Skill => _skill;
        /// <summary>The enemy it is fighting, or -1.</summary>
        public int TargetId => _targetVisible ? _targetId : -1;
        /// <summary>The points it is walking through.</summary>
        public IReadOnlyList<Vector3> Path => _path;

        static int Ticks(float seconds) => Mathf.CeilToInt(seconds * SimConstants.TickRate);

        /// <summary>Placed in the world: forget the last life.</summary>
        public void Spawned(GameServer server, ServerPlayer self)
        {
            _yaw = self.State.Motor.Yaw;
            _pitch = 0f;
            _path.Clear();
            _hasGoal = false;
            _targetId = -1;
            _targetVisible = false;
            _hurtBy = -1;
            _hasCover = false;
            _coverUntil = 0;
            _stuckCount = 0;
            _stuckFrom = self.State.Motor.Position;
            _stuckCheckTick = server.Tick + 45;
            _repathTick = server.Tick;
        }

        /// <summary>Someone hurt it. It will look their way.</summary>
        public void HurtBy(int attackerId)
        {
            _hurtBy = attackerId;
            _hurtFresh = true;
        }

        // ------------------------------------------------------------------ each tick

        public PlayerInputCmd Think(GameServer server, ServerPlayer self)
        {
            int tick = server.Tick;
            if (_hurtFresh)
            {
                _hurtFresh = false;
                _hurtTick = tick;
            }

            MotorState motor = self.Sim.Motor.State;
            Vector3 feet = motor.Position;
            Vector3 eye = motor.EyePosition;
            var cmd = new PlayerInputCmd();

            ServerPlayer target = Perceive(server, self, eye, tick);
            float distance = target != null ? Vector3.Distance(feet, target.State.Motor.Position) : 0f;

            WeaponSimulator weapon = self.Sim.ActiveWeapon;
            WeaponClass weaponClass = self.Sim.ActiveBuild.Weapon.weaponClass;
            ManageWeapons(self, ref cmd, weapon, target != null && _targetVisible);

            // ---- where to look
            bool engaged = target != null && _targetVisible;
            bool climbing = false;
            Vector3 moveDirection = Vector3.zero;
            float moveSpeed = 1f;

            bool covering = WantsCover(self, weapon, engaged, tick);
            if (covering && target != null && tick >= _coverCheckTick)
            {
                _coverCheckTick = tick + Ticks(0.75f);
                _hasCover = FindCover(server, target.State.Motor.EyePosition, feet, out _cover);
                if (_hasCover) _coverUntil = Mathf.Max(_coverUntil, tick + Ticks(2.5f));
            }
            covering &= _hasCover;

            if (covering)
            {
                // Back off to cover, still shooting if there is a shot; once there, crouch and wait it out.
                moveDirection = FollowPath(server, feet, _cover, tick, 0.4f, out climbing);
                if (Flat(_cover - feet).magnitude < 0.7f)
                {
                    moveDirection = Vector3.zero;
                    if (!engaged) cmd.Buttons |= InputButtons.Crouch;
                }
            }
            else if (engaged)
            {
                float preferred = PreferredRange(weaponClass);
                if (distance > preferred + 4f) moveDirection = FollowPath(server, feet, target.State.Motor.Position, tick, 0.5f, out climbing);
                else moveDirection = Strafe(feet, target.State.Motor.Position, distance, preferred, tick, self);
            }
            else
            {
                Vector3 goal = PickGoal(server, self, feet, tick);
                moveDirection = FollowPath(server, feet, goal, tick, 1.5f, out climbing);
                if (moveDirection.sqrMagnitude < 0.01f && _hasGoal && Flat(goal - feet).magnitude < 1.2f) moveSpeed = 0f;
            }

            Unstick(self, feet, moveDirection, tick, ref cmd);

            // ---- aim
            if (engaged && !climbing)
            {
                Vector3 aim = AimPoint(target, weapon.Stats, eye, distance);
                if (tick >= _aimErrorTick)
                {
                    // Off at first, and steadier the longer it stays on the same target.
                    float tracked = (tick - _acquiredTick) * SimConstants.TickDelta;
                    float error = AimErrorDegrees[_skill] * Mathf.Max(0.3f, 1.5f - tracked);
                    _aimError = new Vector2(_random.NextSigned() * error, _random.NextSigned() * error * 0.6f);
                    _aimErrorTick = tick + Ticks(0.3f + _random.NextFloat() * 0.3f);
                }
                Vector3 drift = target.State.Motor.Velocity;
                drift.y = 0f;
                LookAt(eye, aim + drift * LeadSeconds[_skill], _aimError, out _);

                // Pull the trigger when the crosshair is on (or, for a weaker bot, near) the body.
                float offBody = Vector3.Angle(AimMath.Direction(_yaw, _pitch), aim - eye);
                float width = Mathf.Atan2(0.32f, Mathf.Max(distance, 0.5f)) * Mathf.Rad2Deg;
                bool onTarget = offBody <= width + FireToleranceDegrees[_skill];
                if (tick >= _reactionTick && InRange(weapon.Stats, weaponClass, distance) && onTarget) Fire(ref cmd, weapon, tick);

                if (distance > 8f && weaponClass != WeaponClass.Shotgun && weaponClass != WeaponClass.Melee &&
                    weaponClass != WeaponClass.Launcher)
                    cmd.Buttons |= InputButtons.Ads;

                MaybeThrow(self, ref cmd, distance, tick);
            }
            else if (!engaged && _hurtBy >= 0 && tick - _hurtTick < Ticks(1.2f) && Attacker(server) is ServerPlayer attacker && !climbing)
            {
                LookAt(eye, attacker.State.Motor.Position + Vector3.up * 1.4f, Vector2.zero, out _);
            }
            else if (moveDirection.sqrMagnitude > 0.01f)
            {
                Vector3 ahead = eye + moveDirection * 10f;
                LookAt(eye, new Vector3(ahead.x, eye.y, ahead.z), Vector2.zero, out _);
            }

            // ---- move, relative to where it is looking
            cmd.Yaw = _yaw;
            cmd.Pitch = _pitch;
            if (engaged)
            {
                // Pull against the kick, as a player does with the mouse.
                Vector2 recoil = weapon.State.RecoilCurrent * RecoilControl[_skill];
                cmd.Yaw -= recoil.x;
                cmd.Pitch = Mathf.Clamp(cmd.Pitch + recoil.y, -89f, 89f);
            }
            if (moveDirection.sqrMagnitude > 0.01f && moveSpeed > 0f)
            {
                float yaw = _yaw * Mathf.Deg2Rad;
                var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var right = new Vector3(forward.z, 0f, -forward.x);
                cmd.Move = new Vector2(Vector3.Dot(moveDirection, right), Vector3.Dot(moveDirection, forward)) * moveSpeed;

                if (climbing)
                {
                    cmd.Buttons |= InputButtons.Jump;
                    cmd.Move = new Vector2(0f, 1f);
                }
                else if (!engaged && cmd.Move.y > 0.8f && RemainingPath(feet) > 6f)
                {
                    cmd.Buttons |= InputButtons.Sprint;
                }
            }
            return cmd;
        }

        // ------------------------------------------------------------------ seeing

        ServerPlayer Perceive(GameServer server, ServerPlayer self, Vector3 eye, int tick)
        {
            ServerPlayer current = _targetId >= 0 ? server.Players[_targetId] : null;
            if (current != null && !IsEnemy(server, self, current)) current = null;

            // Looking properly takes a few line-of-sight checks, so it happens every few ticks.
            if ((tick + self.Id) % 3 != 0)
            {
                if (current == null) _targetVisible = false;
                return current;
            }

            ServerPlayer best = null;
            float bestScore = float.MaxValue;
            Vector3 forward = AimMath.Direction(_yaw, _pitch);
            foreach (ServerPlayer other in server.Players)
            {
                if (other == null || !IsEnemy(server, self, other)) continue;
                Vector3 chest = other.State.Motor.Position + Vector3.up * 1.3f;
                Vector3 toward = chest - eye;
                float distance = toward.magnitude;
                if (distance > SightRange) continue;

                bool noticed = distance < 4f || (other.Id == _hurtBy && tick - _hurtTick < Ticks(1.5f)) ||
                               (other.Id == _targetId && tick - _targetSeenTick < Ticks(1f)) ||
                               Vector3.Angle(forward, toward) < ViewDegrees[_skill];
                if (!noticed) continue;
                if (Physics.Linecast(eye, chest, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;

                float score = distance - (other.Id == _targetId ? 8f : 0f) - (other.Id == _hurtBy ? 5f : 0f);
                if (score >= bestScore) continue;
                bestScore = score;
                best = other;
            }

            if (best != null)
            {
                if (best.Id != _targetId || !_targetVisible || tick - _targetSeenTick > Ticks(1f))
                {
                    _reactionTick = tick + Ticks(ReactionSeconds[_skill] * (0.8f + _random.NextFloat() * 0.4f));
                    _acquiredTick = tick;
                    _aimErrorTick = tick;
                    CallOut(server, self, best, tick);
                }
                _targetId = best.Id;
                _targetVisible = true;
                _targetSeenTick = tick;
                _lastSeen = best.State.Motor.Position;
                return best;
            }

            _targetVisible = false;
            if (current != null && tick - _targetSeenTick < Ticks(MemorySeconds)) return current;
            _targetId = -1;
            return null;
        }

        static bool IsEnemy(GameServer server, ServerPlayer self, ServerPlayer other) =>
            other != self && other.Alive && (!server.Settings.TeamBased || other.Team != self.Team);

        ServerPlayer Attacker(GameServer server) => _hurtBy >= 0 && _hurtBy < server.Players.Count ? server.Players[_hurtBy] : null;

        // ------------------------------------------------------------------ aiming and shooting

        Vector3 AimPoint(ServerPlayer target, WeaponStats stats, Vector3 eye, float distance)
        {
            MotorState motor = target.State.Motor;
            // Hard bots go for the upper chest, the others centre mass.
            Vector3 point = motor.Position + Vector3.up * (_skill == 2 ? motor.EyeHeight * 0.85f : motor.EyeHeight * 0.75f);

            if (stats.ballistics != BallisticsType.Hitscan && stats.projectileSpeed > 0f)
            {
                float time = Mathf.Min(distance / stats.projectileSpeed, 1.5f);
                Vector3 velocity = motor.Velocity;
                velocity.y = 0f;
                point += velocity * time * (0.5f + 0.25f * _skill);
                point.y += 0.5f * stats.projectileGravity * time * time;
            }
            return point;
        }

        /// <summary>Turns the view part of the way towards a point, as fast as the bot's skill allows.</summary>
        void LookAt(Vector3 eye, Vector3 point, Vector2 error, out float offBy)
        {
            Vector3 toward = point - eye;
            float flat = new Vector2(toward.x, toward.z).magnitude;
            float wantYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg + error.x;
            float wantPitch = Mathf.Clamp(-Mathf.Atan2(toward.y, Mathf.Max(flat, 0.01f)) * Mathf.Rad2Deg + error.y, -85f, 85f);

            float limit = TurnDegreesPerTick[_skill], smoothing = TurnSmoothing[_skill];
            float yawDelta = Mathf.DeltaAngle(_yaw, wantYaw), pitchDelta = wantPitch - _pitch;
            _yaw += Mathf.Abs(yawDelta) < 0.6f ? yawDelta : Mathf.Clamp(yawDelta * smoothing, -limit, limit);
            _pitch += Mathf.Abs(pitchDelta) < 0.6f ? pitchDelta : Mathf.Clamp(pitchDelta * smoothing, -limit, limit);
            _yaw = Mathf.Repeat(_yaw, 360f);
            offBy = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(_yaw, wantYaw)), Mathf.Abs(wantPitch - _pitch));
        }

        void Fire(ref PlayerInputCmd cmd, WeaponSimulator weapon, int tick)
        {
            WeaponStats stats = weapon.Stats;
            switch (stats.fireMode)
            {
                case FireMode.Automatic:
                    // Short bursts, so the recoil settles between them.
                    if (tick >= _pauseUntil)
                    {
                        _burstUntil = tick + Ticks(BurstSeconds[_skill] * (0.7f + _random.NextFloat() * 0.6f));
                        _pauseUntil = _burstUntil + Ticks(0.12f + 0.12f * (2 - _skill));
                    }
                    if (tick < _burstUntil) cmd.Buttons |= InputButtons.Fire;
                    break;

                case FireMode.Charge:
                    // Hold to charge, let go to shoot.
                    if (weapon.State.ChargeAmount < 0.95f) cmd.Buttons |= InputButtons.Fire;
                    break;

                default:
                    // One shot per press.
                    _pulse = !_pulse;
                    if (_pulse) cmd.Buttons |= InputButtons.Fire;
                    break;
            }
        }

        static bool InRange(WeaponStats stats, WeaponClass weaponClass, float distance)
        {
            if (weaponClass == WeaponClass.Melee) return distance <= stats.meleeRange + 0.4f;
            if (weaponClass == WeaponClass.Shotgun) return distance <= Mathf.Max(stats.falloffEnd * 1.3f, 10f);
            return distance <= stats.maxRange;
        }

        static float PreferredRange(WeaponClass weaponClass)
        {
            switch (weaponClass)
            {
                case WeaponClass.Melee: return 1.4f;
                case WeaponClass.Shotgun: return 6f;
                case WeaponClass.Smg: return 11f;
                case WeaponClass.Pistol: return 13f;
                case WeaponClass.Special: return 13f;
                case WeaponClass.Launcher: return 16f;
                case WeaponClass.Marksman: return 24f;
                case WeaponClass.Sniper: return 30f;
                default: return 18f;
            }
        }

        void ManageWeapons(ServerPlayer self, ref PlayerInputCmd cmd, WeaponSimulator weapon, bool engaged)
        {
            PlayerSimulation sim = self.Sim;
            int active = sim.Inventory.ActiveIndex;
            bool melee = sim.ActiveBuild.Weapon.weaponClass == WeaponClass.Melee;
            WeaponState state = weapon.State;

            if (!melee && state.AmmoInMagazine == 0)
            {
                bool reserve = state.AmmoReserve > 0 || weapon.InfiniteReserve;
                if (reserve && !engaged)
                {
                    cmd.Buttons |= InputButtons.Reload;
                    return;
                }
                // Empty in a fight, or nothing left at all: draw something else.
                int other = BestOtherSlot(sim, active);
                if (other >= 0) cmd.SelectSlot = (byte)(other + 1);
                else if (reserve) cmd.Buttons |= InputButtons.Reload;
                return;
            }

            if (engaged) return;

            // Between fights: top up, and go back to the main weapon.
            if (!melee && state.AmmoInMagazine < weapon.Stats.magazineSize * 0.5f && (state.AmmoReserve > 0 || weapon.InfiniteReserve))
            {
                cmd.Buttons |= InputButtons.Reload;
                return;
            }
            if (active != 0 && sim.GetWeapon(0) != null && HasAmmo(sim.GetWeapon(0)) && state.Phase != WeaponPhase.Reloading)
                cmd.SelectSlot = 1;
        }

        static int BestOtherSlot(PlayerSimulation sim, int active)
        {
            for (int slot = 0; slot < sim.SlotCount; slot++)
            {
                if (slot == active) continue;
                WeaponSimulator candidate = sim.GetWeapon(slot);
                if (candidate != null && HasAmmo(candidate)) return slot;
            }
            return -1;
        }

        static bool HasAmmo(WeaponSimulator weapon) =>
            weapon.Stats.magazineSize == 0 || weapon.State.AmmoInMagazine > 0 || weapon.State.AmmoReserve > 0 || weapon.InfiniteReserve;

        void MaybeThrow(ServerPlayer self, ref PlayerInputCmd cmd, float distance, int tick)
        {
            if (_skill == 0 || tick < _nextUtilityTick || distance < 8f || distance > 24f) return;
            UtilityBelt belt = self.Sim.Belt;
            if (belt.SlotCount == 0 || belt.GetItem(0) == null || belt.GetState(0).Charges <= 0) return;
            if (_random.NextFloat() > 0.01f) return;
            cmd.Buttons |= InputButtons.Utility1;
            _nextUtilityTick = tick + Ticks(8f);
        }

        // ------------------------------------------------------------------ moving

        /// <summary>The way to go this tick to follow a path to <paramref name="goal"/>, finding a new one when it is due.</summary>
        Vector3 FollowPath(GameServer server, Vector3 feet, Vector3 goal, int tick, float repathSeconds, out bool climbing)
        {
            climbing = false;
            if (!_hasGoal || tick >= _repathTick || Flat(goal - _goal).sqrMagnitude > 9f)
            {
                _goal = goal;
                _hasGoal = true;
                _repathTick = tick + Ticks(repathSeconds * (0.8f + _random.NextFloat() * 0.4f));
                _pathIndex = 0;
                if (!server.Nav.FindPath(feet, goal, _path)) _path.Clear();
            }

            while (_pathIndex < _path.Count && Flat(_path[_pathIndex] - feet).magnitude < 0.45f) _pathIndex++;
            if (_pathIndex >= _path.Count) return Vector3.zero;

            Vector3 next = _path[_pathIndex];
            Vector3 toward = Flat(next - feet);
            float rise = next.y - feet.y;
            climbing = rise > NavGrid.StepHeight && toward.magnitude < 1.6f;
            return toward.normalized;
        }

        float RemainingPath(Vector3 feet)
        {
            float total = 0f;
            Vector3 at = feet;
            for (int i = _pathIndex; i < _path.Count; i++)
            {
                total += Flat(_path[i] - at).magnitude;
                at = _path[i];
            }
            return total;
        }

        /// <summary>Side to side while fighting, closing in or backing off to stay at a comfortable range.</summary>
        Vector3 Strafe(Vector3 feet, Vector3 enemy, float distance, float preferred, int tick, ServerPlayer self)
        {
            // A new direction now and then, and straight away on running into something.
            bool blocked = self.State.Motor.HorizontalSpeed < 0.3f && tick - _strafeStart > Ticks(0.3f);
            if (tick >= _strafeUntil || blocked)
            {
                _strafeSign = blocked ? -_strafeSign : _random.NextFloat() < 0.5f ? -1 : 1;
                _strafeStart = tick;
                _strafeUntil = tick + Ticks(0.5f + _random.NextFloat() * 0.9f);
            }
            Vector3 toward = Flat(enemy - feet).normalized;
            var side = new Vector3(toward.z, 0f, -toward.x) * _strafeSign;
            float approach = distance > preferred + 1f ? 0.5f : distance < preferred - 3f ? -0.6f : 0f;
            if (_skill == 0) side *= 0.5f;
            return (side + toward * approach).normalized;
        }

        /// <summary>What to head for when there is nobody to shoot.</summary>
        Vector3 PickGoal(GameServer server, ServerPlayer self, Vector3 feet, int tick)
        {
            // Somewhere an enemy was just seen.
            if (_targetId >= 0 && tick - _targetSeenTick < Ticks(MemorySeconds)) return _lastSeen;

            MatchSettings settings = server.Settings;
            ArenaLayout arena = server.Arena;
            if (settings.mode == GameModeKind.Hardpoint && server.Match.ActiveZone >= 0 && server.Match.ActiveZone < arena.Hardpoints.Length)
                return Spread(arena.Hardpoints[server.Match.ActiveZone], self);

            if (settings.mode == GameModeKind.Domination && arena.Zones.Length > 0)
            {
                // The nearest zone that is not ours yet, or that is being taken from us.
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int z = 0; z < arena.Zones.Length && z < server.Mode.ZoneCount; z++)
                {
                    ZoneState zone = server.Mode.Zones[z];
                    bool wanted = zone.Owner != self.Team || (zone.Capturer != Teams.None && zone.Capturer != self.Team) || zone.Contested;
                    if (!wanted) continue;
                    // Spread out: each bot leans towards a different zone.
                    float distance = Flat(arena.Zones[z].Center - feet).magnitude + ((z + self.Id) % 3) * 6f;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = z;
                }
                if (best >= 0) return Spread(arena.Zones[best], self);
            }

            // Where a teammate last saw someone.
            if (server.Sightings.Latest(self.Team, tick, Ticks(5f), feet, out Vector3 reported)) return reported;

            // Otherwise go looking for the nearest enemy.
            ServerPlayer nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (ServerPlayer other in server.Players)
            {
                if (other == null || !IsEnemy(server, self, other)) continue;
                float distance = Flat(other.State.Motor.Position - feet).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = other;
            }
            if (nearest != null) return nearest.State.Motor.Position;

            // Nobody about: wander.
            if (_hasGoal && Flat(_goal - feet).magnitude > 2f) return _goal;
            ArenaSpawn[] spots = arena.FreeSpawns;
            return spots.Length > 0 ? spots[(int)(_random.NextUInt() % (uint)spots.Length)].Position : feet;
        }

        // ------------------------------------------------------------------ cover and callouts

        /// <summary>Hurt badly, or caught reloading, with someone to hide from: time to find cover. It stays a while once there.</summary>
        bool WantsCover(ServerPlayer self, WeaponSimulator weapon, bool engaged, int tick)
        {
            if (_skill == 0) return false;      // easy bots stand and fight
            if (tick < _coverUntil) return true;
            bool recentlyHurt = _hurtTick != int.MinValue && tick - _hurtTick <= Ticks(1.5f);
            if (!engaged && !recentlyHurt) return false;
            bool hurt = self.Vitals.Max > 0f && self.Vitals.Current / self.Vitals.Max < 0.4f;
            bool reloading = weapon.State.Phase == WeaponPhase.Reloading || weapon.State.AmmoInMagazine == 0;
            return hurt || reloading;
        }

        /// <summary>
        /// The nearest walkable spot, a few metres away, that the threat cannot see a standing
        /// body at. Rings of candidates round the bot, nearest first; the first hidden one wins.
        /// </summary>
        bool FindCover(GameServer server, Vector3 threatEye, Vector3 feet, out Vector3 cover)
        {
            Vector3 away = Flat(feet - threatEye).normalized;
            foreach (float radius in CoverRadii)
            {
                for (int i = 0; i < 12; i++)
                {
                    // Start with the directions away from the threat.
                    float angle = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 30f;
                    Vector3 basis = away.sqrMagnitude > 0.01f ? away : Vector3.forward;
                    float radians = angle * Mathf.Deg2Rad, cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
                    var direction = new Vector3(basis.x * cos + basis.z * sin, 0f, basis.z * cos - basis.x * sin);
                    Vector3 spot = feet + direction * radius;
                    if (!server.Nav.TryGetHeight(spot, out float height) || Mathf.Abs(height - feet.y) > 1.5f) continue;
                    spot.y = height;
                    if (!Physics.Linecast(threatEye, spot + Vector3.up * 1.2f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                    cover = spot;
                    return true;
                }
            }
            cover = feet;
            return false;
        }

        static readonly float[] CoverRadii = { 2.5f, 4f, 6f, 8f };

        /// <summary>
        /// Tells the team about an enemy just spotted: teammate bots head that way, and people on
        /// the team get a short line in team chat, at most every so often.
        /// </summary>
        void CallOut(GameServer server, ServerPlayer self, ServerPlayer enemy, int tick)
        {
            if (!server.Settings.TeamBased) return;
            Vector3 where = enemy.State.Motor.Position;
            server.Sightings.Report(self.Team, where, tick);
            if (enemy.Id == _calledTarget && tick - _lastCalloutTick < Ticks(20f)) return;
            if (tick - _lastCalloutTick < Ticks(12f)) return;
            _lastCalloutTick = tick;
            _calledTarget = enemy.Id;
            server.SayToTeam(self, "Enemy " + server.Arena.PlaceName(where));
        }

        /// <summary>A point inside a zone, different for each bot so they do not all stand on the same spot.</summary>
        static Vector3 Spread(in ArenaZone zone, ServerPlayer self)
        {
            float angle = self.Id * 2.4f;
            return zone.Center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * zone.Radius * 0.5f;
        }

        /// <summary>Notices when it has been trying to move without getting anywhere, and tries something else.</summary>
        void Unstick(ServerPlayer self, Vector3 feet, Vector3 moveDirection, int tick, ref PlayerInputCmd cmd)
        {
            if (tick < _unstickUntil) cmd.Buttons |= InputButtons.Jump;
            if (tick < _stuckCheckTick) return;
            _stuckCheckTick = tick + 45;

            bool trying = moveDirection.sqrMagnitude > 0.01f;
            if (trying && Flat(feet - _stuckFrom).magnitude < 0.4f)
            {
                _stuckCount++;
                _unstickUntil = tick + 8;
                _repathTick = tick;
                _strafeSign = -_strafeSign;
                if (_stuckCount >= 3)
                {
                    _hasGoal = false;
                    _stuckCount = 0;
                }
            }
            else
            {
                _stuckCount = 0;
            }
            _stuckFrom = feet;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
