using System;
using System.Globalization;

namespace GridRun.Sim
{
    // Ports of dist/match.js (Duel, first to three) and dist/arena.js (exactly three heats),
    // plus the Daily Signal seed from dist/game.js (rules version 3).
    public sealed class Match
    {
        public bool Arena;        // false: Duel / Daily Signal
        public uint Seed;
        public string Rival = "hunter", Difficulty = "standard", Map = "classic", Rule = "classic";
        public int RoundNumber = 1, PlayerWins, RivalWins, Points;
        public bool Completed, ResultRecorded;

        public bool Won => Arena ? Completed && PlayerWins >= 2 : PlayerWins >= 3;
    }

    public sealed class DailyChallenge
    {
        public const int RulesVersion = 3;
        public string Day;
        public uint Seed;
        public string Map, Rival, Difficulty = "standard";
    }

    public static class SimMatches
    {
        public static uint RoundSeed(uint seed, int roundNumber)
        {
            uint value = seed ^ JsMath.Imul((uint)roundNumber, 0x9e3779b9u);
            value ^= value >> 16;
            value = JsMath.Imul(value, 0x7feb352du);
            value ^= value >> 15;
            value = JsMath.Imul(value, 0x846ca68bu);
            return value ^ (value >> 16);
        }

        public static uint RoundSeed(Match match) => RoundSeed(match.Seed, match.RoundNumber);

        // Returns "complete" or "next" as the browser does.
        public static string Record(Match match, string outcome, int points)
        {
            if (match.ResultRecorded || match.Completed) return match.Completed ? "complete" : "next";
            if (match.Arena && outcome != "won" && outcome != "lost") throw new ArgumentException("Invalid Arena outcome");
            if (!match.Arena && outcome != "won" && outcome != "lost" && outcome != "draw") throw new ArgumentException("Invalid round outcome");
            match.ResultRecorded = true;
            match.Points += Math.Max(0, points);
            if (outcome == "won") match.PlayerWins++;
            if (outcome == "lost") match.RivalWins++;
            match.Completed = match.Arena ? match.RoundNumber == 3 : match.PlayerWins >= 3 || match.RivalWins >= 3;
            return match.Completed ? "complete" : "next";
        }

        public static bool Next(Match match)
        {
            if (!match.ResultRecorded || match.Completed) return false;
            match.RoundNumber++;
            match.ResultRecorded = false;
            return true;
        }

        public static Round CreateRoundFor(Match match)
        {
            uint seed = RoundSeed(match);
            return match.Arena
                ? GridRules.CreateArenaRound(seed, match.Difficulty, match.Map, match.Rule)
                : GridRules.CreateDuelRound(seed, match.Rival, match.Difficulty, match.Map);
        }

        public static DailyChallenge DailyFor(DateTime utc)
        {
            string day = utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            uint seed = 2166136261u;
            foreach (char c in "grid-run:daily:v" + DailyChallenge.RulesVersion + ":" + day)
                seed = JsMath.Imul(seed ^ c, 16777619u);
            string[] maps = { "classic", "solar", "vault" };
            string[] rivals = { "hunter", "survivor", "trapper" };
            return new DailyChallenge { Day = day, Seed = seed, Map = maps[seed % 3], Rival = rivals[(seed >> 8) % 3] };
        }

        public static uint OperationStageSeed(uint flowSeed, int index) => flowSeed ^ JsMath.Imul((uint)(index + 1), 0x9e3779b9u);
    }
}
