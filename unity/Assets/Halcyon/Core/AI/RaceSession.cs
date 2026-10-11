// A race: an event's route, a grid of cars (the player plus AI drivers), a
// countdown, then laps or a sprint to the finish, positions, lap times and
// results. Runs on the simulation's fixed step, so the same race can be
// played in Unity or run headless by the AI tournaments.
using System;
using System.Collections.Generic;

namespace Halcyon.Core
{
    public enum RaceKind { Circuit, Sprint }

    public enum RaceState { Countdown, Racing, Finished }

    public sealed class RaceEvent
    {
        public string Id, Name, Description;
        public RaceKind Kind;
        public int Laps = 1;
        public RaceRoute Route;
        /// <summary>Start line (arc length on the route).</summary>
        public double StartS;
        /// <summary>Race distance from the start line, m.</summary>
        public double Distance;
        public int GridSize = 8;
        /// <summary>The highest class allowed (null: open to every car). Rivals are matched to the player's PI within it.</summary>
        public PiClass? ClassLimit;
    }

    public static class EventCatalog
    {
        public static RaceEvent ProvingGP(WorldData w, int laps = 3)
        {
            var r = RaceRoute.FromProving(w.Proving, "Proving Ground");
            return new RaceEvent
            {
                Id = "proving-gp",
                Name = "Proving Ground Grand Prix",
                Description = $"{laps} laps of the {r.Length / 1000:0.0} km proving circuit.",
                Kind = RaceKind.Circuit,
                Laps = laps,
                Route = r,
                StartS = 0,
                Distance = laps * r.Length,
            };
        }

        /// <summary>A point-to-point race over the island's roads between two places.</summary>
        public static RaceEvent Sprint(WorldData w, IslandGround g, string id, string name, double ax, double az, double bx, double bz)
        {
            var r = RouteFinder.Find(w, g, ax, az, bx, bz, name);
            if (r == null || r.Length < 800) return null;
            const double start = 50, runOff = 160;
            return new RaceEvent
            {
                Id = id,
                Name = name,
                Description = $"{(r.Length - start - runOff) / 1000:0.0} km on the open road.",
                Kind = RaceKind.Sprint,
                Route = r,
                StartS = start,
                Distance = r.Length - start - runOff,
            };
        }

        static Poi FindPoi(WorldData w, string name)
        {
            foreach (var p in w.Pois) if (p.name == name) return p;
            return null;
        }

        /// <summary>Every event on the island.</summary>
        public static List<RaceEvent> All(WorldData w, IslandGround g)
        {
            var gp = ProvingGP(w);
            gp.ClassLimit = PiClass.A;
            var list = new List<RaceEvent> { gp };
            var sp = w.Spawn;
            void Add(string id, string name, string to, PiClass? limit)
            {
                var p = FindPoi(w, to);
                if (p == null) return;
                var e = Sprint(w, g, id, name, sp.x, sp.z, p.x, p.z);
                if (e == null) return;
                e.ClassLimit = limit;
                list.Add(e);
            }
            // Classes: the starter car (A) can enter three events; the cheap D
            // cars open the Saltmarsh run and a B car the Ashby sprint.
            Add("sprint-port-calder", "Festival to Port Calder", "Port Calder", null);
            Add("sprint-harbor", "Harbour Dash", "Harbor", PiClass.S1);
            Add("sprint-ashby", "Ashby Sprint", "Ashby", PiClass.B);
            Add("sprint-saltmarsh", "Saltmarsh Bay Run", "Saltmarsh Bay", PiClass.D);
            return list;
        }
    }

    public sealed class Entrant
    {
        public SimCar Car;
        public AiDriver Ai;
        public string Name;
        public bool Player;
        public int Grid;
        /// <summary>Metres covered from the start line along the route (negative on the grid).</summary>
        public double Progress;
        /// <summary>Current arc length on the route (wrapped), lateral offset and distance from the centreline.</summary>
        public double S, Lateral, Dist;
        public int LapsDone;
        public double LapStart, LastLap = double.NaN, BestLap = double.NaN;
        public bool Finished;
        public double FinishTime = double.NaN;
        public int Position;
        /// <summary>The car's performance index, when known (0 otherwise).</summary>
        public int Pi;
        public bool WrongWay;
        double backT;

        internal void Track(RaceRoute r, double dt)
        {
            var v = Car.vehicle;
            r.Project(v.pos.x, v.pos.z, S, 30, 70, out double s, out double lat, out double dist);
            double d = r.Delta(S, s);
            if (Math.Abs(d) < 60) Progress += d;
            S = s;
            Lateral = lat;
            Dist = dist;
            r.At(s, out _, out _, out _, out double tx, out double tz);
            double along = v.vel.x * tx + v.vel.z * tz;
            backT = along < -2 ? backT + dt : 0;
            WrongWay = backT > 1.0;
        }
    }

    public sealed class RaceSession : AiDriver.IField
    {
        public const double CountdownTime = 3.0;

        public readonly Simulation Sim;
        public readonly RaceEvent Event;
        public readonly RacingLine Line;
        public readonly List<Entrant> Entrants = new List<Entrant>();
        public Entrant PlayerEntrant;
        public RaceState State = RaceState.Countdown;
        /// <summary>Seconds since the start (negative during the countdown).</summary>
        public double Clock = -CountdownTime;
        /// <summary>Seconds since the player finished (results wait a little for the field).</summary>
        public double SinceFinish;
        /// <summary>Car-to-car contacts since the start (a contact lasting several steps counts once).</summary>
        public int Contacts;
        /// <summary>Hits on walls, rails and props since the start, counted the same way.</summary>
        public int WallHits;
        readonly Dictionary<long, long> lastContact = new Dictionary<long, long>();
        /// <summary>Diagnostics: called on each new wall hit with the entrant and the impulse.</summary>
        public Action<Entrant, double> WallHitAt;
        /// <summary>Diagnostics: called on each new car-to-car contact with both entrants and the impulse.</summary>
        public Action<Entrant, Entrant, double> ContactAt;
        readonly Controls hold = new Controls { handbrake = 1 };
        bool playerRemoved;
        readonly List<Entrant> order = new List<Entrant>();

        /// <summary>Measured car abilities, shared between races.</summary>
        public sealed class AbilityCache
        {
            readonly Dictionary<string, CarAbility> map = new Dictionary<string, CarAbility>();

            public CarAbility Get(string id) => Get(id, null, null, "stock");

            /// <summary>
            /// A car with a tune and parts. key names the setup (Rival.Key), so
            /// equal setups share one measurement. Thread-safe: grids can be
            /// measured in parallel.
            /// </summary>
            public CarAbility Get(string id, Tune tune, Upgrades up, string key)
            {
                string k = id + "|" + key;
                lock (map)
                {
                    if (map.TryGetValue(k, out var a)) return a;
                }
                var m = CarAbility.Measure(Cars.ById(id), tune, up);
                lock (map)
                {
                    if (!map.ContainsKey(k)) map[k] = m;
                    return map[k];
                }
            }
        }

        /// <summary>
        /// Builds the grid. The player's car (if any) takes grid slot playerGrid;
        /// opponents fill the rest in order. With includePlayer false the race is
        /// AI only and the player's car is parked out of the way.
        /// </summary>
        public RaceSession(Simulation sim, RaceEvent ev, IList<(string carId, Personality p)> opponents, AbilityCache abilities, uint seed, bool includePlayer = true, int playerGrid = -1, RacingLine line = null)
            : this(sim, ev, Rivals.Stock(opponents), abilities, seed, includePlayer, playerGrid, line)
        {
        }

        /// <summary>The same, with rivals whose cars may carry parts (Rivals.Field).</summary>
        public RaceSession(Simulation sim, RaceEvent ev, IList<Rival> opponents, AbilityCache abilities, uint seed, bool includePlayer = true, int playerGrid = -1, RacingLine line = null)
        {
            Sim = sim;
            Event = ev;
            Line = line ?? RacingLine.Build(ev.Route);
            // Clear any previous race's cars.
            for (int i = sim.Cars.Count - 1; i >= 0; i--) if (!sim.Cars[i].player) sim.Cars.RemoveAt(i);
            int total = opponents.Count + (includePlayer ? 1 : 0);
            if (playerGrid < 0 || playerGrid >= total) playerGrid = total - 1;
            int k = 0;
            for (int slot = 0; slot < total; slot++)
            {
                Entrant e;
                if (includePlayer && slot == playerGrid)
                {
                    e = new Entrant { Car = sim.Player, Name = "You", Player = true };
                    PlayerEntrant = e;
                }
                else
                {
                    var r = opponents[k++];
                    var up = Rivals.PartsKey(r.Parts) == "stock" ? null : r.Upgrades;
                    var car = sim.AddCar(r.CarId, false, r.Tune, up);
                    car.name = r.Driver.Name;
                    var ai = new AiDriver(car, r.Driver, abilities.Get(car.specId, r.Tune, up, r.Key), seed * 7919u + (uint)slot) { Placer = Place };
                    e = new Entrant { Car = car, Ai = ai, Name = r.Driver.Name, Pi = r.Pi };
                }
                e.Grid = slot;
                Entrants.Add(e);
                PlaceOnGrid(e, slot);
            }
            if (!includePlayer)
            {
                // AI only: the player's car leaves the simulation until the race ends.
                sim.Cars.Remove(sim.Player);
                playerRemoved = true;
            }
            foreach (var e in Entrants)
            {
                e.Ai?.SetRoute(ev.Route, Line);
                if (e.Ai != null) e.Ai.Hold = true;
                e.Track(ev.Route, 0);
                e.Progress = ev.Route.Delta(ev.StartS, e.S);
                e.LapStart = 0;
            }
            sim.Filter.Reset();
            Rank();
        }

        void PlaceOnGrid(Entrant e, int slot)
        {
            var r = Event.Route;
            int row = slot / 2, col = slot % 2;
            double s = Event.StartS - 7 - row * 8 - col * 4;
            r.At(s, out double x, out _, out double z, out double tx, out double tz);
            double wl = r.Sample(r.WLeft, s), wr = r.Sample(r.WRight, s);
            double half = e.Car.vehicle.Params.spec.width / 2;
            double lat = col == 0 ? -Math.Min(2.3, wl - half - 0.3) : Math.Min(2.3, wr - half - 0.3);
            x -= tz * lat;
            z += tx * lat;
            Place(e.Car.vehicle, x, z, Simulation.YawOf(tx, tz));
            e.S = r.Wrap(s);
        }

        void Place(Vehicle v, double x, double z, double yaw)
        {
            v.Place(x, Sim.Ground.GroundHeight(x, z), z, yaw);
            if (v == Sim.Player.vehicle) Sim.Filter.Reset();
        }

        public int Count => Entrants.Count;

        public AiDriver.AiDriverView Get(int i)
        {
            var e = Entrants[i];
            var v = e.Car.vehicle;
            return new AiDriver.AiDriverView
            {
                id = e.Car.id,
                s = e.S,
                lateral = e.Lateral,
                speed = v.vLong,
                halfWidth = v.Params.spec.width / 2,
                length = v.Params.spec.length,
            };
        }

        /// <summary>One fixed step: the AI plans, everything moves, progress and positions update.</summary>
        public void Step(Controls player)
        {
            double dt = Loop.SIM_DT;
            bool racing = Clock >= 0;
            if (racing && State == RaceState.Countdown) State = RaceState.Racing;
            foreach (var e in Entrants)
            {
                if (e.Ai == null) continue;
                e.Ai.Hold = !racing;
                e.Ai.Cruise = e.Finished;
                e.Ai.StopAt = Event.Kind == RaceKind.Sprint && e.Finished ? Event.Route.Length - 8 : double.NaN;
                e.Ai.Update(dt, this);
            }
            Drafting();
            long tick = Sim.Tick;
            Sim.Step(racing ? player : hold);
            if (racing) CountContacts(tick);
            Clock += dt;
            if (!racing) return;
            foreach (var e in Entrants)
            {
                e.Track(Event.Route, dt);
                if (e.Finished) continue;
                if (Event.Kind == RaceKind.Circuit)
                {
                    int laps = (int)Math.Floor(e.Progress / Event.Route.Length);
                    if (laps > e.LapsDone && laps >= 1)
                    {
                        double t = Clock - e.LapStart;
                        e.LastLap = t;
                        if (!(e.BestLap <= t)) e.BestLap = t;
                        e.LapStart = Clock;
                        e.LapsDone = laps;
                    }
                }
                if (e.Progress >= Event.Distance)
                {
                    e.Finished = true;
                    e.FinishTime = Clock;
                }
            }
            Rank();
            if (State == RaceState.Racing && PlayerEntrant != null && PlayerEntrant.Finished) State = RaceState.Finished;
            if (State == RaceState.Finished) SinceFinish += dt;
        }

        /// <summary>
        /// Sorts this step's impacts into car-to-car contacts (the event sits at
        /// the midpoint of the two cars) and wall hits; a contact that continues
        /// within half a second is the same contact.
        /// </summary>
        void CountContacts(long tick)
        {
            long gap = (long)(0.5 / Loop.SIM_DT);
            foreach (var ev in Sim.Events)
            {
                if (ev.tick != tick || ev.kind != SimEventKind.Impact) continue;
                Entrant a = null, b = null;
                foreach (var e in Entrants)
                {
                    if (e.Car.id != ev.carId) continue;
                    a = e;
                    break;
                }
                if (a == null) continue;
                foreach (var e in Entrants)
                {
                    if (e == a) continue;
                    var pa = a.Car.vehicle.pos;
                    var pb = e.Car.vehicle.pos;
                    double mx = (pa.x + pb.x) / 2 - ev.x, mz = (pa.z + pb.z) / 2 - ev.z;
                    if (mx * mx + mz * mz < 1e-6)
                    {
                        b = e;
                        break;
                    }
                }
                long key = b == null ? -1 - a.Car.id : Math.Min(a.Car.id, b.Car.id) * 100000L + Math.Max(a.Car.id, b.Car.id);
                bool fresh = !lastContact.TryGetValue(key, out long last) || tick - last > gap;
                lastContact[key] = tick;
                if (!fresh) continue;
                if (b != null)
                {
                    Contacts++;
                    ContactAt?.Invoke(a, b, ev.value);
                }
                else
                {
                    WallHits++;
                    WallHitAt?.Invoke(a, ev.value);
                }
            }
        }

        /// <summary>A car tucked in behind another loses some drag (applies to the player too).</summary>
        void Drafting()
        {
            foreach (var a in Entrants)
            {
                double best = 1;
                var va = a.Car.vehicle;
                foreach (var b in Entrants)
                {
                    if (a == b) continue;
                    double ds = Event.Route.Delta(a.S, b.S);
                    if (ds <= 3 || ds > 30) continue;
                    if (Math.Abs(b.Lateral - a.Lateral) > 1.4) continue;
                    double f = 1 - 0.32 * (1 - (ds - 3) / 27);
                    if (f < best) best = f;
                }
                va.draftFactor = best;
            }
        }

        void Rank()
        {
            order.Clear();
            order.AddRange(Entrants);
            order.Sort((x, y) =>
            {
                if (x.Finished && y.Finished) return x.FinishTime.CompareTo(y.FinishTime);
                if (x.Finished) return -1;
                if (y.Finished) return 1;
                int c = y.Progress.CompareTo(x.Progress);
                return c != 0 ? c : x.Grid.CompareTo(y.Grid);
            });
            for (int i = 0; i < order.Count; i++) order[i].Position = i + 1;
        }

        /// <summary>Entrants in race order.</summary>
        public IReadOnlyList<Entrant> Order => order;

        public bool AllFinished
        {
            get
            {
                foreach (var e in Entrants) if (!e.Finished) return false;
                return true;
            }
        }

        public int Lap(Entrant e) => Event.Kind == RaceKind.Circuit ? Math.Min(Event.Laps, Math.Max(1, e.LapsDone + 1)) : 1;

        /// <summary>Put the player back on the race route a few metres behind where they were, facing along it.</summary>
        public void ResetPlayer()
        {
            var e = PlayerEntrant;
            if (e == null) return;
            var r = Event.Route;
            double s = r.Wrap(e.S - 5);
            r.At(s, out double x, out _, out double z, out double tx, out double tz);
            double off = Line.OffsetAt(s);
            x -= tz * off;
            z += tx * off;
            Place(e.Car.vehicle, x, z, Simulation.YawOf(tx, tz));
            e.Progress += r.Delta(e.S, s);
            e.S = s;
        }

        /// <summary>Remove the AI cars; the player's car stays where it is.</summary>
        public void End()
        {
            foreach (var e in Entrants)
            {
                if (e.Ai != null) Sim.Cars.Remove(e.Car);
                e.Car.vehicle.draftFactor = 1;
            }
            if (playerRemoved && !Sim.Cars.Contains(Sim.Player)) Sim.Cars.Insert(0, Sim.Player);
            playerRemoved = false;
        }

        /// <summary>Opponents for a race: cars with a power-to-weight close to the player's, with drivers around a difficulty.</summary>
        public static List<(string carId, Personality p)> Field(string playerCar, int count, Difficulty d, uint seed)
        {
            var rng = new Rng(seed);
            var mine = VehicleParams.Build(Cars.ById(playerCar));
            double pw = mine.engine.peakPower / mine.mass;
            var close = new List<(double score, string id)>();
            foreach (var c in Cars.All)
            {
                var p = VehicleParams.Build(c);
                double r = Math.Abs(Math.Log((p.engine.peakPower / p.mass) / pw));
                close.Add((r, c.id));
            }
            close.Sort((a, b) => a.score.CompareTo(b.score));
            var pool = new List<string>();
            foreach (var (score, id) in close) if (score < 0.45 || pool.Count < 3) pool.Add(id);
            var drivers = Personalities.Field(d, count, rng);
            var o = new List<(string, Personality)>();
            for (int i = 0; i < count; i++) o.Add((pool[(i + rng.Int(0, pool.Count)) % pool.Count], drivers[i]));
            return o;
        }
    }
}
