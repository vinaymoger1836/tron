using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GridRun.Sim;
using UnityEditor;
using UnityEngine;

namespace GridRun.Editor
{
    // Replays traces recorded from the browser rules (scripts/sim-traces.mjs) through the C# port
    // and requires every tick to match exactly: positions and timers are compared bit for bit.
    // Failure modes this guards against: integer/bit-operation differences in the seeded RNG,
    // floating-point operation order, AI tie-breaking, list ordering after trail cuts, and
    // event or outcome drift between the browser and Android rule sets.
    public static class SimParity
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [MenuItem("Grid Run/Rules/Check Browser Parity")]
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--parity-traces");
            string path = index >= 0 && index + 1 < args.Length ? args[index + 1]
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/sim-parity/traces.txt"));
            var report = new StringBuilder();
            int failures = 0, frames = 0, scenarios = 0, checks = 0;
            try
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.StartsWith("S ", StringComparison.Ordinal))
                    {
                        scenarios++;
                        failures += ReplayScenario(lines, ref i, report, ref frames);
                    }
                    else if (line.StartsWith("D ", StringComparison.Ordinal))
                    {
                        checks++;
                        var p = line.Split(' ');
                        var daily = SimMatches.DailyFor(DateTime.ParseExact(p[1], "yyyy-MM-dd", Inv));
                        string mine = "D " + daily.Day + " " + daily.Seed + " " + daily.Map + " " + daily.Rival;
                        if (mine != line) { failures++; report.AppendLine("DAILY MISMATCH\n  js: " + line + "\n  cs: " + mine); }
                    }
                    else if (line.StartsWith("R ", StringComparison.Ordinal))
                    {
                        checks++;
                        var p = line.Split(' ');
                        uint value = SimMatches.RoundSeed(uint.Parse(p[1], Inv), int.Parse(p[2], Inv));
                        if (value.ToString(Inv) != p[3]) { failures++; report.AppendLine("ROUND SEED MISMATCH " + line + " cs=" + value); }
                    }
                }
            }
            catch (Exception error)
            {
                failures++;
                report.AppendLine("ERROR " + error);
            }
            string summary = $"Scenarios {scenarios}, frames {frames}, seed checks {checks}, failures {failures}";
            report.Insert(0, summary + "\n");
            string reportPath = Path.Combine(Path.GetDirectoryName(path), "parity-report.txt");
            File.WriteAllText(reportPath, report.ToString());
            Debug.Log((failures == 0 ? "SIM_PARITY_PASS " : "SIM_PARITY_FAIL ") + summary + " report: " + reportPath);
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 3);
        }

        sealed class Action { public int Tick; public string Type; public string A, B; }

        static int ReplayScenario(string[] lines, ref int i, StringBuilder report, ref int frames)
        {
            var header = lines[i].Split(' ');
            string name = header[1], kind = header[2];
            uint seed = uint.Parse(header[3], Inv);
            string rival = header[4], difficulty = header[5], map = header[6], rule = header[7], op = header[8];
            var actions = new List<Action>();
            while (i + 1 < lines.Length && lines[i + 1].StartsWith("A ", StringComparison.Ordinal))
            {
                var p = lines[++i].Split(' ');
                actions.Add(new Action { Tick = int.Parse(p[1], Inv), Type = p[2], A = p.Length > 3 ? p[3] : null, B = p.Length > 4 ? p[4] : null });
            }
            Round round;
            switch (kind)
            {
                case "duel": round = GridRules.CreateDuelRound(seed, rival, difficulty, map, rule); break;
                case "arena": round = GridRules.CreateArenaRound(seed, difficulty, map, rule); break;
                case "operation": round = GridRules.CreateOperationRound(op, seed, difficulty); break;
                case "shift": round = SimShift.CreateShiftRound(seed, difficulty, map); break;
                case "ai4":
                case "autoplayer": round = AiRound(kind, seed, difficulty, map, rule); break;
                default: round = GridRules.CreateLessonRound(seed); break;
            }
            bool shift = kind == "shift";
            int failures = 0, tick = 0;
            while (i + 1 < lines.Length && lines[i + 1].StartsWith("F ", StringComparison.Ordinal))
            {
                string expected = lines[++i];
                var applied = new StringBuilder();
                foreach (var action in actions) if (action.Tick == tick) applied.Append(Apply(round, shift, action) ? '1' : '0');
                if (shift) SimShift.StepShiftRound(round); else GridRules.StepRound(round);
                string actual = Frame(round, GridRules.DrainEvents(round), applied.Length > 0 ? applied.ToString() : "-");
                frames++;
                tick++;
                if (actual != expected && failures++ < 3)
                    report.AppendLine("MISMATCH " + name + " tick " + tick + "\n  js: " + expected + "\n  cs: " + actual);
            }
            if (round.Outcome == null && tick < int.Parse(header[9], Inv))
            {
                failures++;
                report.AppendLine("ENDED EARLY IN JS " + name + " at tick " + tick);
            }
            if (i + 1 < lines.Length && lines[i + 1].StartsWith("E ", StringComparison.Ordinal)) i++;
            report.AppendLine((failures == 0 ? "PASS " : "FAIL ") + name + " (" + tick + " ticks, outcome " + Describe(round.Outcome) + ")");
            return failures > 0 ? 1 : 0;
        }

        static Round AiRound(string kind, uint seed, string difficulty, string map, string rule)
        {
            var spawns = SimMaps.Get(map).Arena;
            var riders = new List<RiderSpec>();
            if (kind == "ai4")
            {
                string[] styles = { "hunter", "survivor", "trapper", "survivor" };
                for (int i = 0; i < spawns.Length; i++) riders.Add(new RiderSpec("AI" + i, spawns[i], false, "ai", styles[i]));
            }
            else
            {
                riders.Add(new RiderSpec("YOU", spawns[0], true, "ai"));
                string[] names = { "AXIOM", "HELIX", "PULSE" }, styles = { "hunter", "survivor", "trapper" };
                for (int i = 0; i < 3; i++) riders.Add(new RiderSpec(names[i], spawns[i + 1], false, "ai", styles[i]));
            }
            return GridRules.CreateRound(seed, riders, difficulty, map, rule, kind == "ai4");
        }

        static bool Apply(Round round, bool shift, Action action)
        {
            switch (action.Type)
            {
                case "turn": return shift ? SimShift.QueueTurn(round, "YOU", int.Parse(action.A, Inv)) : GridRules.QueueTurn(round, "YOU", int.Parse(action.A, Inv));
                case "fire": return shift ? SimShift.SetFire(round, "YOU", action.A == "1") : GridRules.SetFire(round, "YOU", action.A == "1");
                case "throttle": return GridRules.SetThrottle(round, "YOU", action.A == "1");
                case "auto": return GridRules.SetAutoDrive(round, "YOU", action.A == "1");
                case "flight": return GridRules.RequestFlight(round, "YOU", action.A);
                case "form": return SimShift.RequestForm(round, "YOU");
                case "disc": return SimShift.ThrowDisc(round, "YOU");
                case "move": return SimShift.SetMove(round, "YOU", double.Parse(action.A, Inv), double.Parse(action.B, Inv));
            }
            throw new ArgumentException("Unknown action " + action.Type);
        }

        static string Hex(double value) => BitConverter.DoubleToInt64Bits(value == 0 ? 0 : value).ToString("x16");

        static string Sum<T>(IEnumerable<T> items, Func<T, double[]> fields)
        {
            double total = 0;
            foreach (var item in items) foreach (double field in fields(item)) total += field;
            return Hex(total);
        }

        static string Describe(Outcome outcome) => outcome == null ? "-" : outcome.Kind + ":" + (outcome.Reason ?? "") + ":" + (outcome.By ?? "");

        static string Frame(Round round, List<SimEvent> events, string applied)
        {
            bool shift = round.Shift != null;
            string riders = string.Join(" ", round.Riders.Select(r => string.Join(":", new[]
            {
                r.Id, Hex(r.X), Hex(r.Z), r.Dir.ToString(Inv), r.Alive ? "1" : "0", r.Ammo.ToString(Inv),
                shift ? r.Hp.ToString(Inv) : "-", r.Form ?? "-", r.FlightKind ?? "-", r.RocketReady ? "1" : "0", Hex(r.NitroUntil),
            })));
            string pickups = string.Join(";", round.Pickups.Select(p => p.Type + "@" + Hex(p.X) + "," + Hex(p.Z) + (p.Active ? "" : "!")));
            var bullets = shift ? round.Shift.Bullets : round.Bullets;
            var discs = shift ? round.Shift.Discs : new List<Disc>();
            string evs = string.Join(",", events.Select(e => e.Type == "crash" ? "crash:" + e.RiderId + ":" + e.Reason + ":" + (e.By ?? "") : e.Type));
            return string.Join(" | ", new[]
            {
                "F", round.Tick.ToString(Inv), round.RandomState.ToString(Inv), round.Score.ToString(Inv), Hex(round.Limit),
                round.Segments.Count.ToString(Inv), Sum(round.Segments, s => new[] { s.X1, s.Z1, s.X2, s.Z2 }),
                bullets.Count.ToString(Inv), Sum(bullets, b => new[] { b.X, b.Z }), discs.Count.ToString(Inv),
                Sum(discs, d => new[] { d.X, d.Z }), Describe(round.Outcome), applied, evs.Length > 0 ? evs : "-",
                pickups.Length > 0 ? pickups : "-", riders,
            });
        }
    }
}
