using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridRun.Solo
{
    // Non-sensitive local progress (PlayerPrefs): bests, records, badges, history, operation
    // unlocks, trail look, and setup choices. Mirrors the browser's localStorage features.
    public static class SoloProfile
    {
        [Serializable] public sealed class Setup
        {
            public string rival = "hunter", difficulty = "standard", map = "classic", rule = "classic", operation = "portal";
            public bool finalRival;
        }
        [Serializable] public sealed class RunEntry
        {
            public string date, mode, map, operation, difficulty, outcome, day;
            public int score;
        }
        [Serializable] sealed class History { public List<RunEntry> runs = new List<RunEntry>(); }
        [Serializable] public sealed class OpEntry { public string id; public bool completed; public int best; }
        [Serializable] sealed class OpProgress { public List<OpEntry> entries = new List<OpEntry>(); }
        [Serializable] sealed class Records { public int duel, arena, operations, traversal, shift; public List<string> dailyDays = new List<string>(); public List<int> dailyScores = new List<int>(); }
        [Serializable] sealed class Badges { public bool victor, trap, escape, journey, shift, daily; }

        const string SetupKey = "gridrun.solo.setup.v1", HistoryKey = "gridrun.history.v1", OpsKey = "gridrun.operations.v1";
        const string RecordsKey = "gridrun.records.v1", BadgesKey = "gridrun.badges.v1", LookKey = "gridrun.look.v1", SoundKey = "gridrun.sound.v1";
        public static readonly string[] Looks = { "default", "amber", "violet", "white" };
        public static bool StorageAvailable { get; private set; } = true;

        static string Read(string key)
        {
            try { return PlayerPrefs.GetString(key, ""); }
            catch (Exception) { StorageAvailable = false; return ""; }
        }

        static void Write(string key, string value)
        {
            try { PlayerPrefs.SetString(key, value); PlayerPrefs.Save(); }
            catch (Exception error) { StorageAvailable = false; Debug.LogWarning("Progress could not be saved: " + error.GetType().Name); }
        }

        static T Load<T>(string key) where T : new()
        {
            string json = Read(key);
            if (string.IsNullOrEmpty(json)) return new T();
            try { return JsonUtility.FromJson<T>(json) ?? new T(); }
            catch (Exception) { return new T(); }
        }

        public static Setup LoadSetup() => Load<Setup>(SetupKey);
        public static void SaveSetup(Setup setup) => Write(SetupKey, JsonUtility.ToJson(setup));

        public static int Best(string key)
        {
            string value = Read("gridrun.best.v1:" + key);
            return int.TryParse(value, out int best) && best >= 0 ? best : 0;
        }

        public static void SaveBest(string key, int score)
        {
            if (score > Best(key)) Write("gridrun.best.v1:" + key, score.ToString());
        }

        public static OpEntry Operation(string id)
        {
            var progress = Load<OpProgress>(OpsKey);
            return progress.entries.Find(e => e.id == id) ?? new OpEntry { id = id };
        }

        public static void CompleteOperation(string id, int points)
        {
            var progress = Load<OpProgress>(OpsKey);
            var entry = progress.entries.Find(e => e.id == id);
            if (entry == null) progress.entries.Add(entry = new OpEntry { id = id });
            entry.completed = true;
            entry.best = Math.Max(entry.best, Math.Max(0, points));
            Write(OpsKey, JsonUtility.ToJson(progress));
        }

        public static bool LookUnlocked(string look) =>
            look == "default" || (look == "amber" && Operation("portal").completed) ||
            (look == "violet" && Operation("sweep").completed) || (look == "white" && Operation("collapse").completed);

        public static string Look
        {
            get { string look = Read(LookKey); return Array.IndexOf(Looks, look) >= 0 && LookUnlocked(look) ? look : "default"; }
            set => Write(LookKey, value);
        }

        public static string LookLabel(string look) =>
            look == "amber" ? "PORTAL AMBER" : look == "violet" ? "SCANNER VIOLET" : look == "white" ? "CONTAINMENT WHITE" : "GRID CYAN";

        public static float[] LookColor(string look) =>
            look == "amber" ? new[] { 1f, .62f, .18f } : look == "violet" ? new[] { .78f, .45f, 1f }
            : look == "white" ? new[] { .92f, .98f, 1f } : new[] { .15f, .88f, 1f };

        public static bool AutoDrive
        {
            get { try { return PlayerPrefs.GetInt("gridrun.autodrive.v1", 1) == 1; } catch (Exception) { return true; } }
            set
            {
                try { PlayerPrefs.SetInt("gridrun.autodrive.v1", value ? 1 : 0); PlayerPrefs.Save(); }
                catch (Exception error) { Debug.LogWarning("Auto Drive preference could not be saved: " + error.GetType().Name); }
            }
        }

        public static bool Sound
        {
            get { string value = Read(SoundKey); return value != "off"; }
            set => Write(SoundKey, value ? "on" : "off");
        }

        public static List<RunEntry> Runs => Load<History>(HistoryKey).runs;

        // Saves a finished run, its record, and earned badges. Returns newly earned badge labels.
        public static List<string> RecordRun(RunEntry entry, ICollection<string> badgeIds)
        {
            var history = Load<History>(HistoryKey);
            history.runs.Insert(0, entry);
            if (history.runs.Count > 8) history.runs.RemoveRange(8, history.runs.Count - 8);
            Write(HistoryKey, JsonUtility.ToJson(history));
            var records = Load<Records>(RecordsKey);
            switch (entry.mode)
            {
                case "duel": records.duel = Math.Max(records.duel, entry.score); break;
                case "arena": records.arena = Math.Max(records.arena, entry.score); break;
                case "operations": records.operations = Math.Max(records.operations, entry.score); break;
                case "shift": records.shift = Math.Max(records.shift, entry.score); break;
            }
            if (!string.IsNullOrEmpty(entry.day))
            {
                int index = records.dailyDays.IndexOf(entry.day);
                if (index < 0) { records.dailyDays.Add(entry.day); records.dailyScores.Add(entry.score); }
                else records.dailyScores[index] = Math.Max(records.dailyScores[index], entry.score);
            }
            Write(RecordsKey, JsonUtility.ToJson(records));
            var badges = Load<Badges>(BadgesKey);
            var earned = new List<string>();
            foreach (string id in badgeIds)
            {
                bool had = BadgeOwned(badges, id);
                SetBadge(badges, id);
                if (!had) earned.Add(BadgeLabel(id));
            }
            Write(BadgesKey, JsonUtility.ToJson(badges));
            return earned;
        }

        public static int DailyBest(string day)
        {
            var records = Load<Records>(RecordsKey);
            int index = records.dailyDays.IndexOf(day);
            return index >= 0 ? records.dailyScores[index] : -1;
        }

        public static string RecordsLine()
        {
            var r = Load<Records>(RecordsKey);
            return "DUEL " + r.duel + "  /  ARENA " + r.arena + "  /  OPERATIONS " + r.operations + "  /  GRID SHIFT " + r.shift;
        }

        public static string BadgesLine()
        {
            var b = Load<Badges>(BadgesKey);
            var owned = new List<string>();
            foreach (string id in new[] { "victor", "trap", "escape", "journey", "shift", "daily" })
                if (BadgeOwned(b, id)) owned.Add(BadgeLabel(id));
            return owned.Count > 0 ? string.Join("  /  ", owned) : "Win a match, escape closely, trap a rival, or clear a journey.";
        }

        public static string BadgeLabel(string id) => id switch
        {
            "victor" => "GRID VICTOR", "trap" => "CLEAN TRAP", "escape" => "CLOSE ESCAPE",
            "journey" => "JOURNEY CLEAR", "shift" => "FORM MASTER", "daily" => "DAILY CLEAR", _ => id.ToUpperInvariant(),
        };

        static bool BadgeOwned(Badges b, string id) => id switch
        {
            "victor" => b.victor, "trap" => b.trap, "escape" => b.escape, "journey" => b.journey, "shift" => b.shift, "daily" => b.daily, _ => false,
        };

        static void SetBadge(Badges b, string id)
        {
            switch (id)
            {
                case "victor": b.victor = true; break;
                case "trap": b.trap = true; break;
                case "escape": b.escape = true; break;
                case "journey": b.journey = true; break;
                case "shift": b.shift = true; break;
                case "daily": b.daily = true; break;
            }
        }
    }
}
