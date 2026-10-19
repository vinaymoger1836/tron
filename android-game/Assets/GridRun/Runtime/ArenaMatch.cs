using System;
using System.Collections.Generic;

namespace GridRun
{
    // Fixed-step Arena rules. Coordinates and collision decisions are independent of Unity rendering.
    public sealed class ArenaMatch
    {
        public const float InitialLimit = 48f;
        public const float Speed = 12f;
        public const float ShrinkStartsAt = 180f;
        public const int WinningScore = 3;
        private const float TurnDelay = .12f;
        private const float WallMargin = .5f;
        private const float ShrinkRate = .6f;
        private readonly Random random = new Random(4107);
        public readonly Rider Player = new Rider();
        public readonly Rider Opponent = new Rider();
        public readonly List<Trail> Trails = new List<Trail>();
        public int PlayerScore { get; private set; }
        public int OpponentScore { get; private set; }
        public int Heat { get; private set; }
        public float Elapsed { get; private set; }
        public float Limit { get; private set; }
        public bool HeatEnded { get; private set; }
        public bool MatchEnded => PlayerScore >= WinningScore || OpponentScore >= WinningScore;
        public string Result { get; private set; }
        public string PlayerCrash { get; private set; }
        public string OpponentCrash { get; private set; }
        private float aiThink;

        public sealed class Rider
        {
            public float X, Z;
            public int Heading;
            public bool Crashed;
            public float TurnCooldown;
            public bool Moving;
        }

        public struct Trail
        {
            public int Owner;
            public float X0, Z0, X1, Z1;
            public int Heading;
            public Trail(int owner, float x0, float z0, float x1, float z1, int heading)
            { Owner = owner; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Heading = heading; }
        }

        public ArenaMatch() { NewMatch(); }

        public void NewMatch()
        {
            PlayerScore = OpponentScore = 0;
            Heat = 0;
            BeginHeat();
        }

        public void BeginHeat()
        {
            if (MatchEnded) return;
            Heat++;
            Elapsed = 0;
            Limit = InitialLimit;
            HeatEnded = false;
            Result = PlayerCrash = OpponentCrash = "";
            Trails.Clear();
            SetRider(Player, 0, -23, 0);
            SetRider(Opponent, 12, 23, 2);
            aiThink = .3f;
        }

        private static void SetRider(Rider rider, float x, float z, int heading)
        {
            rider.X = x; rider.Z = z; rider.Heading = heading;
            rider.Crashed = false; rider.TurnCooldown = 0; rider.Moving = true;
        }

        public bool TurnPlayer(int direction) => Turn(Player, direction);
        private static bool Turn(Rider rider, int direction)
        {
            if (rider.Crashed || rider.TurnCooldown > 0 || direction == 0) return false;
            rider.Heading = (rider.Heading + (direction < 0 ? 3 : 1)) % 4;
            rider.TurnCooldown = TurnDelay;
            return true;
        }

        public void Step(float dt, bool playerForward)
        {
            if (dt <= 0 || HeatEnded) return;
            Elapsed += dt;
            Limit = Math.Max(12f, InitialLimit - Math.Max(0, Elapsed - ShrinkStartsAt) * ShrinkRate);
            Player.TurnCooldown = Math.Max(0, Player.TurnCooldown - dt);
            Opponent.TurnCooldown = Math.Max(0, Opponent.TurnCooldown - dt);
            Player.Moving = playerForward;
            ThinkAI(dt);
            float px = Player.X, pz = Player.Z, ax = Opponent.X, az = Opponent.Z;
            if (playerForward) Advance(Player, Speed * dt, ref px, ref pz);
            Advance(Opponent, Speed * dt, ref ax, ref az);
            PlayerCrash = CollisionReason(Player, 0, px, pz);
            OpponentCrash = CollisionReason(Opponent, 1, ax, az);
            if (playerForward && Opponent.Moving && SegmentsIntersect(Player.X, Player.Z, px, pz, Opponent.X, Opponent.Z, ax, az))
            { PlayerCrash = OpponentCrash = "RIDER COLLISION"; }
            if (PlayerCrash.Length == 0 && playerForward) AddTrail(0, Player.X, Player.Z, px, pz, Player.Heading);
            if (OpponentCrash.Length == 0) AddTrail(1, Opponent.X, Opponent.Z, ax, az, Opponent.Heading);
            if (PlayerCrash.Length == 0) { Player.X = px; Player.Z = pz; }
            if (OpponentCrash.Length == 0) { Opponent.X = ax; Opponent.Z = az; }
            Player.Crashed = PlayerCrash.Length > 0;
            Opponent.Crashed = OpponentCrash.Length > 0;
            if (Player.Crashed || Opponent.Crashed) ResolveHeat();
        }

        private static void Advance(Rider rider, float distance, ref float x, ref float z)
        {
            if (rider.Heading == 0) z += distance;
            else if (rider.Heading == 1) x += distance;
            else if (rider.Heading == 2) z -= distance;
            else x -= distance;
        }

        private string CollisionReason(Rider rider, int owner, float x, float z)
        {
            if (Math.Abs(x) > Limit - WallMargin || Math.Abs(z) > Limit - WallMargin)
                return Elapsed >= ShrinkStartsAt ? "CLOSING BOUNDARY" : "ARENA WALL";
            if (x == rider.X && z == rider.Z) return "";
            for (int i = 0; i < Trails.Count; i++)
            {
                var t = Trails[i];
                // The newest own segment is attached to the rider. At a turn, its endpoint
                // remains the departure point; neither is an obstacle until the path crosses later.
                if (t.Owner == owner && IsLatestOwnSegment(i, owner) &&
                    Math.Abs(t.X1 - rider.X) < .001f && Math.Abs(t.Z1 - rider.Z) < .001f) continue;
                if (SegmentsIntersect(rider.X, rider.Z, x, z, t.X0, t.Z0, t.X1, t.Z1))
                    return t.Owner == owner ? "OWN LIGHT TRAIL" : "RIVAL LIGHT TRAIL";
            }
            return "";
        }

        private bool IsLatestOwnSegment(int index, int owner)
        {
            for (int i = index + 1; i < Trails.Count; i++)
                if (Trails[i].Owner == owner) return false;
            return true;
        }

        private static bool SegmentsIntersect(float x0, float z0, float x1, float z1, float a0, float b0, float a1, float b1)
        {
            const float epsilon = .0001f;
            bool travelVertical = Math.Abs(x1 - x0) < epsilon;
            bool trailVertical = Math.Abs(a1 - a0) < epsilon;
            if (travelVertical == trailVertical)
            {
                if (travelVertical && Math.Abs(x0 - a0) < epsilon)
                    return Math.Max(Math.Min(z0, z1), Math.Min(b0, b1)) <= Math.Min(Math.Max(z0, z1), Math.Max(b0, b1)) - epsilon;
                if (!travelVertical && Math.Abs(z0 - b0) < epsilon)
                    return Math.Max(Math.Min(x0, x1), Math.Min(a0, a1)) <= Math.Min(Math.Max(x0, x1), Math.Max(a0, a1)) - epsilon;
                return false;
            }
            if (travelVertical)
                return x0 >= Math.Min(a0, a1) - epsilon && x0 <= Math.Max(a0, a1) + epsilon &&
                    b0 >= Math.Min(z0, z1) - epsilon && b0 <= Math.Max(z0, z1) + epsilon;
            return a0 >= Math.Min(x0, x1) - epsilon && a0 <= Math.Max(x0, x1) + epsilon &&
                z0 >= Math.Min(b0, b1) - epsilon && z0 <= Math.Max(b0, b1) + epsilon;
        }

        private void AddTrail(int owner, float x0, float z0, float x1, float z1, int heading)
        {
            for (int i = Trails.Count - 1; i >= 0; i--)
            {
                var last = Trails[i];
                if (last.Owner != owner) continue;
                if (last.Heading == heading && Math.Abs(last.X1 - x0) < .001f && Math.Abs(last.Z1 - z0) < .001f)
                { last.X1 = x1; last.Z1 = z1; Trails[i] = last; return; }
                break;
            }
            Trails.Add(new Trail(owner, x0, z0, x1, z1, heading));
        }

        private void ThinkAI(float dt)
        {
            aiThink -= dt;
            if (aiThink > 0) return;
            aiThink = .22f;
            float straight = Clearance(Opponent, Opponent.Heading);
            if (straight > 8 + (float)random.NextDouble() * 3) return;
            int left = (Opponent.Heading + 3) % 4, right = (Opponent.Heading + 1) % 4;
            float l = Clearance(Opponent, left), r = Clearance(Opponent, right);
            if (l > straight + 2 || r > straight + 2) Turn(Opponent, l >= r ? -1 : 1);
        }

        private float Clearance(Rider rider, int heading)
        {
            float dx = heading == 1 ? 1 : heading == 3 ? -1 : 0;
            float dz = heading == 0 ? 1 : heading == 2 ? -1 : 0;
            float max = 0;
            for (float d = 1; d < 60; d += .5f)
            {
                float x = rider.X + dx * d, z = rider.Z + dz * d;
                if (Math.Abs(x) >= Limit - WallMargin || Math.Abs(z) >= Limit - WallMargin) break;
                // Probe the entire ray, rather than only its endpoint, so perpendicular trails count.
                if (CollisionReason(rider, 1, x, z).Length > 0) break;
                max = d;
            }
            return max;
        }

        private void ResolveHeat()
        {
            HeatEnded = true;
            if (Player.Crashed && Opponent.Crashed) Result = "DRAW - BOTH RIDERS CRASHED";
            else if (Player.Crashed) { OpponentScore++; Result = "RIVAL WINS THE HEAT"; }
            else { PlayerScore++; Result = "YOU WIN THE HEAT"; }
            if (MatchEnded) Result = PlayerScore > OpponentScore ? "MATCH WON" : "MATCH LOST";
        }
    }
}
