// The simulation on the island: everything that advances on the fixed
// 240 Hz clock. A port of the web version's src/sim.ts on top of the
// exported island (WorldData + IslandGround). No UnityEngine.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public sealed class SimCar
    {
        public int id;
        public string name;
        public string specId;
        public Vehicle vehicle;
        public bool player;
    }

    public enum SimEventKind { Impact, Break, Lap, Reset }

    public struct SimEvent
    {
        public SimEventKind kind;
        public int carId;
        public double x, z;
        /// <summary>Impulse (N·s) for impacts, lap time (s) for laps.</summary>
        public double value;
        public long tick;
    }

    public sealed class LapState
    {
        public int laps;
        public double start;
        public double last = double.NaN;
        public double best = double.NaN;
        public int sector;
        /// <summary>Arc length along the circuit from the start line, m.</summary>
        public double s;
    }

    public enum SpawnAt { Island, Proving }

    public sealed class Simulation
    {
        public long Tick;
        public double Time;
        public readonly WorldData World;
        public readonly IslandGround Ground;
        public readonly Colliders Colliders;
        public SpawnAt Spawn;
        public readonly List<SimCar> Cars = new List<SimCar>();
        public SimCar Player;
        public readonly PlayerControlFilter Filter = new PlayerControlFilter();
        /// <summary>Presentation events since the last drain (impacts, breaks, laps, resets).</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public LapState Lap = new LapState();
        /// <summary>The car the player drives (an id from Cars.All).</summary>
        public string PlayerCarId;
        readonly NearestResult near = new NearestResult();
        readonly RoadHit roadScratch = new RoadHit();
        int nextId = 1;
        SimCar breakCar;
        readonly Action<Circle> onBreak;

        public Simulation(WorldData world, SpawnAt spawn = SpawnAt.Island, string playerCar = null)
        {
            World = world;
            Ground = new IslandGround(world);
            Colliders = Ground.BuildColliders();
            Spawn = spawn;
            PlayerCarId = playerCar ?? Halcyon.Core.Cars.All[0].id;
            onBreak = prop => Emit(SimEventKind.Break, breakCar.id, prop.x, prop.z, 0);
            Reset();
        }

        public void Reset()
        {
            Tick = 0;
            Time = 0;
            Cars.Clear();
            nextId = 1;
            Events.Clear();
            Filter.Reset();
            Ground.BrokenTrees.Clear();
            Player = AddCar(PlayerCarId, true);
            if (Spawn == SpawnAt.Proving)
            {
                var p = World.Proving;
                Player.vehicle.Place(p.spawnX, Ground.GroundHeight(p.spawnX, p.spawnZ), p.spawnZ, p.spawnYaw);
            }
            else
            {
                var sp = World.Spawn;
                Player.vehicle.Place(sp.x, Ground.GroundHeight(sp.x, sp.z), sp.z, sp.yaw);
            }
            Lap = new LapState();
        }

        public SimCar AddCar(string specId, bool player = false)
        {
            CarSpec spec = Array.Find(Halcyon.Core.Cars.All, c => c.id == specId) ?? Halcyon.Core.Cars.All[0];
            var v = new Vehicle(VehicleParams.Build(spec)) { aids = Aids.Default() };
            var car = new SimCar { id = nextId++, name = player ? "You" : spec.model, specId = spec.id, vehicle = v, player = player };
            Cars.Add(car);
            return car;
        }

        /// <summary>One fixed step of Loop.SIM_DT with the player's controls.</summary>
        public void Step(Controls c)
        {
            double dt = Loop.SIM_DT;
            var pv = Player.vehicle;
            pv.input = Filter.Update(c, pv, dt);
            foreach (var car in Cars) car.vehicle.Step(dt, Ground);
            // Collisions: world first, then every pair of cars.
            foreach (var car in Cars)
            {
                breakCar = car;
                var hit = CarCollision.CollideWorld(car.vehicle, Colliders, onBreak);
                if (hit.HasValue && hit.Value.impulse > 300) Emit(SimEventKind.Impact, car.id, hit.Value.x, hit.Value.z, hit.Value.impulse);
            }
            for (int i = 0; i < Cars.Count; i++)
            {
                for (int j = i + 1; j < Cars.Count; j++)
                {
                    var a = Cars[i].vehicle;
                    var b = Cars[j].vehicle;
                    double imp = CarCollision.CollideCars(a, b);
                    if (imp > 300) Emit(SimEventKind.Impact, Cars[i].id, (a.pos.x + b.pos.x) / 2, (a.pos.z + b.pos.z) / 2, imp);
                }
            }
            UpdateLap();
            Tick++;
            Time += dt;
        }

        void Emit(SimEventKind kind, int carId, double x, double z, double value)
        {
            if (Events.Count > 256) Events.RemoveAt(0);
            Events.Add(new SimEvent { kind = kind, carId = carId, x = x, z = z, value = value, tick = Tick });
        }

        /// <summary>Lap timing on the proving circuit, with three sectors so cutting back over the line does not count.</summary>
        void UpdateLap()
        {
            var v = Player.vehicle;
            var road = World.Proving.road;
            if (!road.Nearest(v.pos.x, v.pos.z, near)) return;
            double L = road.length;
            double s = near.s - World.Proving.startS;
            if (s < 0) s += L;
            double prev = Lap.s;
            Lap.s = s;
            int sector = s < L / 3 ? 0 : s < (2 * L) / 3 ? 1 : 2;
            if (sector == (Lap.sector + 1) % 3 && Math.Abs(s - prev) < 50) Lap.sector = sector;
            // Crossing the line forward: s wraps from near L to near 0.
            if (prev > L - 60 && s < 60 && near.dist < 30)
            {
                if (Lap.sector == 2)
                {
                    double t = Time - Lap.start;
                    if (Lap.laps > 0)
                    {
                        Lap.last = t;
                        if (!(Lap.best <= t)) Lap.best = t;
                        Emit(SimEventKind.Lap, Player.id, v.pos.x, v.pos.z, t);
                    }
                    Lap.laps++;
                }
                else if (Lap.laps == 0)
                {
                    // First crossing starts the clock.
                    Lap.laps = 1;
                }
                Lap.start = Time;
                Lap.sector = 0;
            }
        }

        /// <summary>Swap the player's car for another model (an id from Cars.All) at the same place and heading.</summary>
        public void ChangePlayerCar(string specId)
        {
            var old = Player.vehicle;
            double x = old.pos.x, z = old.pos.z;
            double yaw = YawOf(old.fwd.x, old.fwd.z);
            CarSpec spec = Array.Find(Halcyon.Core.Cars.All, c => c.id == specId) ?? Halcyon.Core.Cars.All[0];
            var v = new Vehicle(VehicleParams.Build(spec)) { aids = old.aids };
            PlayerCarId = spec.id;
            Player.specId = spec.id;
            Player.vehicle = v;
            v.Place(x, Ground.GroundHeight(x, z), z, yaw);
            Filter.Reset();
            Emit(SimEventKind.Reset, Player.id, x, z, 0);
        }

        public void Teleport(double x, double z, double yaw = 0, double? y = null)
        {
            if (!(M.IsFinite(x) && M.IsFinite(z) && M.IsFinite(yaw))) throw new ArgumentException("teleport: non-finite argument");
            Player.vehicle.Place(x, y ?? Ground.GroundHeight(x, z), z, yaw);
            Filter.Reset();
        }

        /// <summary>Put the player back on the nearest road (circuit or island network), facing along it.</summary>
        public void ResetToRoad()
        {
            var v = Player.vehicle;
            double px = v.pos.x, pz = v.pos.z;
            double x = px, z = pz;
            double yaw = DMath.Atan2(-v.fwd.x, -v.fwd.z);
            var road = World.Proving.road;
            road.NearestAny(px, pz, near);
            double bestD = near.dist;
            road.At(near.s, out double bx, out _, out double bz, out double btx, out double btz, out _);
            foreach (var e in World.Edges)
            {
                for (int i = 0; i < e.n; i += 2)
                {
                    double d = Math.Sqrt((e.x[i] - px) * (e.x[i] - px) + (e.z[i] - pz) * (e.z[i] - pz));
                    if (d < bestD)
                    {
                        bestD = d;
                        bx = e.x[i];
                        bz = e.z[i];
                        double same = e.tx[i] * v.fwd.x + e.tz[i] * v.fwd.z >= 0 ? 1 : -1;
                        btx = e.tx[i] * same;
                        btz = e.tz[i] * same;
                    }
                }
            }
            if (bestD > 3)
            {
                x = bx;
                z = bz;
                yaw = DMath.Atan2(-btx, -btz);
            }
            v.Place(x, Ground.GroundHeight(x, z), z, yaw);
            Filter.Reset();
            Emit(SimEventKind.Reset, Player.id, x, z, 0);
        }

        /// <summary>True when the player is on a paved or dirt road surface.</summary>
        public bool OnRoad()
        {
            var v = Player.vehicle;
            if (World.Proving.road.Nearest(v.pos.x, v.pos.z, near) && near.dist <= 6.5) return true;
            return Ground.NearestRoad(v.pos.x, v.pos.z, 0.5, roadScratch) && roadScratch.dist <= roadScratch.halfWidth + 0.5;
        }

        /// <summary>Yaw for a direction vector (x, z) in the simulation's convention (yaw 0 faces -z).</summary>
        public static double YawOf(double x, double z) => DMath.Atan2(-x, -z);
    }
}
