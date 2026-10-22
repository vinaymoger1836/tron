using System;
using System.Collections.Generic;

namespace GridRun.Sim
{
    // Port of dist/operations.js plus prepareOperation/updateOperationProgress from dist/simulation.js.
    public sealed class Gate { public string Id; public double MinX, MaxX, MinZ, MaxZ, OpenFrom, OpenUntil; }
    public sealed class Gap { public string Id, Kind; public double MinX, MaxX, MinZ, MaxZ; }
    public sealed class CollapseStep { public double At, Limit; }

    public sealed class Mission
    {
        public string Id, Name, Map, Kind, Objective, Briefing, Tip, HudTip, RewardLook, RewardLore;
        public double TimeLimit = double.NaN; // NaN: no limit (Sentinel)
        public Spawn Spawn, RivalSpawn;
        public bool HasRival;
        public Gap[] Gaps;
        public double FinishZ;
        public Gate[] Gates;
        public double[] Pulses;
        public double WarningTime, ActiveTime;
        public CollapseStep[] Steps;
        public bool HasTimeLimit => !double.IsNaN(TimeLimit);
    }

    public static class SimOperations
    {
        public static readonly string[] Journey = { "portal", "sweep", "collapse" };
        public static readonly string[] Singles = { "portal", "sweep", "collapse" };

        public static readonly Dictionary<string, Mission> All = new Dictionary<string, Mission>
        {
            ["transit"] = new Mission
            {
                Id = "transit", Name = "Solar Transit", Map = "classic", Kind = "traversal",
                Objective = "Cross the jet gap, then the sail gap, and reach the finish before time runs out.",
                Briefing = "Two broken spans block the northbound route. Lift over the first with the light jet, then glide over the second with the solar sail.",
                Tip = "Use the jet just before the amber gap, then the sail just before the cyan gap. Land beyond each gap.",
                HudTip = "Amber gap: jet. Cyan gap: sail.",
                TimeLimit = 12, Spawn = new Spawn(0, 22, 0),
                Gaps = new[]
                {
                    new Gap { Id = "Jet gap", Kind = "jet", MinX = -52, MaxX = 52, MinZ = 10, MaxZ = 15 },
                    new Gap { Id = "Sail gap", Kind = "sail", MinX = -52, MaxX = 52, MinZ = -12, MaxZ = -2 },
                },
                FinishZ = -32,
                RewardLore = "The light jet clears short breaks; a solar sail carries momentum across a longer span.",
            },
            ["portal"] = new Mission
            {
                Id = "portal", Name = "Portal Run", Map = "solar", Kind = "checkpoints",
                Objective = "Cross three gates in order before the timer ends.",
                Briefing = "The transit corridor is restarting. Follow the lit sequence; closed shutters are solid.",
                Tip = "Ride north through Gate 1, turn east through Gate 2, then go north outside the east pylon to Gate 3.",
                HudTip = "Follow the green gates in order.",
                TimeLimit = 18, Spawn = new Spawn(0, 22, 0),
                Gates = new[]
                {
                    new Gate { Id = "Gate 1", MinX = -4, MaxX = 4, MinZ = 7, MaxZ = 10, OpenFrom = 0.65, OpenUntil = 6 },
                    new Gate { Id = "Gate 2", MinX = 8, MaxX = 11, MinZ = 6, MaxZ = 10, OpenFrom = 1.55, OpenUntil = 10 },
                    new Gate { Id = "Gate 3", MinX = 18, MaxX = 22, MinZ = -9, MaxZ = -5, OpenFrom = 3.15, OpenUntil = 17 },
                },
                RewardLook = "amber", RewardLore = "The transit portals were built for speed, but timing keeps the Grid alive.",
            },
            ["sweep"] = new Mission
            {
                Id = "sweep", Name = "System Sweep", Map = "classic", Kind = "scanner",
                Objective = "Survive three marked scanner sweeps.",
                Briefing = "A search craft has locked onto this sector. Its floor mark appears before each beam.",
                Tip = "Watch the yellow lane mark. Turn out of that lane before the beam fires.",
                HudTip = "Leave the marked lane before the beam fires.",
                TimeLimit = 16.5, Spawn = new Spawn(0, 18, 0),
                Pulses = new double[] { 4, 9, 14 }, WarningTime = 1.5, ActiveTime = 0.7,
                RewardLook = "violet", RewardLore = "The scanner only sees a lane it has already marked. Movement breaks its prediction.",
            },
            ["collapse"] = new Mission
            {
                Id = "collapse", Name = "Arena Collapse", Map = "classic", Kind = "collapse",
                Objective = "Survive until the containment ring stabilizes.",
                Briefing = "The perimeter is folding inward. Each amber warning ring becomes the new boundary.",
                Tip = "Stay inside the marked ring before it locks. Keep an escape lane clear.",
                HudTip = "Stay inside the amber warning ring.",
                TimeLimit = 13.5, Spawn = new Spawn(0, 18, 0),
                Steps = new[] { new CollapseStep { At = 4, Limit = 25 }, new CollapseStep { At = 8, Limit = 21 }, new CollapseStep { At = 12, Limit = 17 } },
                WarningTime = 1.5,
                RewardLook = "white", RewardLore = "Containment walls do not chase riders. They close on a fixed schedule.",
            },
            ["sentinel"] = new Mission
            {
                Id = "sentinel", Name = "The Sentinel", Map = "solar", Kind = "rival",
                Objective = "Derez the Sentinel to complete the optional final challenge.",
                Briefing = "One guard program remains at the exit. Its cycle follows the same collision rules as yours.",
                Tip = "The Sentinel favors pressure, but it cannot cross a live light wall.",
                HudTip = "Trap the Sentinel with light walls.",
                Spawn = new Spawn(-5, 19, 0), RivalSpawn = new Spawn(5, -19, 2), HasRival = true,
                RewardLore = "The Sentinel was the last test, not the last path through the Grid.",
            },
        };

        public static Mission Get(string id) => id != null && All.TryGetValue(id, out var mission) ? mission : All["portal"];

        static string Fixed1(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        public static void Prepare(Round round)
        {
            var op = round.Operation;
            if (op == null) return;
            var mission = Get(op.Id);
            double t = round.Elapsed;
            round.DynamicObstacles = new List<Obstacle>();
            op.Warning = "";
            op.NextLimit = null;
            op.Beam = null;
            if (mission.Kind == "traversal")
            {
                foreach (var gap in mission.Gaps)
                    round.DynamicObstacles.Add(new Obstacle { Id = gap.Id, MinX = gap.MinX, MaxX = gap.MaxX, MinZ = gap.MinZ, MaxZ = gap.MaxZ, Kind = "gap", FlightKind = gap.Kind });
                op.Warning = op.Progress < mission.Gaps.Length
                    ? mission.Gaps[op.Progress].Id + ": use " + mission.Gaps[op.Progress].Kind.ToUpperInvariant() + " before the edge"
                    : "Both spans cleared. Reach the finish line.";
            }
            else if (mission.Kind == "checkpoints")
            {
                for (int index = 0; index < mission.Gates.Length; index++)
                {
                    var gate = mission.Gates[index];
                    bool open = t >= gate.OpenFrom && t < gate.OpenUntil;
                    if (!open) round.DynamicObstacles.Add(new Obstacle { Id = gate.Id + " shutter", MinX = gate.MinX, MaxX = gate.MaxX, MinZ = gate.MinZ, MaxZ = gate.MaxZ });
                    if (index != op.Progress) continue;
                    if (open && op.AnnouncedGate != index)
                    {
                        op.AnnouncedGate = index;
                        round.Events.Add(new SimEvent { Type = "operation-warning", Message = gate.Id + " is open." });
                    }
                    if (!open)
                    {
                        double wait = gate.OpenFrom - t;
                        op.Warning = wait > 0 ? gate.Id + " opens in " + Fixed1(wait) + "s" : gate.Id + " is closed. Retry the route.";
                        if (wait <= 0 && op.MissedGate != index)
                        {
                            op.MissedGate = index;
                            round.Events.Add(new SimEvent { Type = "operation-warning", Message = gate.Id + " is closed." });
                        }
                    }
                }
            }
            else if (mission.Kind == "scanner")
            {
                if (op.PulseIndex < mission.Pulses.Length && t >= mission.Pulses[op.PulseIndex] + mission.ActiveTime)
                {
                    op.PulseIndex++;
                    op.TargetLane = null;
                }
                if (op.PulseIndex < mission.Pulses.Length && t >= mission.Pulses[op.PulseIndex] - mission.WarningTime)
                {
                    double next = mission.Pulses[op.PulseIndex];
                    if (op.TargetLane == null)
                    {
                        var player = round.Player;
                        op.TargetLane = (int)Math.Max(-22, Math.Min(22, JsMath.Round(player.X / 2) * 2));
                        round.Events.Add(new SimEvent { Type = "operation-warning", Message = "Scanner locked lane " + op.TargetLane + ". Move sideways." });
                    }
                    bool active = t >= next;
                    op.Beam = new Beam { X = op.TargetLane.Value, Active = active, FiresAt = next, EndsAt = next + mission.ActiveTime };
                    op.Warning = active ? "SCANNER BEAM ACTIVE" : "SCANNER BEAM IN " + Fixed1(Math.Max(0, next - t)) + "s";
                    if (active)
                        round.DynamicObstacles.Add(new Obstacle { Id = "Scanner beam", MinX = op.TargetLane.Value - 0.65, MaxX = op.TargetLane.Value + 0.65, MinZ = -24, MaxZ = 24 });
                }
            }
            else if (mission.Kind == "collapse")
            {
                if (op.CollapseIndex < mission.Steps.Length && t >= mission.Steps[op.CollapseIndex].At)
                {
                    var step = mission.Steps[op.CollapseIndex];
                    round.Limit = step.Limit;
                    op.CollapseIndex++;
                    round.Events.Add(new SimEvent { Type = "operation-warning", Message = "Containment ring locked at " + step.Limit + "." });
                }
                if (op.CollapseIndex < mission.Steps.Length && t >= mission.Steps[op.CollapseIndex].At - mission.WarningTime)
                {
                    var upcoming = mission.Steps[op.CollapseIndex];
                    op.NextLimit = upcoming.Limit;
                    op.Warning = "RING CLOSES IN " + Fixed1(Math.Max(0, upcoming.At - t)) + "s";
                    if (op.WarnedLimit == null || op.WarnedLimit != upcoming.Limit)
                    {
                        op.WarnedLimit = upcoming.Limit;
                        round.Events.Add(new SimEvent { Type = "operation-warning", Message = "Move inside the marked ring before it closes." });
                    }
                }
            }
        }

        public static void UpdateProgress(Round round, Rider player)
        {
            var op = round.Operation;
            var mission = Get(op.Id);
            if (mission.Kind == "traversal")
            {
                var gap = op.Progress < mission.Gaps.Length ? mission.Gaps[op.Progress] : null;
                if (gap != null && player.Z < gap.MinZ - GridRules.RIDER_RADIUS)
                {
                    op.Progress++;
                    round.Score += 400;
                    round.ScoreBreakdown.Checkpoint += 400;
                    round.Events.Add(new SimEvent { Type = "operation-checkpoint", Progress = op.Progress, Total = mission.Gaps.Length, Name = gap.Id });
                }
                if (op.Progress == mission.Gaps.Length && player.Z <= mission.FinishZ)
                    GridRules.CompleteOperation(round, 1000 + (int)Math.Floor((mission.TimeLimit - round.Elapsed) * 50));
                if (round.Outcome == null && round.Elapsed >= mission.TimeLimit)
                    round.Outcome = new Outcome { Kind = "lost", Reason = "timeout", By = "finish line" };
            }
            else if (mission.Kind == "checkpoints")
            {
                var gate = op.Progress < mission.Gates.Length ? mission.Gates[op.Progress] : null;
                if (gate != null && round.Elapsed >= gate.OpenFrom && round.Elapsed < gate.OpenUntil &&
                    player.X >= gate.MinX && player.X <= gate.MaxX && player.Z >= gate.MinZ && player.Z <= gate.MaxZ)
                {
                    op.Progress++;
                    round.Score += 400;
                    round.ScoreBreakdown.Checkpoint += 400;
                    round.Events.Add(new SimEvent { Type = "operation-checkpoint", Progress = op.Progress, Total = mission.Gates.Length, Name = gate.Id });
                    if (op.Progress == mission.Gates.Length)
                        GridRules.CompleteOperation(round, 800 + (int)Math.Max(0, Math.Floor((mission.TimeLimit - round.Elapsed) * 25)));
                }
                if (round.Outcome == null && round.Elapsed >= mission.TimeLimit)
                    round.Outcome = new Outcome { Kind = "lost", Reason = "timeout", By = gate?.Id ?? "final gate" };
            }
            else if (mission.Kind == "scanner")
            {
                if (round.Elapsed >= mission.TimeLimit) GridRules.CompleteOperation(round, 1400);
            }
            else if (mission.Kind == "collapse")
            {
                if (round.Elapsed >= mission.TimeLimit) GridRules.CompleteOperation(round, 1500);
            }
            else if (mission.Kind == "rival" && round.Riders.TrueForAll(r => r.IsPlayer || !r.Alive))
                GridRules.CompleteOperation(round, 1250);
        }
    }
}
