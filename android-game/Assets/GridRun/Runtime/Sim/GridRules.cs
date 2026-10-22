using System;
using System.Collections.Generic;

namespace GridRun.Sim
{
    // Faithful C# port of dist/simulation.js and dist/arena-rules.js. Field names, call order,
    // random-number consumption, and floating-point operation order follow the JavaScript so a
    // seeded round replays identically (checked by Editor/SimParity against recorded browser traces).
    public sealed class Segment
    {
        public int Id;
        public string OwnerId;
        public Rider Rider;
        public double X1, Z1, X2, Z2;
        public bool Active;
        public double ClosedAt;
        public Segment Copy() => (Segment)MemberwiseClone();
    }

    public sealed class PendingTurn { public int Sign; public double ExpiresAt; }

    public sealed class ShiftTactic { public double Aggression, FootAt, Dodge; public bool PreferCycle; public int Strafe; }

    public sealed class Rider
    {
        public string Id;
        public double X, Z;
        public int Dir;
        public float[] Color;
        public bool IsPlayer;
        public string Controller, Style;
        public double TacticBias, TacticAt;
        public bool Alive;
        public double LastTurnAt;
        public PendingTurn PendingTurn;
        public double NextDecisionAt;
        public int? IgnoreSegmentId;
        public double DistanceSinceTurn;
        public Segment Segment;
        public int Ammo;
        public double NextShotAt;
        public bool FireHeld, AutoDrive, ThrottleHeld;
        public string FlightKind;
        public double FlightStartedAt, FlightUntil, JetReadyAt, SailReadyAt;
        public double NitroUntil;
        public bool RocketReady;
        // Grid Shift only.
        public string Form;
        public int Hp;
        public double FormReadyAt, RecoveryUntil, DiscReadyAt, FormChangedAt, FootTime, AiNextAt;
        public double InputX, InputZ;
        public ShiftTactic Tactic;

        public Rider Copy() => (Rider)MemberwiseClone();
    }

    public sealed class Bullet { public string OwnerId; public double X, Z, Traveled; public int Vx, Vz; public bool Rocket; }

    public sealed class Pickup { public int Id; public string Type; public bool Active; public double X, Z, ExpiresAt; }

    public sealed class Outcome { public string Kind, Reason, By, WinnerId; }

    public sealed class SimEvent
    {
        public string Type, RiderId, Kind, Reason, By, Pickup, Style, Form, Message, Name;
        public int Sign, Ammo, Points, Progress, Total, Hp;
        public double X, Z, Limit;
        public bool NearMiss, Rocket;
        public Outcome Outcome;
        public int Score;
    }

    public sealed class Breakdown { public int Derez, CloseCall, Trap, Checkpoint, Win; }

    public sealed class OperationState
    {
        public string Id;
        public int Progress, PulseIndex, CollapseIndex;
        public string Warning = "";
        public int? TargetLane, AnnouncedGate, MissedGate;
        public double? NextLimit, WarnedLimit;
        public Beam Beam;
    }

    public sealed class Beam { public double X, FiresAt, EndsAt; public bool Active; }

    public sealed class Disc { public string OwnerId; public double X, Z, Vx, Vz, Age; public bool Returning; }

    public sealed class ShiftState
    {
        public List<Disc> Discs = new List<Disc>();
        public List<Bullet> Bullets = new List<Bullet>();
        public double TimeLimit;
        public string AiIntent = "reading lanes";
    }

    public sealed class RiderSpec
    {
        public string Id;
        public double X, Z;
        public int Dir;
        public float[] Color;
        public bool IsPlayer;
        public string Controller, Style;
        public RiderSpec(string id, Spawn spawn, bool isPlayer = false, string controller = null, string style = null, float[] color = null)
        {
            Id = id; X = spawn.X; Z = spawn.Z; Dir = spawn.Dir; IsPlayer = isPlayer;
            Controller = controller; Style = style; Color = color;
        }
    }

    public sealed class Round
    {
        public uint Seed;
        public bool Multiplayer;
        public uint RandomState;
        public int Tick;
        public double Elapsed;
        public double Speed;
        public string Map;
        public double Limit, BaseLimit;
        public string Rule;
        public double TrailLifetime;
        public int Score;
        public Breakdown ScoreBreakdown = new Breakdown();
        public string Difficulty;
        public bool NearMissAwarded, TrapAwarded;
        public Outcome Outcome;
        public List<Rider> Riders = new List<Rider>();
        public List<Segment> Segments = new List<Segment>();
        public List<Bullet> Bullets = new List<Bullet>();
        public List<Obstacle> Obstacles = new List<Obstacle>();
        public List<Obstacle> DynamicObstacles = new List<Obstacle>();
        public List<SimEvent> Events = new List<SimEvent>();
        public int ActionCount;
        public int NextSegmentId = 1;
        public bool HasOpponents;
        public OperationState Operation;
        public ShiftState Shift;
        public List<Pickup> Pickups = new List<Pickup>();
        public string[] PickupTypes;
        public double NextPickupAt;
        public int NextPickupId, PickupSerial;
        public bool HasLastPickupPoint;
        public double LastPickupX, LastPickupZ;

        public Rider Find(string id)
        {
            foreach (var rider in Riders) if (rider.Id == id) return rider;
            return null;
        }
        public Rider Player
        {
            get { foreach (var rider in Riders) if (rider.IsPlayer) return rider; return null; }
        }
    }

    public sealed class Difficulty { public string Label; public double Interval, Horizon, Margin; }
    public sealed class RivalProfile { public string Key, Id, Label, Style, Cue; public float[] Color; }
    public sealed class FlightSpec { public double Duration, Cooldown, Speed; }

    public static class GridRules
    {
        public const double STEP = 1.0 / 120;
        public const double SPEED = 10.6;
        public const double RIDER_RADIUS = 0.3;
        public const int ROUND_AMMO = 24;
        public const double SHRINK_START = 180, SHRINK_INTERVAL = 15, ROUND_END = 300;
        const double TRAIL_HALF_WIDTH = 0.09, TURN_COOLDOWN = 0.16, TURN_BUFFER = 0.22;
        const double BULLET_SPEED = 48, BULLET_RANGE = 20, FIRE_INTERVAL = 0.12, BULLET_GAP = 1.35;
        const double CORNER_CLEARANCE = RIDER_RADIUS + TRAIL_HALF_WIDTH + 0.02;
        const double EPSILON = 1e-8;
        const double FIRST_PICKUP_AT = 4, PICKUP_INTERVAL = 8, PICKUP_LIFETIME = 14;
        public static readonly int[][] Dir = { new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 } };
        static readonly string[] PickupTypeOrder = { "ammo", "nitro", "rocket" };

        public static class Colors
        {
            public static readonly float[] Player = { .15f, .88f, 1f };
            public static readonly float[] Orange = { 1f, .38f, .09f };
            public static readonly float[] Violet = { .75f, .30f, 1f };
            public static readonly float[] Gold = { 1f, .73f, .20f };
        }

        public static readonly Dictionary<string, RivalProfile> Rivals = new Dictionary<string, RivalProfile>
        {
            ["survivor"] = new RivalProfile { Key = "survivor", Id = "AEGIS", Label = "Aegis", Style = "Survivor", Cue = "Protects open lanes", Color = Colors.Violet },
            ["hunter"] = new RivalProfile { Key = "hunter", Id = "VANTA", Label = "Vanta", Style = "Hunter", Cue = "Pressures your route", Color = Colors.Orange },
            ["trapper"] = new RivalProfile { Key = "trapper", Id = "KNOT", Label = "Knot", Style = "Trapper", Cue = "Closes exits with light walls", Color = Colors.Gold },
        };
        public static readonly string[] RivalOrder = { "hunter", "survivor", "trapper" };

        public static readonly Dictionary<string, Difficulty> Difficulties = new Dictionary<string, Difficulty>
        {
            ["rookie"] = new Difficulty { Label = "Rookie", Interval = 0.42, Horizon = 8, Margin = 1.3 },
            ["standard"] = new Difficulty { Label = "Standard", Interval = 0.26, Horizon = 11, Margin = 0.65 },
            ["expert"] = new Difficulty { Label = "Expert", Interval = 0.15, Horizon = 15, Margin = 0.2 },
        };
        public static readonly string[] DifficultyOrder = { "rookie", "standard", "expert" };

        public static readonly Dictionary<string, FlightSpec> Flight = new Dictionary<string, FlightSpec>
        {
            ["jet"] = new FlightSpec { Duration = .8, Cooldown = 5, Speed = 1.7 },
            ["sail"] = new FlightSpec { Duration = 2.0, Cooldown = 7, Speed = 1.22 },
        };

        // ---- Round construction -------------------------------------------------------------

        public static Round CreateDuelRound(uint seed, string rival = "hunter", string difficulty = "standard",
            string map = "classic", string rule = "classic")
        {
            var profile = Rivals.TryGetValue(rival ?? "", out var p) ? p : Rivals["hunter"];
            var layout = SimMaps.Get(map);
            int side = (seed & 1) != 0 ? 1 : -1;
            double offset = ((int)((seed >> 4) % 5) - 2) * 0.35;
            var player = layout.Duel[0];
            var opponent = layout.Duel[1];
            var playerSpawn = new Spawn(player.X * side + offset, player.Z, player.Dir);
            var rivalSpawn = new Spawn(opponent.X * side - offset, opponent.Z, opponent.Dir);
            if (!SimMaps.SpawnClear(layout, playerSpawn) || !SimMaps.SpawnClear(layout, rivalSpawn))
                throw new InvalidOperationException("Unsafe Duel spawn on " + layout.Id);
            return CreateRound(seed, new[]
            {
                new RiderSpec("YOU", playerSpawn, true, "none"),
                new RiderSpec(profile.Id, rivalSpawn, false, "ai", profile.Key, profile.Color),
            }, difficulty, layout.Id, rule);
        }

        public static Round CreateArenaRound(uint seed, string difficulty = "standard", string map = "classic", string rule = "classic")
        {
            var layout = SimMaps.Get(map);
            string[] names = { "AXIOM", "HELIX", "PULSE" };
            string[] styles = { "hunter", "survivor", "trapper" };
            float[][] colors = { Colors.Orange, Colors.Violet, Colors.Gold };
            uint rotation = seed % 3;
            var spawns = new Spawn[layout.Arena.Length];
            for (int index = 0; index < layout.Arena.Length; index++)
            {
                var spawn = layout.Arena[index];
                double jitter = index == 0 ? 0 : ((int)((seed >> (index * 5)) % 5) - 2) * 0.35;
                var candidate = new Spawn(spawn.X + jitter, spawn.Z, spawn.Dir);
                if (!SimMaps.SpawnClear(layout, candidate)) throw new InvalidOperationException("Unsafe Arena spawn on " + layout.Id);
                spawns[index] = candidate;
            }
            var riders = new List<RiderSpec> { new RiderSpec("YOU", spawns[0], true, "none") };
            for (int index = 0; index < names.Length; index++)
                riders.Add(new RiderSpec(names[index], spawns[index + 1], false, "ai", styles[(index + rotation) % 3], colors[index]));
            return CreateRound(seed, riders, difficulty, layout.Id, rule);
        }

        public static Round CreateOperationRound(string id = "portal", uint seed = 1, string difficulty = "standard")
        {
            var mission = SimOperations.Get(id);
            var map = SimMaps.Get(mission.Map);
            if (!SimMaps.SpawnClear(map, mission.Spawn) || (mission.HasRival && !SimMaps.SpawnClear(map, mission.RivalSpawn)))
                throw new InvalidOperationException("Unsafe operation spawn: " + mission.Id);
            var riders = new List<RiderSpec> { new RiderSpec("YOU", mission.Spawn, true, "none") };
            if (mission.HasRival) riders.Add(new RiderSpec("SENTINEL", mission.RivalSpawn, false, "ai", "hunter", Colors.Gold));
            var round = CreateRound(seed, riders, difficulty, mission.Map);
            round.Operation = new OperationState { Id = mission.Id };
            round.DynamicObstacles = new List<Obstacle>();
            return round;
        }

        public static Round CreateLessonRound(uint seed) =>
            CreateRound(seed, new[] { new RiderSpec("YOU", new Spawn(0, 16, 0), true, "none") });

        public static double NextRandom(Round round)
        {
            uint x = round.RandomState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            round.RandomState = x;
            return x / 4294967296.0;
        }

        static Segment NewSegment(Round round, Rider rider) => new Segment
        {
            Id = round.NextSegmentId++, OwnerId = rider.Id, Rider = rider,
            X1 = rider.X, Z1 = rider.Z, X2 = rider.X, Z2 = rider.Z, Active = true, ClosedAt = -1,
        };

        public static Round CreateRound(uint seed, IList<RiderSpec> riders, string difficulty = "standard",
            string map = "classic", string rule = "classic", bool multiplayer = false)
        {
            var layout = SimMaps.Get(map);
            var round = new Round
            {
                Seed = seed, Multiplayer = multiplayer,
                RandomState = seed != 0 ? seed : 0x6d2b79f5u,
                Speed = SPEED, Map = layout.Id, Limit = layout.Limit,
                Rule = rule == "decay" ? "decay" : "classic",
                TrailLifetime = rule == "decay" ? 10 : double.PositiveInfinity,
                Difficulty = Difficulties.ContainsKey(difficulty ?? "") ? difficulty : "standard",
            };
            foreach (var o in layout.Obstacles) round.Obstacles.Add(o.Copy());
            foreach (var source in riders) if (!source.IsPlayer) round.HasOpponents = true;
            foreach (var source in riders)
            {
                var rider = new Rider
                {
                    Id = source.Id, X = source.X, Z = source.Z, Dir = source.Dir,
                    Color = source.Color ?? (source.IsPlayer ? Colors.Player : Colors.Orange),
                    IsPlayer = source.IsPlayer,
                    Controller = source.Controller ?? (source.IsPlayer ? "none" : "ai"),
                    Style = source.Style ?? (source.IsPlayer ? "player" : "survivor"),
                };
                rider.TacticBias = NextRandom(round) * 2 - 1;
                rider.TacticAt = 5 + NextRandom(round) * 7;
                rider.Alive = true;
                rider.LastTurnAt = double.NegativeInfinity;
                rider.DistanceSinceTurn = double.PositiveInfinity;
                rider.Ammo = ROUND_AMMO;
                rider.AutoDrive = true;
                rider.FlightStartedAt = double.NegativeInfinity;
                rider.Segment = NewSegment(round, rider);
                round.Riders.Add(rider);
                round.Segments.Add(rider.Segment);
            }
            InitArenaRules(round);
            return round;
        }

        // ---- Flight -------------------------------------------------------------------------

        public static bool Airborne(Round round, Rider rider) =>
            rider != null && rider.FlightKind != null && round.Elapsed < rider.FlightUntil;

        static double ReadyAt(Rider rider, string kind) => kind == "jet" ? rider.JetReadyAt : rider.SailReadyAt;

        public static bool RequestFlight(Round round, string riderId, string kind)
        {
            var rider = round.Find(riderId);
            if (kind == null || !Flight.TryGetValue(kind, out var spec) || rider == null || !rider.Alive || round.Outcome != null ||
                rider.FlightKind != null || rider.Form == "foot" || round.Elapsed < ReadyAt(rider, kind)) return false;
            rider.FlightKind = kind;
            rider.FlightStartedAt = round.Elapsed;
            rider.FlightUntil = round.Elapsed + spec.Duration;
            if (kind == "jet") rider.JetReadyAt = round.Elapsed + spec.Cooldown;
            else rider.SailReadyAt = round.Elapsed + spec.Cooldown;
            if (rider.Segment != null)
            {
                rider.Segment.Active = false;
                rider.Segment.ClosedAt = round.Elapsed;
                if (JsMath.Hypot(rider.X - rider.Segment.X1, rider.Z - rider.Segment.Z1) < EPSILON)
                    JsMath.SpliceOne(round.Segments, round.Segments.IndexOf(rider.Segment));
                rider.Segment = null;
            }
            round.Events.Add(new SimEvent { Type = "flight-start", RiderId = riderId, Kind = kind });
            round.ActionCount++;
            return true;
        }

        public static void SettleFlight(Round round, Rider rider)
        {
            if (rider.FlightKind == null || round.Elapsed < rider.FlightUntil) return;
            string kind = rider.FlightKind;
            rider.FlightKind = null;
            rider.FlightUntil = 0;
            rider.IgnoreSegmentId = null;
            rider.DistanceSinceTurn = double.PositiveInfinity;
            var segment = NewSegment(round, rider);
            rider.Segment = segment;
            round.Segments.Add(segment);
            round.Events.Add(new SimEvent { Type = "flight-land", RiderId = rider.Id, Kind = kind });
        }

        public static List<SimEvent> DrainEvents(Round round)
        {
            var events = new List<SimEvent>(round.Events);
            round.Events.Clear();
            return events;
        }

        // ---- Input --------------------------------------------------------------------------

        public static bool QueueTurn(Round round, string riderId, int sign)
        {
            var rider = round.Find(riderId);
            if (rider == null || !rider.Alive || round.Outcome != null || (sign != -1 && sign != 1)) return false;
            double readyAt = rider.LastTurnAt + TURN_COOLDOWN;
            rider.PendingTurn = new PendingTurn
            {
                Sign = sign,
                ExpiresAt = round.Elapsed + Math.Max(TURN_BUFFER, readyAt - round.Elapsed + STEP),
            };
            round.ActionCount++;
            if (readyAt > round.Elapsed + EPSILON)
                round.Events.Add(new SimEvent { Type = "turn-queued", RiderId = riderId, Sign = sign });
            return true;
        }

        public static bool SetAutoDrive(Round round, string riderId, bool enabled)
        {
            var rider = round.Find(riderId);
            if (rider == null) return false;
            rider.AutoDrive = enabled;
            return true;
        }

        public static bool SetThrottle(Round round, string riderId, bool held)
        {
            var rider = round.Find(riderId);
            if (rider == null) return false;
            rider.ThrottleHeld = held;
            return true;
        }

        // ---- Collision ----------------------------------------------------------------------

        public static double? SweptBox(double x, double z, double dx, double dz, double minX, double maxX, double minZ, double maxZ)
        {
            double enter = 0, exit = 1;
            for (int axis = 0; axis < 2; axis++)
            {
                double start = axis == 0 ? x : z, delta = axis == 0 ? dx : dz;
                double min = axis == 0 ? minX : minZ, max = axis == 0 ? maxX : maxZ;
                if (Math.Abs(delta) < EPSILON)
                {
                    if (start < min || start > max) return null;
                    continue;
                }
                double near = Math.Min((min - start) / delta, (max - start) / delta);
                double far = Math.Max((min - start) / delta, (max - start) / delta);
                enter = Math.Max(enter, near);
                exit = Math.Min(exit, far);
                if (enter > exit) return null;
            }
            return exit >= 0 && enter <= 1 ? Math.Max(0, enter) : (double?)null;
        }

        public static double? SweptCircle(double x, double z, double dx, double dz, double cx, double cz, double radius)
        {
            double px = x - cx, pz = z - cz;
            double c = px * px + pz * pz - radius * radius;
            if (c <= 0) return 0;
            double a = dx * dx + dz * dz;
            if (a < EPSILON) return null;
            double b = 2 * (px * dx + pz * dz);
            double discriminant = b * b - 4 * a * c;
            if (discriminant < 0) return null;
            double t = (-b - Math.Sqrt(discriminant)) / (2 * a);
            return t >= 0 && t <= 1 ? t : (double?)null;
        }

        static double? BoundaryHit(double limitValue, double x, double z, double dx, double dz, double radius)
        {
            double limit = limitValue - radius;
            if (Math.Abs(x) > limit || Math.Abs(z) > limit) return 0;
            double? t = null;
            if (dx > EPSILON && x + dx > limit) t = (limit - x) / dx;
            if (dx < -EPSILON && x + dx < -limit) t = (-limit - x) / dx;
            if (dz > EPSILON && z + dz > limit) t = (limit - z) / dz;
            if (dz < -EPSILON && z + dz < -limit) t = (-limit - z) / dz;
            return t;
        }

        public sealed class Hit { public double T; public string Reason, By; public Segment Segment; }

        static Hit StaticHit(Round round, Rider rider, double x, double z, double dx, double dz, double radius = RIDER_RADIUS)
        {
            Hit hit = null;
            var boundaryTime = BoundaryHit(round.Limit, x, z, dx, dz, radius);
            if (boundaryTime.HasValue)
                hit = new Hit
                {
                    T = boundaryTime.Value,
                    Reason = round.Operation != null && round.Operation.Id == "collapse" && round.Limit < SimMaps.Get(round.Map).Limit
                        ? "collapse" : "boundary",
                };
            foreach (var obstacle in round.Obstacles)
            {
                var t = obstacle.Circle
                    ? SweptCircle(x, z, dx, dz, obstacle.X, obstacle.Z, obstacle.Radius + radius)
                    : SweptBox(x, z, dx, dz, obstacle.MinX - radius, obstacle.MaxX + radius, obstacle.MinZ - radius, obstacle.MaxZ + radius);
                if (t.HasValue && (hit == null || t.Value < hit.T)) hit = new Hit { T = t.Value, Reason = "obstacle", By = obstacle.Id };
            }
            foreach (var obstacle in round.DynamicObstacles)
            {
                if (obstacle.Kind == "gap" && Airborne(round, rider) && rider.FlightKind == obstacle.FlightKind) continue;
                var t = SweptBox(x, z, dx, dz, obstacle.MinX - radius, obstacle.MaxX + radius, obstacle.MinZ - radius, obstacle.MaxZ + radius);
                if (t.HasValue && (hit == null || t.Value < hit.T))
                    hit = new Hit { T = t.Value, Reason = obstacle.Kind == "gap" ? "gap" : "hazard", By = obstacle.Id };
            }
            foreach (var segment in round.Segments)
            {
                if (Airborne(round, rider)) break;
                if (rider != null && segment == rider.Segment) continue;
                if (rider != null && rider.IgnoreSegmentId.HasValue && segment.Id == rider.IgnoreSegmentId.Value &&
                    rider.DistanceSinceTurn < CORNER_CLEARANCE) continue;
                double minX = Math.Min(segment.X1, segment.X2) - TRAIL_HALF_WIDTH - radius;
                double maxX = Math.Max(segment.X1, segment.X2) + TRAIL_HALF_WIDTH + radius;
                double minZ = Math.Min(segment.Z1, segment.Z2) - TRAIL_HALF_WIDTH - radius;
                double maxZ = Math.Max(segment.Z1, segment.Z2) + TRAIL_HALF_WIDTH + radius;
                var t = SweptBox(x, z, dx, dz, minX, maxX, minZ, maxZ);
                if (t.HasValue && (hit == null || t.Value < hit.T))
                    hit = new Hit { T = t.Value, Reason = "trail", By = segment.OwnerId, Segment = segment };
            }
            return hit;
        }

        // ---- Weapons ------------------------------------------------------------------------

        static bool ShootBullet(Round round, Rider rider)
        {
            if (!rider.Alive || rider.Form == "foot" || round.Outcome != null ||
                (rider.Ammo <= 0 && !rider.RocketReady) || round.Elapsed + EPSILON < rider.NextShotAt) return false;
            int[] v = Dir[rider.Dir];
            var bullet = new Bullet { OwnerId = rider.Id, X = rider.X + v[0] * .8, Z = rider.Z + v[1] * .8, Vx = v[0], Vz = v[1], Rocket = rider.RocketReady };
            round.Bullets.Add(bullet);
            if (rider.RocketReady) rider.RocketReady = false;
            else rider.Ammo--;
            rider.NextShotAt = round.Elapsed + FIRE_INTERVAL;
            if (rider.Ammo == 0) rider.FireHeld = false;
            round.Events.Add(new SimEvent { Type = "bullet-fire", RiderId = rider.Id, Ammo = rider.Ammo, Rocket = bullet.Rocket });
            round.ActionCount++;
            return true;
        }

        public static bool SetFire(Round round, string riderId, bool held)
        {
            var rider = round.Find(riderId);
            if (rider == null || !rider.Alive || round.Outcome != null) return false;
            rider.FireHeld = held && (rider.Ammo > 0 || rider.RocketReady);
            bool fired = rider.FireHeld && ShootBullet(round, rider);
            return rider.FireHeld || fired || !held;
        }

        public static void CutTrail(Round round, Segment segment, double x, double z)
        {
            double dx = segment.X2 - segment.X1, dz = segment.Z2 - segment.Z1;
            double length = JsMath.Hypot(dx, dz);
            if (length < EPSILON) return;
            double along = Math.Max(0, Math.Min(length, ((x - segment.X1) * dx + (z - segment.Z1) * dz) / length));
            double before = Math.Max(0, along - BULLET_GAP);
            double after = Math.Min(length, along + BULLET_GAP);
            double firstEndX = segment.X1 + dx * before / length, firstEndZ = segment.Z1 + dz * before / length;
            double lastStartX = segment.X1 + dx * after / length, lastStartZ = segment.Z1 + dz * after / length;
            if (segment.Active)
            {
                if (before > EPSILON)
                {
                    var piece = segment.Copy();
                    piece.Id = round.NextSegmentId++; piece.X2 = firstEndX; piece.Z2 = firstEndZ;
                    piece.Active = false; piece.ClosedAt = round.Elapsed;
                    round.Segments.Add(piece);
                }
                segment.X1 = lastStartX; segment.Z1 = lastStartZ;
            }
            else
            {
                if (after < length - EPSILON)
                {
                    var piece = segment.Copy();
                    piece.Id = round.NextSegmentId++; piece.X1 = lastStartX; piece.Z1 = lastStartZ;
                    round.Segments.Add(piece);
                }
                if (before > EPSILON) { segment.X2 = firstEndX; segment.Z2 = firstEndZ; }
                else JsMath.SpliceOne(round.Segments, round.Segments.IndexOf(segment));
            }
        }

        static bool MoveBullet(Round round, Bullet bullet)
        {
            double travel = Math.Min(BULLET_SPEED * STEP, BULLET_RANGE - bullet.Traveled);
            if (travel <= 0) return false;
            double dx = bullet.Vx * travel, dz = bullet.Vz * travel;
            var hit = StaticHit(round, null, bullet.X, bullet.Z, dx, dz, .12);
            double? riderHit = null;
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive || rider.Id == bullet.OwnerId) continue;
                var t = SweptBox(bullet.X, bullet.Z, dx, dz,
                    rider.X - RIDER_RADIUS - .12, rider.X + RIDER_RADIUS + .12,
                    rider.Z - RIDER_RADIUS - .12, rider.Z + RIDER_RADIUS + .12);
                if (t.HasValue && (riderHit == null || t.Value < riderHit.Value)) riderHit = t;
            }
            if (riderHit.HasValue && (hit == null || riderHit.Value < hit.T))
            {
                if (bullet.Rocket) ExplodeRocket(round, bullet, bullet.X + dx * riderHit.Value, bullet.Z + dz * riderHit.Value);
                else round.Events.Add(new SimEvent { Type = "bullet-blocked", RiderId = bullet.OwnerId, Reason = "rider" });
                return false;
            }
            if (hit != null)
            {
                if (bullet.Rocket)
                {
                    ExplodeRocket(round, bullet, bullet.X + dx * hit.T, bullet.Z + dz * hit.T);
                    return false;
                }
                if (hit.Segment != null)
                {
                    double x = bullet.X + dx * hit.T, z = bullet.Z + dz * hit.T;
                    CutTrail(round, hit.Segment, x, z);
                    round.Events.Add(new SimEvent { Type = "bullet-cut", RiderId = bullet.OwnerId, By = hit.Segment.OwnerId, X = x, Z = z });
                }
                else round.Events.Add(new SimEvent { Type = "bullet-blocked", RiderId = bullet.OwnerId, Reason = hit.Reason });
                return false;
            }
            bullet.X += dx; bullet.Z += dz; bullet.Traveled += travel;
            if (bullet.Rocket && bullet.Traveled >= BULLET_RANGE - EPSILON)
            {
                ExplodeRocket(round, bullet, bullet.X, bullet.Z);
                return false;
            }
            return bullet.Traveled < BULLET_RANGE - EPSILON;
        }

        static void ExplodeRocket(Round round, Bullet bullet, double x, double z)
        {
            foreach (var segment in round.Segments.ToArray())
            {
                double px = Math.Max(Math.Min(x, Math.Max(segment.X1, segment.X2)), Math.Min(segment.X1, segment.X2));
                double pz = Math.Max(Math.Min(z, Math.Max(segment.Z1, segment.Z2)), Math.Min(segment.Z1, segment.Z2));
                if (JsMath.Hypot(px - x, pz - z) <= 3) CutTrail(round, segment, px, pz);
            }
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive || rider.Id == bullet.OwnerId || JsMath.Hypot(rider.X - x, rider.Z - z) > 3.2) continue;
                rider.Alive = false;
                if (rider.Segment != null)
                {
                    rider.Segment.Active = false;
                    rider.Segment.ClosedAt = round.Elapsed;
                }
                round.Events.Add(new SimEvent { Type = "crash", RiderId = rider.Id, Reason = "rocket", By = bullet.OwnerId, X = rider.X, Z = rider.Z });
                if (!round.Multiplayer && !rider.IsPlayer)
                {
                    round.Score += 250;
                    round.ScoreBreakdown.Derez += 250;
                }
            }
            round.Events.Add(new SimEvent { Type = "rocket-explode", RiderId = bullet.OwnerId, X = x, Z = z });
        }

        // ---- AI -----------------------------------------------------------------------------

        sealed class Proposal { public Rider Rider; public double Dx, Dz; public Hit Hit; }

        static double? MovingRiderHit(Proposal a, Proposal b)
        {
            double px = a.Rider.X - b.Rider.X, pz = a.Rider.Z - b.Rider.Z;
            double vx = a.Dx - b.Dx, vz = a.Dz - b.Dz;
            double radius = RIDER_RADIUS * 2;
            double c = px * px + pz * pz - radius * radius;
            if (c <= 0) return 0;
            double aa = vx * vx + vz * vz;
            if (aa < EPSILON) return null;
            double bb = 2 * (px * vx + pz * vz);
            double discriminant = bb * bb - 4 * aa * c;
            if (discriminant < 0) return null;
            double t = (-bb - Math.Sqrt(discriminant)) / (2 * aa);
            return t >= 0 && t <= 1 ? t : (double?)null;
        }

        static double FreeDistance(Round round, Rider rider, int dir, double maxDistance = 10)
        {
            int[] v = Dir[dir];
            var hit = StaticHit(round, rider, rider.X, rider.Z, v[0] * maxDistance, v[1] * maxDistance);
            double distance = hit != null ? hit.T * maxDistance : maxDistance;
            foreach (var other in round.Riders)
            {
                if (other == rider || !other.Alive) continue;
                double forward = (other.X - rider.X) * v[0] + (other.Z - rider.Z) * v[1];
                double side = Math.Abs((other.X - rider.X) * v[1] - (other.Z - rider.Z) * v[0]);
                if (forward > 0 && side < RIDER_RADIUS * 2) distance = Math.Min(distance, Math.Max(0, forward - RIDER_RADIUS * 2));
            }
            return distance;
        }

        sealed class Choice { public int Sign, Direction; public double Score, Distance; }

        static Choice RouteScore(Round round, Rider rider, int direction, double horizon, int sign)
        {
            double distance = FreeDistance(round, rider, direction, horizon);
            int[] v = Dir[direction];
            double probe = Math.Min(distance, 4);
            var future = rider.Copy();
            future.X = rider.X + v[0] * probe;
            future.Z = rider.Z + v[1] * probe;
            double left = FreeDistance(round, future, (direction + 3) % 4, 6);
            double right = FreeDistance(round, future, (direction + 1) % 4, 6);
            double escape = Math.Max(left, right);
            Rider opponent = null;
            foreach (var other in round.Riders) if (other.IsPlayer && other.Alive) { opponent = other; break; }
            double targetDistance = opponent != null ? JsMath.Hypot(future.X - opponent.X, future.Z - opponent.Z) : 20;
            int crowded = 0;
            foreach (var segment in round.Segments)
            {
                if (segment.OwnerId == rider.Id && segment == rider.Segment) continue;
                double cx = (segment.X1 + segment.X2) * 0.5, cz = (segment.Z1 + segment.Z2) * 0.5;
                if (JsMath.Hypot(future.X - cx, future.Z - cz) < 5) crowded++;
            }
            double score = distance + escape * 0.34 - crowded * 0.35 - (sign != 0 ? 0.55 : 0);
            bool playerTurnedRecently = opponent != null && round.Elapsed - opponent.LastTurnAt < 1.5;
            if (rider.Style == "hunter")
            {
                score -= targetDistance * (round.Elapsed > rider.TacticAt ? 0.35 : 0.24);
                if (playerTurnedRecently && direction == opponent.Dir && targetDistance < 12) score += 1.5;
            }
            else if (rider.Style == "trapper")
            {
                score -= targetDistance * 0.16;
                if (opponent != null && direction % 2 != opponent.Dir % 2 && targetDistance < 14) score += playerTurnedRecently ? 3.4 : 2.2;
            }
            else score += targetDistance * 0.23;
            score += rider.TacticBias * sign * (round.Elapsed > rider.TacticAt ? 1.15 : 0.55);
            return new Choice { Sign = sign, Direction = direction, Score = score, Distance = distance };
        }

        static void DecideAI(Round round, Rider rider)
        {
            if (round.Elapsed + EPSILON < rider.NextDecisionAt || rider.PendingTurn != null) return;
            var level = Difficulties[round.Difficulty];
            rider.NextDecisionAt = round.Elapsed + level.Interval + NextRandom(round) * 0.05;
            int[] v = Dir[rider.Dir];
            var forwardHit = StaticHit(round, rider, rider.X, rider.Z, v[0] * 10, v[1] * 10);
            double front = forwardHit != null ? forwardHit.T * 10 : 10;
            if (forwardHit != null && forwardHit.Reason == "trail" && front > 3.7 && front < 10 &&
                rider.Ammo > 0 && SetFire(round, rider.Id, true)) return;
            SetFire(round, rider.Id, false);
            double horizon = level.Horizon + (round.Elapsed > rider.TacticAt ? 2 : 0);
            var forward = RouteScore(round, rider, rider.Dir, horizon, 0);
            var left = RouteScore(round, rider, (rider.Dir + 3) % 4, horizon, -1);
            var right = RouteScore(round, rider, (rider.Dir + 1) % 4, horizon, 1);
            double safeDistance = round.Speed * (level.Interval + 0.18);
            var safeTurns = new List<Choice>();
            if (left.Distance > safeDistance) safeTurns.Add(left);
            if (right.Distance > safeDistance) safeTurns.Add(right);
            if (forwardHit != null && forwardHit.Reason == "trail" && front > 1.1 && front < 3.6 && safeTurns.Count == 0 &&
                RequestFlight(round, rider.Id, round.Elapsed >= rider.JetReadyAt ? "jet" : "sail")) return;
            // Stable descending sort: the earlier choice (left) wins ties, as Array.prototype.sort does.
            Choice best = null;
            foreach (var choice in safeTurns) if (best == null || choice.Score > best.Score) best = choice;
            if (best != null && (forward.Distance < safeDistance || best.Score > forward.Score + level.Margin))
            {
                QueueTurn(round, rider.Id, best.Sign);
                round.Events.Add(new SimEvent { Type = "ai-intent", RiderId = rider.Id, Style = rider.Style, Sign = best.Sign });
            }
        }

        static void ApplyTurn(Round round, Rider rider, int sign)
        {
            double priorFree = FreeDistance(round, rider, rider.Dir, 3);
            if (rider.Segment != null && JsMath.Hypot(rider.X - rider.Segment.X1, rider.Z - rider.Segment.Z1) < EPSILON)
                JsMath.SpliceOne(round.Segments, round.Segments.IndexOf(rider.Segment));
            else if (rider.Segment != null)
            {
                rider.Segment.Active = false;
                rider.Segment.ClosedAt = round.Elapsed;
                rider.IgnoreSegmentId = rider.Segment.Id;
                rider.DistanceSinceTurn = 0;
            }
            rider.Dir = (rider.Dir + sign + 4) % 4;
            if (!Airborne(round, rider))
            {
                rider.Segment = NewSegment(round, rider);
                round.Segments.Add(rider.Segment);
            }
            rider.LastTurnAt = round.Elapsed;
            bool nearMiss = priorFree < 2.4;
            round.Events.Add(new SimEvent { Type = "turn", RiderId = rider.Id, Sign = sign, NearMiss = nearMiss });
            if (rider.IsPlayer && nearMiss && !round.NearMissAwarded)
            {
                round.NearMissAwarded = true;
                round.Score += 75;
                round.ScoreBreakdown.CloseCall = 75;
                round.Events.Add(new SimEvent { Type = "skill", Kind = "close-call", Points = 75 });
            }
        }

        // ---- Arena rules: pickups and shrink ----------------------------------------------

        static bool PlacePickup(Round round, Pickup pickup)
        {
            var riders = round.Riders.FindAll(r => r.Alive);
            if (riders.Count == 0) return false;
            var target = riders[(int)Math.Floor(NextRandom(round) * riders.Count)];
            int forwardX = Dir[target.Dir][0], forwardZ = Dir[target.Dir][1];
            for (int attempt = 0; attempt < 100; attempt++)
            {
                double x, z;
                if (attempt < 70 || round.Operation != null)
                {
                    double forward = 5 + NextRandom(round) * 10;
                    double side = (NextRandom(round) * 2 - 1) * 9;
                    x = JsMath.Round(target.X + forwardX * forward - forwardZ * side);
                    z = JsMath.Round(target.Z + forwardZ * forward + forwardX * side);
                }
                else
                {
                    double range = Math.Min(round.Limit - 3, 30);
                    x = JsMath.Round((NextRandom(round) * 2 - 1) * range);
                    z = JsMath.Round((NextRandom(round) * 2 - 1) * range);
                }
                if (Math.Max(Math.Abs(x), Math.Abs(z)) >= round.Limit - 2) continue;
                if (round.HasLastPickupPoint && JsMath.Hypot(x - round.LastPickupX, z - round.LastPickupZ) < 6) continue;
                bool blocked = false;
                foreach (var segment in round.Segments)
                    if (x >= Math.Min(segment.X1, segment.X2) - 1.2 && x <= Math.Max(segment.X1, segment.X2) + 1.2 &&
                        z >= Math.Min(segment.Z1, segment.Z2) - 1.2 && z <= Math.Max(segment.Z1, segment.Z2) + 1.2) { blocked = true; break; }
                if (blocked) continue;
                foreach (var obstacle in round.Obstacles) if (SimMaps.Contains(obstacle, x, z, 1.2)) { blocked = true; break; }
                if (!blocked) foreach (var obstacle in round.DynamicObstacles) if (SimMaps.Contains(obstacle, x, z, 1.2)) { blocked = true; break; }
                if (blocked) continue;
                foreach (var rider in riders) if (JsMath.Hypot(rider.X - x, rider.Z - z) < 4) { blocked = true; break; }
                if (blocked) continue;
                foreach (var other in round.Pickups) if (JsMath.Hypot(other.X - x, other.Z - z) < 5) { blocked = true; break; }
                if (blocked) continue;
                pickup.X = x;
                pickup.Z = z;
                pickup.Active = true;
                pickup.ExpiresAt = round.Elapsed + PICKUP_LIFETIME;
                round.HasLastPickupPoint = true;
                round.LastPickupX = x; round.LastPickupZ = z;
                return true;
            }
            return false;
        }

        static void InitArenaRules(Round round)
        {
            round.BaseLimit = round.Limit;
            round.Pickups = new List<Pickup>();
            round.PickupTypes = (string[])PickupTypeOrder.Clone();
            for (int index = round.PickupTypes.Length - 1; index > 0; index--)
            {
                int other = (int)Math.Floor(NextRandom(round) * (index + 1));
                string swap = round.PickupTypes[index];
                round.PickupTypes[index] = round.PickupTypes[other];
                round.PickupTypes[other] = swap;
            }
            round.NextPickupAt = FIRST_PICKUP_AT;
            round.NextPickupId = 1;
            round.PickupSerial = 0;
            round.HasLastPickupPoint = false;
            foreach (var rider in round.Riders)
            {
                rider.NitroUntil = 0;
                rider.RocketReady = false;
            }
        }

        public static void UpdateArenaRules(Round round)
        {
            if (round.Outcome != null) return;
            double stage = Math.Floor(Math.Max(0, round.Elapsed - SHRINK_START) / SHRINK_INTERVAL) + 1;
            if (round.Elapsed >= SHRINK_START)
            {
                double next = Math.Max(4, round.BaseLimit - stage * 5);
                if (next < round.Limit)
                {
                    round.Limit = next;
                    round.Events.Add(new SimEvent { Type = "arena-shrink", Limit = next });
                }
            }
            round.Pickups = round.Pickups.FindAll(pickup => pickup.Active && round.Elapsed < pickup.ExpiresAt &&
                Math.Max(Math.Abs(pickup.X), Math.Abs(pickup.Z)) < round.Limit - 1);
            if (round.Elapsed >= round.NextPickupAt)
            {
                string type = round.PickupTypes[round.PickupSerial % round.PickupTypes.Length];
                var pickup = new Pickup { Id = round.NextPickupId++, Type = type };
                if (PlacePickup(round, pickup))
                {
                    round.Pickups.Add(pickup);
                    round.PickupSerial++;
                    round.NextPickupAt = round.Elapsed + PICKUP_INTERVAL;
                    round.Events.Add(new SimEvent { Type = "pickup-spawn", Pickup = type, X = pickup.X, Z = pickup.Z });
                }
                else round.NextPickupAt = round.Elapsed + 2;
            }
            foreach (var pickup in round.Pickups)
            {
                foreach (var rider in round.Riders)
                {
                    if (!rider.Alive || JsMath.Hypot(rider.X - pickup.X, rider.Z - pickup.Z) > .85) continue;
                    if (pickup.Type == "ammo") rider.Ammo = Math.Min(48, rider.Ammo + 12);
                    if (pickup.Type == "nitro") rider.NitroUntil = round.Elapsed + 7;
                    if (pickup.Type == "rocket") rider.RocketReady = true;
                    pickup.Active = false;
                    round.Events.Add(new SimEvent { Type = "pickup", RiderId = rider.Id, Pickup = pickup.Type });
                    break;
                }
            }
        }

        // ---- Step ---------------------------------------------------------------------------

        public static Round StepRound(Round round)
        {
            if (round.Outcome != null) return round;
            SimOperations.Prepare(round);
            UpdateArenaRules(round);
            if (round.Rule == "decay")
                round.Segments = round.Segments.FindAll(segment => segment.Active || round.Elapsed < segment.ClosedAt + round.TrailLifetime);
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive) continue;
                SettleFlight(round, rider);
                if (rider.Controller == "ai") DecideAI(round, rider);
                var pending = rider.PendingTurn;
                if (pending == null) continue;
                if (pending.ExpiresAt + EPSILON < round.Elapsed) rider.PendingTurn = null;
                else if (round.Elapsed - rider.LastTurnAt + EPSILON >= TURN_COOLDOWN)
                {
                    rider.PendingTurn = null;
                    ApplyTurn(round, rider, pending.Sign);
                }
            }

            foreach (var rider in round.Riders) if (rider.FireHeld) ShootBullet(round, rider);
            var bullets = new List<Bullet>();
            foreach (var bullet in round.Bullets) if (MoveBullet(round, bullet)) bullets.Add(bullet);
            round.Bullets = bullets;

            var proposals = new List<Proposal>();
            foreach (var rider in round.Riders)
            {
                if (!rider.Alive) continue;
                double distance = (rider.AutoDrive || rider.ThrottleHeld ? 1 : 0) * round.Speed *
                    (round.Elapsed < rider.NitroUntil ? 1.5 : 1) *
                    (Airborne(round, rider) ? Flight[rider.FlightKind].Speed : 1) * STEP;
                int[] v = Dir[rider.Dir];
                double dx = v[0] * distance, dz = v[1] * distance;
                proposals.Add(new Proposal { Rider = rider, Dx = dx, Dz = dz, Hit = StaticHit(round, rider, rider.X, rider.Z, dx, dz) });
            }

            var encounters = new List<(int i, int j, double t)>();
            for (int i = 0; i < proposals.Count; i++)
                for (int j = i + 1; j < proposals.Count; j++)
                {
                    var t = MovingRiderHit(proposals[i], proposals[j]);
                    if (t.HasValue && !Airborne(round, proposals[i].Rider) && !Airborne(round, proposals[j].Rider))
                        encounters.Add((i, j, t.Value));
                }
            StableSortByT(encounters);
            foreach (var (i, j, t) in encounters)
            {
                var a = proposals[i]; var b = proposals[j];
                if ((a.Hit == null || t <= a.Hit.T + EPSILON) && (b.Hit == null || t <= b.Hit.T + EPSILON))
                {
                    a.Hit = new Hit { T = t, Reason = "rider", By = b.Rider.Id };
                    b.Hit = new Hit { T = t, Reason = "rider", By = a.Rider.Id };
                }
            }

            foreach (var proposal in proposals)
            {
                var rider = proposal.Rider;
                var hit = proposal.Hit;
                double fraction = hit != null ? Math.Max(0, Math.Min(1, hit.T)) : 1;
                rider.X += proposal.Dx * fraction;
                rider.Z += proposal.Dz * fraction;
                if (rider.Segment != null) { rider.Segment.X2 = rider.X; rider.Segment.Z2 = rider.Z; }
                rider.DistanceSinceTurn += JsMath.Hypot(proposal.Dx, proposal.Dz) * fraction;
                if (hit == null) continue;
                rider.Alive = false;
                if (rider.Segment != null)
                {
                    rider.Segment.Active = false;
                    rider.Segment.ClosedAt = round.Elapsed + STEP * fraction;
                }
                round.Events.Add(new SimEvent { Type = "crash", RiderId = rider.Id, Reason = hit.Reason, By = hit.By, X = rider.X, Z = rider.Z });
                if (!round.Multiplayer && !rider.IsPlayer)
                {
                    round.Score += 250;
                    round.ScoreBreakdown.Derez += 250;
                    if (hit.Reason == "trail" && hit.By == "YOU" && !round.TrapAwarded)
                    {
                        round.TrapAwarded = true;
                        round.Score += 150;
                        round.ScoreBreakdown.Trap = 150;
                        round.Events.Add(new SimEvent { Type = "skill", Kind = "clean-trap", Points = 150 });
                    }
                }
            }

            round.Tick++;
            round.Elapsed = round.Tick * STEP;
            var player = round.Player;
            if (round.Multiplayer)
            {
                var alive = round.Riders.FindAll(r => r.Alive);
                if (alive.Count <= 1)
                    round.Outcome = new Outcome { Kind = alive.Count > 0 ? "won" : "draw", WinnerId = alive.Count > 0 ? alive[0].Id : null };
            }
            else if (player != null && !player.Alive)
            {
                SimEvent crash = null;
                for (int i = round.Events.Count - 1; i >= 0; i--)
                    if (round.Events[i].Type == "crash" && round.Events[i].RiderId == player.Id) { crash = round.Events[i]; break; }
                round.Outcome = new Outcome { Kind = "lost", Reason = crash?.Reason ?? "unknown", By = crash?.By };
            }
            else if (round.Operation != null) SimOperations.UpdateProgress(round, player);
            else if (round.HasOpponents && round.Riders.TrueForAll(r => r.IsPlayer || !r.Alive))
            {
                round.Score += 1000;
                round.ScoreBreakdown.Win = 1000;
                round.Outcome = new Outcome { Kind = "won" };
            }
            if (round.Outcome == null && round.Elapsed >= ROUND_END)
                round.Outcome = round.Operation != null
                    ? new Outcome { Kind = "lost", Reason = "timeout" }
                    : new Outcome { Kind = "draw", Reason = "timeout" };
            if (round.Outcome != null)
                round.Events.Add(new SimEvent { Type = "round-end", Outcome = round.Outcome, Score = round.Score });
            return round;
        }

        static void StableSortByT(List<(int i, int j, double t)> items)
        {
            for (int k = 1; k < items.Count; k++)
            {
                var item = items[k];
                int m = k - 1;
                while (m >= 0 && items[m].t > item.t) { items[m + 1] = items[m]; m--; }
                items[m + 1] = item;
            }
        }

        internal static void CompleteOperation(Round round, int bonus)
        {
            round.Score += bonus;
            round.ScoreBreakdown.Win += bonus;
            round.Outcome = new Outcome { Kind = "won" };
        }
    }
}
