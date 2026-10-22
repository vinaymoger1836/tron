using System;
using System.Collections.Generic;

namespace GridRun.Sim
{
    // Port of dist/maps.js. Coordinates are the browser's: dir 0 = -z (north), 1 = +x, 2 = +z, 3 = -x.
    // Presentation flips z (Unity z = -sim z) so relative turns read correctly on screen.
    public static class JsMath
    {
        // Matches V8's Math.hypot (Kahan-compensated, scaled) so results are bit-identical with the browser.
        public static double Hypot(double a, double b)
        {
            double x = Math.Abs(a), y = Math.Abs(b);
            if (double.IsNaN(x) || double.IsNaN(y))
                return double.IsInfinity(x) || double.IsInfinity(y) ? double.PositiveInfinity : double.NaN;
            double max = x > y ? x : y;
            if (double.IsPositiveInfinity(max)) return double.PositiveInfinity;
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            double n = x / max;
            double summand = n * n - compensation;
            double preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
            n = y / max;
            summand = n * n - compensation;
            preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
            return Math.Sqrt(sum) * max;
        }

        // JS Math.round: halves round toward +Infinity.
        public static double Round(double value)
        {
            double floor = Math.Floor(value);
            return value - floor >= .5 ? floor + 1 : floor;
        }

        public static uint Imul(uint a, uint b) => unchecked(a * b);

        // Array.prototype.splice(index, 1) semantics, including index -1 removing the last item.
        public static void SpliceOne<T>(List<T> list, int index)
        {
            if (index < 0) index += list.Count;
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
        }
    }

    public sealed class Obstacle
    {
        public string Id;
        public bool Circle;
        public double X, Z, Radius;
        public double MinX, MaxX, MinZ, MaxZ;
        public string Kind;        // "gap" for traversal gaps, otherwise null
        public string FlightKind;  // flight required to cross a gap

        public Obstacle Copy() => (Obstacle)MemberwiseClone();
        public static Obstacle Disc(string id, double x, double z, double radius) =>
            new Obstacle { Id = id, Circle = true, X = x, Z = z, Radius = radius };
        public static Obstacle Box(string id, double minX, double maxX, double minZ, double maxZ) =>
            new Obstacle { Id = id, MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
    }

    public struct Spawn
    {
        public double X, Z;
        public int Dir;
        public Spawn(double x, double z, int dir) { X = x; Z = z; Dir = dir; }
    }

    public sealed class MapDef
    {
        public string Id, Name, Cue;
        public double Limit;
        public float[] Rail, Floor;
        public Obstacle[] Obstacles;
        public Spawn[] Duel, Arena;
    }

    public static class SimMaps
    {
        public static readonly string[] Order = { "classic", "solar", "vault" };

        public static readonly Dictionary<string, MapDef> All = new Dictionary<string, MapDef>
        {
            ["classic"] = new MapDef
            {
                Id = "classic", Name = "Classic Grid", Limit = 52,
                Cue = "Open square. Read every wall and keep an exit in reserve.",
                Rail = new[] { .06f, .66f, .82f }, Floor = new[] { .008f, .026f, .045f },
                Obstacles = new Obstacle[0],
                Duel = new[] { new Spawn(-5, 17, 0), new Spawn(5, -17, 2) },
                Arena = new[] { new Spawn(0, 16, 0), new Spawn(-17, -16, 1), new Spawn(17, -14, 3), new Spawn(16, 14, 0) },
            },
            ["solar"] = new MapDef
            {
                Id = "solar", Name = "Solar Circuit", Limit = 56,
                Cue = "Long central sight lines; amber pylons reward early lane changes.",
                Rail = new[] { 1f, .54f, .17f }, Floor = new[] { .035f, .022f, .012f },
                Obstacles = new[]
                {
                    Obstacle.Disc("WEST REACTOR", -14, 0, 3.1), Obstacle.Disc("EAST REACTOR", 14, 0, 3.1),
                    Obstacle.Disc("NORTH SPARK", 9, -12, 1.8), Obstacle.Disc("SOUTH SPARK", -9, 12, 1.8),
                },
                Duel = new[] { new Spawn(-5, 19, 0), new Spawn(5, -19, 2) },
                Arena = new[] { new Spawn(0, 20, 0), new Spawn(-20, -19, 1), new Spawn(20, -19, 3), new Spawn(20, 19, 0) },
            },
            ["vault"] = new MapDef
            {
                Id = "vault", Name = "Data Vault", Limit = 54,
                Cue = "Round violet nodes split the center. Plan your turn before each narrow passage.",
                Rail = new[] { .67f, .34f, 1f }, Floor = new[] { .018f, .014f, .044f },
                Obstacles = new[]
                {
                    Obstacle.Disc("NORTHWEST NODE", -11, -9, 2.8), Obstacle.Disc("SOUTHEAST NODE", 11, 9, 2.8),
                    Obstacle.Disc("NORTHEAST NODE", 11, -9, 2.8), Obstacle.Disc("SOUTHWEST NODE", -11, 9, 2.8),
                    Obstacle.Disc("VAULT CORE", 0, 0, 3.3),
                },
                Duel = new[] { new Spawn(-3, 18, 0), new Spawn(3, -18, 2) },
                Arena = new[] { new Spawn(0, 19, 0), new Spawn(-18, -18, 1), new Spawn(18, -18, 3), new Spawn(18, 18, 0) },
            },
        };

        public static MapDef Get(string id) => id != null && All.TryGetValue(id, out var map) ? map : All["classic"];

        public static bool Contains(Obstacle obstacle, double x, double z, double radius = 0) =>
            obstacle.Circle
                ? JsMath.Hypot(x - obstacle.X, z - obstacle.Z) <= obstacle.Radius + radius
                : x >= obstacle.MinX - radius && x <= obstacle.MaxX + radius &&
                  z >= obstacle.MinZ - radius && z <= obstacle.MaxZ + radius;

        public static bool SpawnClear(MapDef map, Spawn spawn, double distance = 5)
        {
            const double radius = .3;
            if (spawn.Dir < 0 || spawn.Dir > 3) return false;
            int[] direction = GridRules.Dir[spawn.Dir];
            for (int step = 0; step <= 20; step++)
            {
                double x = spawn.X + direction[0] * distance * step / 20;
                double z = spawn.Z + direction[1] * distance * step / 20;
                if (Math.Abs(x) + radius >= map.Limit || Math.Abs(z) + radius >= map.Limit) return false;
                foreach (var o in map.Obstacles) if (Contains(o, x, z, radius)) return false;
            }
            return true;
        }
    }
}
