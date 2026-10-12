// An AI racing driver. It drives through the same Vehicle and physics as the
// player, only its inputs come from here:
//
// - Steering: pure pursuit on the driver's own line with a speed-dependent
//   look-ahead, plus a yaw-rate damper and countersteer in slides.
// - Pedals: feedforward from the driver's speed profile plus feedback,
//   turned into throttle and brake through the car's measured curves, with
//   braking capped by what the rear tyres can take while cornering. The
//   stability control is off (ABS and traction control stay on).
// - Racecraft: follow, pass (a small state machine on the car being passed,
//   preferring the inside of the next corner), defend, and never steer into
//   a car alongside.
// - Recovery: one reverse attempt when stuck, then a reset to the route;
//   also when flipped, far off the road or facing the wrong way.
// - Personality: skill (line accuracy, grip, braking and throttle use,
//   reaction), aggression (passing, defending, following distance),
//   consistency (mistakes) and risk (a little closer to the grip limit).
using System;

namespace Halcyon.Core
{
    public sealed class Personality
    {
        public string Name;
        /// <summary>0..1: line accuracy, braking points, reaction time.</summary>
        public double Skill = 0.7;
        /// <summary>0..1: willingness to pass and defend, following distance.</summary>
        public double Aggression = 0.5;
        /// <summary>0..1: 1 never makes a mistake.</summary>
        public double Consistency = 0.8;
        /// <summary>0..1: how close to the grip limit the driver plans.</summary>
        public double Risk = 0.5;

        /// <summary>Share of the car's measured cornering grip the driver plans with.</summary>
        public double GripScale => 0.72 + 0.20 * Skill + 0.03 * Risk;
        /// <summary>Share of the car's measured braking the driver plans with.</summary>
        public double BrakeScale => 0.64 + 0.28 * Skill + 0.03 * Risk;
        /// <summary>Share of the car's acceleration the driver commits to out of corners.</summary>
        public double AccelScale => 0.80 + 0.20 * Skill;
        /// <summary>How much of the road the driver's line uses (1 = the full racing line, 0 = the centreline).</summary>
        public double LineAccuracy => 0.30 + 0.70 * Skill;
        /// <summary>Extra reaction time, s: braking starts this much early and the throttle comes this much late.</summary>
        public double Reaction => 0.35 * (1 - Skill);
        /// <summary>Wander around the line, m.</summary>
        public double LineError => 0.9 * (1 - Skill);

        public static readonly string[] Names =
        {
            "M. Okafor", "L. Varga", "S. Lindqvist", "K. Tanaka", "R. Duarte", "A. Moreau",
            "J. Whitfield", "E. Novak", "D. Haldane", "P. Ferreira", "Y. Seo", "T. Brennan",
            "I. Castell", "N. Oyelaran", "F. Brandt", "G. Ricci",
        };
    }

    public enum Difficulty { Novice, Average, Pro, Legend }

    public static class Personalities
    {
        /// <summary>A field of drivers spread around a difficulty: deterministic for a seed.</summary>
        public static Personality[] Field(Difficulty d, int count, Rng rng)
        {
            double centre = d == Difficulty.Novice ? 0.35 : d == Difficulty.Average ? 0.58 : d == Difficulty.Pro ? 0.78 : 0.93;
            var names = (string[])Personality.Names.Clone();
            for (int i = names.Length - 1; i > 0; i--)
            {
                int j = rng.Int(0, i + 1);
                (names[i], names[j]) = (names[j], names[i]);
            }
            var o = new Personality[count];
            for (int i = 0; i < count; i++)
            {
                double f = count > 1 ? (double)i / (count - 1) : 0.5;
                o[i] = new Personality
                {
                    Name = names[i % names.Length],
                    Skill = Clamp01(centre - 0.14 + 0.22 * f + rng.Range(-0.03, 0.03)),
                    Aggression = Clamp01(rng.Range(0.25, 0.85)),
                    Consistency = Clamp01(0.6 + 0.35 * centre + rng.Range(-0.1, 0.1)),
                    Risk = Clamp01(rng.Range(0.3, 0.8)),
                };
            }
            return o;
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    }

    public sealed class AiDriver
    {
        public readonly SimCar Car;
        public readonly Personality P;
        public readonly CarAbility Ability;
        public RaceRoute Route { get; private set; }
        public RacingLine Line { get; private set; }
        public SpeedProfile Profile { get; private set; }

        /// <summary>Arc length on the route (wrapped) and lateral offset (+ right).</summary>
        public double S, Lateral, Dist;
        /// <summary>Lateral target relative to the racing line (racecraft), m.</summary>
        public double Lane;
        public double TargetSpeed;
        /// <summary>Slow down and keep out of the way (after the finish).</summary>
        public bool Cruise;
        /// <summary>True while held on the grid.</summary>
        public bool Hold;
        /// <summary>Arc length to stop at (the end of a sprint), or NaN.</summary>
        public double StopAt = double.NaN;

        // Statistics for the tournament tests.
        public double StuckTime, MaxStuck;
        public int Resets, Reversals;

        readonly Rng rng;
        double laneTarget, reverseT, stuckT, offT, flipT, wrongT, lastResetS = double.NaN;
        // Headway: the furthest point along the route lately, and how long since the car got 4 m past it.
        double headS = double.NaN, headT;
        /// <summary>Held up by a car close ahead that is barely moving (last step): waiting, not stuck.</summary>
        bool queued;
        double mistakeT, mistakeSpeed = 1, mistakeLane;
        double wobble, wobbleRate;
        int passSide, passId = -1;
        double passT, passCooldown;
        bool started;

        public AiDriver(SimCar car, Personality p, CarAbility ability, uint seed)
        {
            Car = car;
            P = p;
            Ability = ability;
            rng = new Rng(seed);
            wobble = rng.Range(0, Math.PI * 2);
            wobbleRate = rng.Range(0.6, 1.2);
            // The AI does its own stability work (yaw damping, countersteer), like a
            // skilled driver with the stability control off; ABS and traction control stay on.
            car.vehicle.aids.steering = SteeringAssist.Off;
            car.vehicle.aids.gearbox = GearboxMode.Auto;
            car.vehicle.aids.esc = false;
        }

        public void SetRoute(RaceRoute route, RacingLine line)
        {
            Route = route;
            Line = line.Blend(P.LineAccuracy);
            Profile = SpeedProfile.Build(Line, Ability, P.GripScale, P.BrakeScale, P.AccelScale);
            var v = Car.vehicle;
            route.ProjectAny(v.pos.x, v.pos.z, out S, out Lateral, out Dist);
            started = true;
        }

        /// <summary>The other cars, for racecraft.</summary>
        public interface IField
        {
            int Count { get; }
            AiDriverView Get(int i);
        }

        public struct AiDriverView
        {
            public int id;
            public double s, lateral, speed, halfWidth, length;
        }

        /// <summary>Plan this step's inputs.</summary>
        public void Update(double dt, IField field)
        {
            var v = Car.vehicle;
            var inp = v.input;
            if (!started) return;
            Route.Project(v.pos.x, v.pos.z, S, 30, 70, out S, out Lateral, out Dist);
            if (Hold)
            {
                inp.steer = 0;
                inp.throttle = 0;
                inp.brake = 0;
                inp.handbrake = 1;
                return;
            }
            inp.handbrake = 0;
            inp.clutch = 0;
            double speed = v.vLong;
            double wl = Route.Sample(Route.WLeft, S), wr = Route.Sample(Route.WRight, S);
            double edge = Lateral > 0 ? wr : wl;
            if (Math.Abs(Lateral) - edge > MaxOff) MaxOff = Math.Abs(Lateral) - edge;

            // --- Parked at the end of a sprint after finishing ------------------------------------
            if (Cruise && !double.IsNaN(StopAt) && S > StopAt - 30 && Math.Abs(speed) < 3)
            {
                inp.steer = 0;
                inp.throttle = 0;
                inp.brake = 1;
                inp.handbrake = 1;
                StuckTime = 0;
                return;
            }

            // --- Recovery -------------------------------------------------------------------
            if (Recover(dt, speed, edge)) return;

            // --- Mistakes ---------------------------------------------------------------------
            mistakeT -= dt;
            if (mistakeT <= 0)
            {
                mistakeSpeed = 1;
                mistakeLane = 0;
                if (rng.Chance((1 - P.Consistency) * 0.08 * dt))
                {
                    mistakeT = rng.Range(1.0, 2.2);
                    double kind = rng.Next();
                    if (kind < 0.4) mistakeSpeed = Math.Min(1.05 + 0.04 * rng.Next(), Math.Sqrt(0.98 / P.GripScale)); // brakes late, within the car's grip
                    else if (kind < 0.75) mistakeSpeed = 0.88;                    // lifts
                    else mistakeLane = rng.Range(-1.6, 1.6);                      // runs wide
                    OnEvent?.Invoke($"mistake at s {S:0}: speed x{mistakeSpeed:0.00}, lane {mistakeLane:+0.0;-0.0} for {mistakeT:0.0} s");
                }
            }

            // --- Speed target -------------------------------------------------------------------
            // Braking reads the profile a reaction time ahead; a slower driver also
            // gets back on the throttle later (reads the rising profile further back).
            double vv0 = Math.Max(0, speed);
            lead = 1.5 + vv0 * (0.12 + P.Reaction);
            lag = 2 * vv0 * P.Reaction;
            double vT = Want(S);

            // --- Racecraft ----------------------------------------------------------------------
            double half = Ability.HalfWidth;
            laneTarget = mistakeLane;
            sideMin = double.NegativeInfinity;
            sideMax = double.PositiveInfinity;
            if (field != null)
            {
                Racecraft(dt, field, speed, half, wl, wr, ref vT);
            }
            // Lane changes ease in, and never faster than 2.5 m/s sideways, so a
            // pass at 250 km/h is as gentle as one at 100.
            Lane += Clamp((laneTarget - Lane) * Math.Min(1, dt * 1.6), -2.5 * dt, 2.5 * dt);
            // Keep the lane inside the room the cars alongside leave.
            double lineHere = Line.OffsetAt(S);
            if (sideMin <= sideMax) Lane = Clamp(Lane, sideMin - lineHere, sideMax - lineHere);
            // Off the line towards the inside of a corner the radius is tighter than the profile planned for.
            double kAhead = Line.CurvAt(S + lead);
            double inward = Lane * Math.Sign(kAhead);
            if (Math.Abs(kAhead) > 1e-3 && inward > 0.2)
            {
                double radius = 1 / Math.Abs(kAhead);
                vT *= Math.Sqrt(Math.Max(0.55, (radius - inward) / radius));
            }

            // --- Steering: pure pursuit on the line plus the lane offset ------------------------
            double ld = Clamp(5.5 + 0.38 * Math.Max(0, speed), 7.5, 42);
            // Far from where it should be (off the road, after a spin), aim further
            // ahead: rejoin at a shallow angle instead of turning back at full lock.
            double latErr = Math.Abs(Lateral - (Line.OffsetAt(S) + Lane));
            if (latErr > 3) ld = Math.Max(ld, 2.5 * latErr);
            double sT = S + ld;
            Route.At(sT, out double cx, out _, out double cz, out double tx, out double tz);
            wobble += dt * wobbleRate;
            double err = P.LineError * Math.Sin(wobble * 0.9) * 0.6;
            double off = Line.OffsetAt(sT) + Lane + err;
            double wlT = Route.Sample(Route.WLeft, sT), wrT = Route.Sample(Route.WRight, sT);
            if (sideMin <= sideMax) off = Clamp(off, sideMin, sideMax);
            else off = (sideMin + sideMax) / 2;
            // Keep the target off the edges, further at speed: a car tracking a
            // target that hugs the edge runs past it.
            double margin = 0.25 + 0.025 * Math.Max(0, speed - 20);
            double offLo = -wlT + half + margin, offHi = wrT - half - margin;
            off = offLo <= offHi ? Clamp(off, offLo, offHi) : (offLo + offHi) / 2;
            double px = cx - tz * off, pz = cz + tx * off;
            var p = v.Params;
            double rx = v.pos.x - v.fwd.x * p.b, rz = v.pos.z - v.fwd.z * p.b;
            double dx = px - rx, dz = pz - rz;
            double right = dx * v.right.x + dz * v.right.z;
            double d2 = Math.Max(1, dx * dx + dz * dz);
            double kappa = -2 * right / d2;                    // + = left
            double delta = DMath.Atan(kappa * p.wheelbase);    // road-wheel angle, + = left
            // Yaw damping: steer against yaw beyond what the arc to the target asks for (+ yaw = left).
            double rDes = kappa * Math.Max(0, speed);
            delta += 0.2 * (rDes - v.yawRate);
            double steer = -delta / p.maxSteer;
            // Countersteer in a slide (sideslip beta > 0: steer right).
            double beta = DMath.Atan2(v.vLat, Math.Max(2, Math.Abs(v.vLong)));
            if (Math.Abs(beta) > 0.035) steer += (beta - 0.035 * Math.Sign(beta)) * 2.6;
            inp.steer = Clamp(steer, -1, 1);
            DebugRight = right;
            DebugBeta = beta;
            // The rear stepping out shows as yaw running ahead of the arc's: a
            // driver feels it at once and eases the pedals before it is a slide.
            double yawOver = Math.Abs(v.yawRate - rDes) - 0.035;

            // --- Pedals: the acceleration wanted, from the car's own measured curves -----------
            TargetSpeed = vT;
            double vv = Math.Max(0, speed);
            // Feedforward from the profile's own slope keeps the car on its braking curve.
            double vA = Want(S), vB = Want(S + 6);
            double aFF = Clamp(vA * (vB - vA) / 6, -16, 6);
            double aReq = Clamp(aFF + 2.5 * (vT - vv), -40, 40);
            DebugA = aReq;
            DebugFF = aFF;
            // Grip left for driving after cornering (friction circle).
            double kHere = Math.Abs(Line.CurvAt(S));
            double useLat = Math.Min(1, vv * vv * kHere / Math.Max(0.1, Ability.LatMax(vv)));
            double full = Ability.AccelAt(vv);
            double avail = full * Math.Sqrt(Math.Max(0, 1 - useLat * useLat));
            double coast = Ability.DragK * vv * vv + 0.35;   // drag, rolling and engine braking off throttle
            double throttle, brake;
            if (aReq >= -coast)
            {
                double a = Math.Min(aReq, avail);
                throttle = Clamp((a + coast) / Math.Max(0.5, full + coast), 0, 1);
                brake = 0;
            }
            else
            {
                throttle = 0;
                double grip = Ability.BrakeG * CarAbility.G * (1 + Ability.AeroK * vv * vv);
                brake = Clamp((-aReq - coast) / Math.Max(1, grip), 0.03, 1);
            }
            // In a slide: off the brakes, ease the throttle.
            if (Math.Abs(beta) > 0.10 && vv > 8)
            {
                brake *= 0.3;
                throttle *= 0.5;
            }
            else if (yawOver > 0 && vv > 8)
            {
                brake *= Clamp(1 - yawOver * 7, 0.35, 1);
                throttle *= Clamp(1 - yawOver * 4, 0.4, 1);
            }
            // Braking shares the tyres with cornering, and the rear must keep enough grip to stay behind.
            brake *= Math.Sqrt(Math.Max(0.15, 1 - useLat * useLat));
            brake = Math.Min(brake, Math.Max(0.12, Ability.StableBrake(vv * vv * kHere / CarAbility.G) / Ability.BrakeG));
            // Off the road: slow to what grass and gravel allow, then rejoin.
            if (Math.Abs(Lateral) > edge + 1.5)
            {
                throttle = Math.Min(throttle, speed < 14 ? 0.7 : 0.25);
                if (speed > 22)
                {
                    throttle = 0;
                    brake = Math.Max(brake, Math.Abs(beta) > 0.10 ? 0.25 : 0.6);
                }
            }
            // Stopped behind a car (on the grid, in a jam): held on the handbrake. The brake pedal held
            // at a standstill selects reverse, and the car would back into the one behind it.
            bool hold = brake > 0 && throttle == 0 && Math.Abs(Car.vehicle.vLong) < 0.8;
            inp.throttle = throttle;
            inp.brake = hold ? 0 : brake;
            if (hold) inp.handbrake = 1;
            inp.shiftUp = false;
            inp.shiftDown = false;
        }

        double lead, lag;
        /// <summary>Lateral room left by the cars alongside this step (absolute, + = right).</summary>
        double sideMin, sideMax;

        /// <summary>The speed the driver wants at s, with their reaction: the profile lead metres ahead, or lag metres behind that while it rises.</summary>
        double Want(double s)
        {
            double a = TargetAt(s + lead);
            return lag > 0 ? Math.Min(a, TargetAt(s + lead - lag)) : a;
        }

        /// <summary>The driver's target speed at arc length s: the profile, mistakes, cool-down and stopping after a sprint.</summary>
        double TargetAt(double s)
        {
            double v = Profile.At(s) * mistakeSpeed;
            if (Cruise) v = Math.Min(v, 0.72 * Profile.At(s));
            if (!double.IsNaN(StopAt))
            {
                double left = StopAt - s;
                v = Math.Min(v, left <= 0 ? 0 : Math.Sqrt(2 * 3.5 * left));
            }
            return v;
        }

        /// <summary>
        /// Following, passing and defending. A pass is a small state machine: it
        /// starts when the driver is faster and close behind a car on their line,
        /// picks a side (the inside of the next corner if there is room), then
        /// keeps beside that car until it is clearly behind, or gives up after a
        /// while if it never got alongside.
        /// </summary>
        void Racecraft(double dt, IField field, double speed, double half, double wl, double wr, ref double vT)
        {
            double myLine = Line.OffsetAt(S);
            double minLat = -wl + half + 0.3, maxLat = wr - half - 0.3;
            double best = double.PositiveInfinity;
            AiDriverView ahead = default, target = default, behind = default;
            bool haveAhead = false, haveTarget = false;
            double nearBehind = double.PositiveInfinity;
            for (int i = 0; i < field.Count; i++)
            {
                var o = field.Get(i);
                if (o.id == Car.id) continue;
                double ds = Route.Delta(S, o.s);
                double lat = o.lateral - Lateral;
                // Alongside (or about to be): its side of the road is not mine. The
                // steering target keeps a car's width and a margin away from it.
                if (Math.Abs(ds) < (Ability.Length + o.length) * 0.5 + 1.5 && Math.Abs(lat) < half + o.halfWidth + 1.6)
                {
                    double sep = half + o.halfWidth + 0.6;
                    if (lat >= 0) sideMax = Math.Min(sideMax, o.lateral - sep);
                    else sideMin = Math.Max(sideMin, o.lateral + sep);
                }
                if (o.id == passId)
                {
                    target = o;
                    haveTarget = true;
                }
                if (ds > 0 && ds < best && ds < 80)
                {
                    // In my path if it overlaps where I am heading.
                    double myLat = Line.OffsetAt(S + ds) + Lane;
                    if (Math.Abs(o.lateral - myLat) < half + o.halfWidth + 0.5)
                    {
                        best = ds;
                        ahead = o;
                        haveAhead = true;
                    }
                }
                if (ds < 0 && -ds < nearBehind && -ds < 25)
                {
                    nearBehind = -ds;
                    behind = o;
                }
            }

            // --- A pass in progress ---------------------------------------------------------------
            if (passId >= 0)
            {
                double ds = haveTarget ? Route.Delta(S, target.s) : 0;
                double clear = haveTarget ? (Ability.Length + target.length) * 0.5 + 1.5 : 0;
                if (!haveTarget || ds < -clear || ds > 45 || (passT <= 0 && ds > clear))
                {
                    // Done (it is behind), lost, or never got alongside: back to the line, and a pause before the next try.
                    if (haveTarget && ds > -clear) passCooldown = 1.5;
                    passId = -1;
                    passSide = 0;
                }
                else
                {
                    double need = half + target.halfWidth + 0.8;
                    double want = target.lateral + passSide * need;
                    double lo = minLat + 0.3, hi = maxLat - 0.3;
                    if (want < lo || want > hi)
                    {
                        // The gap closed: try the other side, else back out rather than be squeezed off the road.
                        double other = target.lateral - passSide * need;
                        if (other >= lo && other <= hi && ds > 0)
                        {
                            passSide = -passSide;
                            want = other;
                        }
                        else
                        {
                            passId = -1;
                            passSide = 0;
                            passCooldown = 1.0;
                        }
                    }
                    if (passId >= 0) laneTarget += want - Line.OffsetAt(S + Math.Max(5, ds));
                }
            }

            // --- Following, and deciding to pass ---------------------------------------------------
            queued = false;
            if (haveAhead)
            {
                double gap = best - (Ability.Length + ahead.length) * 0.5;
                // A car spinning or rolling backwards counts as stopped: follow it, never chase its speed below zero.
                double aheadV = Math.Max(0, ahead.speed);
                double closing = speed - aheadV;
                double follow = 2.5 + Math.Max(0, speed) * (0.17 - 0.08 * P.Aggression);
                queued = gap < follow + 4 && aheadV < 2;
                double profileHere = Profile.At(S + best);
                bool faster = profileHere > aheadV + 0.5 || closing > 1.0;
                // Stay on the line until close (about half a second), then go for the gap; on a
                // straight the slipstream pulls the car in, so the move can start further back.
                double bend = 0;
                for (double d = 0; d <= 150; d += 15) bend += Math.Abs(Line.CurvAt(S + d));
                bool straight = bend < 0.012;
                bool close = gap < Math.Max(9, Math.Max(0, speed) * (0.45 + 0.25 * P.Aggression)) || (straight && gap < 26 && closing > -0.5);
                if (passId < 0 && faster && close && passCooldown <= 0 && P.Aggression > 0.15)
                {
                    double need = half + ahead.halfWidth + 0.8;
                    bool roomLeft = ahead.lateral - need >= minLat + 0.3;
                    bool roomRight = ahead.lateral + need <= maxLat - 0.3;
                    double k = 0;
                    for (double d = 20; d <= 120; d += 20) k += Line.CurvAt(S + d);
                    int inside = k > 0 ? 1 : -1;
                    int side = 0;
                    if (inside > 0 && roomRight) side = 1;
                    else if (inside < 0 && roomLeft) side = -1;
                    else if (roomRight && (!roomLeft || ahead.lateral < Lateral)) side = 1;
                    else if (roomLeft) side = -1;
                    if (side != 0)
                    {
                        passId = ahead.id;
                        passSide = side;
                        passT = 4.0;
                        laneTarget += ahead.lateral + side * need - Line.OffsetAt(S + Math.Max(5, best));
                    }
                }
                if (passId != ahead.id)
                {
                    // Follow: match speed at the following distance.
                    double vf = aheadV + 0.6 * (gap - follow);
                    if (gap < follow * 2.5) vT = Math.Min(vT, Math.Max(0, vf));
                }
                // Never drive into it: brake if the overlap remains and we are closing fast.
                double myLatThere = Line.OffsetAt(S + best) + Lane;
                if (Math.Abs(ahead.lateral - myLatThere) < half + ahead.halfWidth + 0.2 && closing > 0)
                {
                    double tts = gap / Math.Max(0.1, closing);
                    if (tts < 1.2) vT = Math.Min(vT, aheadV - 1);
                    // A much slower car in the way (traffic, a car that spun): no faster than lets the car
                    // brake to its speed by the following distance, at three quarters of what it can brake.
                    double room = Math.Max(0, gap - follow);
                    vT = Math.Min(vT, Math.Sqrt(aheadV * aheadV + 2 * 0.75 * Ability.BrakeMax(Math.Max(0, speed)) * room));
                }
            }
            if (passT > 0) passT -= dt;
            if (passCooldown > 0) passCooldown -= dt;
            // Defend: a faster car close behind, an aggressive driver moves to the inside.
            if (nearBehind < 14 && behind.speed > speed + 0.5 && P.Aggression > 0.55)
            {
                double k = 0;
                for (double d = 30; d <= 150; d += 30) k += Line.CurvAt(S + d);
                double inside = k > 0 ? 1 : -1;
                laneTarget += inside * 1.1 * P.Aggression;
            }
            laneTarget = Clamp(laneTarget, minLat - myLine, maxLat - myLine);
        }

        bool Recover(double dt, double speed, double edge)
        {
            var v = Car.vehicle;
            var inp = v.input;
            // Upside down or on its side: reset.
            if (v.up.y < 0.3) flipT += dt;
            else flipT = 0;
            // Far off the road.
            if (Dist > edge + 22) offT += dt;
            else offT = Math.Max(0, offT - dt);
            // Facing the wrong way.
            Route.At(S, out _, out _, out _, out double tx, out double tz);
            double along = v.fwd.x * tx + v.fwd.z * tz;
            if (along < -0.2 && speed < 6) wrongT += dt;
            else wrongT = 0;
            // Not moving while it should (waiting behind a car that is barely moving is not stuck: no reversing).
            if (Math.Abs(speed) < 0.8 && reverseT <= 0 && !queued) stuckT += dt;
            else if (reverseT <= 0) stuckT = Math.Max(0, stuckT - dt * 2);
            StuckTime = Math.Abs(speed) < 0.8 ? StuckTime + dt : 0;
            if (StuckTime > MaxStuck) MaxStuck = StuckTime;
            // Wedged against a car or a wall, rocking back and forth without getting anywhere: no
            // headway along the route for 8 s (the stuck test above wants a car standing still).
            if (double.IsNaN(headS) || Route.Delta(headS, S) > 4)
            {
                headS = S;
                headT = 0;
            }
            else headT += dt;
            if (flipT > 1.5 || offT > 4 || stuckT > 3.8 || wrongT > 3 || headT > 8)
            {
                OnEvent?.Invoke($"reset ({(flipT > 1.5 ? "flipped" : offT > 4 ? "off the road" : stuckT > 3.8 ? "stuck" : wrongT > 3 ? "wrong way" : "no headway")}) at s {S:0}, lateral {Lateral:0.0}, speed {speed:0.0}");
                ResetToRoute();
                return true;
            }
            // One attempt at reversing out; if the car is still stuck afterwards, the reset above follows.
            if (reverseT <= 0 && ((stuckT > 1.4 && stuckT < 2.0) || (wrongT > 1.2 && wrongT < 1.5)))
            {
                reverseT = 1.6;
                Reversals++;
            }
            if (reverseT > 0)
            {
                reverseT -= dt;
                // Holding the brake at a standstill selects reverse; steer to swing the nose towards the road.
                Route.At(S + 6, out double cx, out _, out double cz, out _, out _);
                double right = (cx - v.pos.x) * v.right.x + (cz - v.pos.z) * v.right.z;
                inp.steer = right > 0 ? -1 : 1;
                inp.throttle = 0;
                inp.brake = 0.8;
                inp.handbrake = 0;
                if (reverseT <= 0) stuckT = Math.Max(stuckT, 2.2);
                return true;
            }
            return false;
        }

        /// <summary>Back on the route, a few metres behind where it was, facing along the road.</summary>
        public void ResetToRoute()
        {
            var v = Car.vehicle;
            // A second reset near the last one: start further back, on the centreline.
            bool again = Resets > 0 && Math.Abs(Route.Delta(lastResetS, S)) < 40;
            lastResetS = S;
            double s = Route.Wrap(S - (again ? 30 : 8));
            Route.At(s, out double x, out double y, out double z, out double tx, out double tz);
            double off = again ? 0 : Line.OffsetAt(s);
            x -= tz * off;
            z += tx * off;
            Placer?.Invoke(v, x, y, z, Simulation.YawOf(tx, tz));
            Resets++;
            flipT = offT = stuckT = wrongT = reverseT = headT = 0;
            headS = s;
            StuckTime = 0;
            Lane = 0;
            S = s;
        }

        public double DebugA, DebugFF;
        public int DebugPassSide => passSide;
        /// <summary>Diagnostics: the steering target's offset to the right of the car, m, and the sideslip, rad.</summary>
        public double DebugRight, DebugBeta;
        public double DebugPassT => passT;
        public double DebugLaneTarget => laneTarget;

        /// <summary>Diagnostics: resets and their causes.</summary>
        public Action<string> OnEvent;
        /// <summary>Largest distance beyond the drivable edge so far, m.</summary>
        public double MaxOff;

        /// <summary>Puts the car down at (x, z, yaw) on the road at the route's height y there (set by the race).</summary>
        public Action<Vehicle, double, double, double, double> Placer;

        static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;
    }
}
