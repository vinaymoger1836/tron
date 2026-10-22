using System.Collections.Generic;
using GridRun.Sim;

namespace GridRun.Solo
{
    // What the renderer needs from a round. Live play captures one each frame; crash replays
    // keep a short history of copies and interpolate between them.
    public struct RiderView
    {
        public string Id, Form, FlightKind, Style;
        public double X, Z, FlightStartedAt, FlightUntil, FormChangedAt;
        public int Dir;
        public bool Alive, IsPlayer, HasDiscOut;
        public float[] Color;
    }
    public struct SegmentView { public int Id; public string OwnerId; public double X1, Z1, X2, Z2, ClosedAt; public bool Active; }
    public struct BulletView { public double X, Z; public bool Rocket, FromPlayer; public int Vx, Vz; }
    public struct DiscView { public double X, Z; public bool FromPlayer; }
    public struct PickupView { public int Id; public string Type; public double X, Z; }

    public sealed class SceneView
    {
        public double Elapsed, Limit, TrailLifetime = double.PositiveInfinity;
        public string Map = "classic", Rule = "classic";
        public readonly List<RiderView> Riders = new List<RiderView>();
        public readonly List<SegmentView> Segments = new List<SegmentView>();
        public readonly List<BulletView> Bullets = new List<BulletView>();
        public readonly List<DiscView> Discs = new List<DiscView>();
        public readonly List<PickupView> Pickups = new List<PickupView>();
        public string OpId;
        public int OpProgress, OpPulse, OpCollapse;
        public bool HasBeam, BeamActive;
        public double BeamX, NextLimit = double.NaN;

        public void Capture(Round round)
        {
            Elapsed = round.Elapsed; Limit = round.Limit; Map = round.Map; Rule = round.Rule; TrailLifetime = round.TrailLifetime;
            Riders.Clear(); Segments.Clear(); Bullets.Clear(); Discs.Clear(); Pickups.Clear();
            foreach (var r in round.Riders)
            {
                bool discOut = false;
                if (round.Shift != null) foreach (var d in round.Shift.Discs) if (d.OwnerId == r.Id) discOut = true;
                Riders.Add(new RiderView
                {
                    Id = r.Id, X = r.X, Z = r.Z, Dir = r.Dir, Alive = r.Alive, IsPlayer = r.IsPlayer, Form = r.Form ?? "cycle",
                    FlightKind = GridRules.Airborne(round, r) ? r.FlightKind : null, FlightStartedAt = r.FlightStartedAt, FlightUntil = r.FlightUntil,
                    FormChangedAt = r.Form != null ? r.FormChangedAt : -10, Style = r.Style, Color = r.Color, HasDiscOut = discOut,
                });
            }
            foreach (var s in round.Segments)
                Segments.Add(new SegmentView { Id = s.Id, OwnerId = s.OwnerId, X1 = s.X1, Z1 = s.Z1, X2 = s.X2, Z2 = s.Z2, Active = s.Active, ClosedAt = s.ClosedAt });
            var bullets = round.Shift != null ? round.Shift.Bullets : round.Bullets;
            foreach (var b in bullets)
                Bullets.Add(new BulletView { X = b.X, Z = b.Z, Rocket = b.Rocket, FromPlayer = b.OwnerId == "YOU", Vx = b.Vx, Vz = b.Vz });
            if (round.Shift != null)
                foreach (var d in round.Shift.Discs) Discs.Add(new DiscView { X = d.X, Z = d.Z, FromPlayer = d.OwnerId == "YOU" });
            foreach (var p in round.Pickups) if (p.Active) Pickups.Add(new PickupView { Id = p.Id, Type = p.Type, X = p.X, Z = p.Z });
            var op = round.Operation;
            OpId = op?.Id;
            if (op != null)
            {
                OpProgress = op.Progress; OpPulse = op.PulseIndex; OpCollapse = op.CollapseIndex;
                HasBeam = op.Beam != null; BeamActive = op.Beam?.Active ?? false; BeamX = op.Beam?.X ?? 0;
                NextLimit = op.NextLimit ?? double.NaN;
            }
            else { HasBeam = false; NextLimit = double.NaN; }
        }

        public SceneView Copy()
        {
            var fresh = new SceneView();
            fresh.CopyFrom(this);
            return fresh;
        }

        public void CopyFrom(SceneView other)
        {
            Elapsed = other.Elapsed; Limit = other.Limit; TrailLifetime = other.TrailLifetime; Map = other.Map; Rule = other.Rule;
            OpId = other.OpId; OpProgress = other.OpProgress; OpPulse = other.OpPulse; OpCollapse = other.OpCollapse;
            HasBeam = other.HasBeam; BeamActive = other.BeamActive; BeamX = other.BeamX; NextLimit = other.NextLimit;
            Riders.Clear(); Riders.AddRange(other.Riders);
            Segments.Clear(); Segments.AddRange(other.Segments);
            Bullets.Clear(); Bullets.AddRange(other.Bullets);
            Discs.Clear(); Discs.AddRange(other.Discs);
            Pickups.Clear(); Pickups.AddRange(other.Pickups);
        }

        // Blends `next` toward `from` positions for smooth replay; trails grow by matching ids.
        public void Interpolate(SceneView from, SceneView next, double t)
        {
            CopyFrom(next);
            Elapsed = from.Elapsed + (next.Elapsed - from.Elapsed) * t;
            for (int i = 0; i < Riders.Count; i++)
            {
                var r = Riders[i];
                foreach (var p in from.Riders)
                {
                    if (p.Id != r.Id || !p.Alive || !r.Alive || p.Form != r.Form || (r.Form == "cycle" && p.Dir != r.Dir)) continue;
                    r.X = p.X + (r.X - p.X) * t; r.Z = p.Z + (r.Z - p.Z) * t;
                    Riders[i] = r;
                    break;
                }
            }
            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                foreach (var p in from.Segments)
                {
                    if (p.Id != s.Id) continue;
                    s.X2 = p.X2 + (s.X2 - p.X2) * t; s.Z2 = p.Z2 + (s.Z2 - p.Z2) * t;
                    Segments[i] = s;
                    break;
                }
            }
        }
    }
}
