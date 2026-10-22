#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GridRun.Sim;
using GridRun.Solo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // Opt-in desktop end-to-end flow (--solo-capture-dir) through every offline mode using the
    // same UI controls a player taps. Physical Android validation is separate.
    public sealed class DevelopmentSoloCapture : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult { public string name; public bool passed; public string detail; }
        [Serializable] private sealed class Capture { public string file; public int width, height; }
        [Serializable] private sealed class Report
        {
            public string scope = "Desktop end-to-end flow for all offline modes; physical Android validation is separate";
            public string engine, platform;
            public List<CheckResult> checks = new List<CheckResult>();
            public List<Capture> captures = new List<Capture>();
        }
        private readonly Report report = new Report();
        private string output;
        private bool failed;
        private SoloGame solo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--solo-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Solo verification").AddComponent<DevelopmentSoloCapture>();
            DontDestroyOnLoad(runner.gameObject);
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 300);
            runner.StartCoroutine(runner.Run());
        }

        private void Check(string name, bool passed, string detail = "")
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            Debug.Log("SOLO_CHECK " + name + ": " + passed + (detail.Length > 0 ? " (" + detail + ")" : ""));
            if (!passed) failed = true;
        }
        private void Save() => File.WriteAllText(Path.Combine(output, "solo-verification.json"), JsonUtility.ToJson(report, true));
        private void Timeout() { Check("Flow completed before timeout", false); Save(); Application.Quit(2); }

        private static GameObject Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name && button.gameObject.activeInHierarchy) return button.gameObject;
            throw new InvalidOperationException("Missing active control: " + name);
        }
        private static bool Has(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name && button.gameObject.activeInHierarchy) return true;
            return false;
        }
        private static void Submit(string name) => ExecuteEvents.Execute(Control(name), new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        private static void Pointer(string name, bool down)
        {
            var data = new PointerEventData(EventSystem.current) { pointerId = 77 };
            if (down) ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerUpHandler);
        }
        private static void Tap(string name) { Pointer(name, true); Pointer(name, false); }
        private static string Caption(string name) => Control(name).GetComponentInChildren<Text>().text;
        private static Text Label(string name) { var obj = GameObject.Find(name); return obj != null ? obj.GetComponent<Text>() : null; }
        private Rider Player => solo.Round?.Player;

        private IEnumerator Shot(string file, int width, int height)
        {
            if (Screen.width != width || Screen.height != height)
            {
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(.6f);
            }
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG());
            Destroy(texture);
            bool fits = true;
            var corners = new Vector3[4];
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (!button.gameObject.activeInHierarchy) continue;
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                    fits &= corner.x >= -1 && corner.y >= -1 && corner.x <= Screen.width + 1 && corner.y <= Screen.height + 1;
            }
            Check(file + " controls fit viewport", fits);
            report.captures.Add(new Capture { file = file, width = Screen.width, height = Screen.height });
        }

        private IEnumerator Layouts(string name)
        {
            yield return Shot(name + "-phone.png", 1170, 540);
            yield return Shot(name + "-desktop.png", 1280, 720);
            yield return Shot(name + "-tablet.png", 1024, 768);
        }

        private IEnumerator WaitFor(Func<bool> condition, float seconds, string name)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
            Check(name, condition());
        }

        // Leaves the current round for mode select from any in-round state (running, replay, result).
        private IEnumerator ChangeMode()
        {
            if (solo.Phase == "running" || solo.Phase == "countdown") Submit("Pause");
            yield return null;
            if (solo.Phase == "paused") { Submit("Pause Change Mode"); yield return null; yield break; }
            if (solo.Phase == "replay") Submit("Skip Replay");
            float until = Time.realtimeSinceStartup + 3;
            while (solo.Phase != "result" && Time.realtimeSinceStartup < until) yield return null;
            Submit("Result Change Mode");
            yield return null;
        }

        private IEnumerator Run()
        {
            report.engine = Application.unityVersion;
            report.platform = Application.platform.ToString();
            // Start from clean local progress so unlock and record checks prove this run's play.
            // Affects only this desktop verification player's own storage.
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            if (SceneManager.GetActiveScene().name != "PhaseOneArena")
            {
                SceneManager.LoadScene("PhaseOneArena");
                yield return new WaitForSecondsRealtime(1f);
            }
            yield return new WaitForSecondsRealtime(1.5f);
            solo = FindFirstObjectByType<SoloGame>();
            Check("Main menu loaded", solo != null && Has("Start Arena") && Has("Records") && Has("Settings"));
            Check("Main menu has no preview entry", !Has("Style Preview"));
            yield return Shot("warmup.png", 1170, 540);
            yield return Layouts("menu");

            // Mode select lists every offline mode.
            Submit("Start Arena");
            yield return null;
            Check("Mode select lists every offline mode", Has("Mode Duel") && Has("Mode Arena") && Has("Mode Daily") && Has("Mode Shift") &&
                Has("Mode Operations") && Has("Solar Transit") && Has("Mode Lesson"));
            yield return Layouts("modes");

            // ---- Duel ----
            Submit("Mode Duel");
            yield return null;
            string rival = Caption("Option Rival");
            Submit("Option Rival");
            Check("Rival option cycles", Caption("Option Rival") != rival, Caption("Option Rival"));
            Submit("Option Rival"); Submit("Option Rival");
            Check("Duel setup offers rival, difficulty, map", Has("Option Rival") && Has("Option Difficulty") && Has("Option Map") && !Has("Option Rule"));
            yield return Layouts("setup-duel");
            Submit("Enter Mode");
            yield return null;
            Check("Duel starts with a countdown", solo.Phase == "countdown" && solo.Match != null && !solo.Match.Arena);
            yield return WaitFor(() => solo.Phase == "running", 4, "Countdown launches the heat");
            double z0 = Player.Z;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Auto Drive moves the cycle north", Player.Z < z0 - 2, "z " + z0.ToString("0.0") + " -> " + Player.Z.ToString("0.0"));
            int dir = Player.Dir;
            Tap("Right");
            yield return new WaitForSecondsRealtime(.25f);
            Check("Touch RIGHT turns relative to the rider", Player.Dir == (dir + 1) % 4, "dir " + dir + " -> " + Player.Dir);
            var cycle = GameObject.Find("Player cycle").transform;
            Check("Rendered heading follows the rider", Mathf.Abs(Mathf.DeltaAngle(cycle.eulerAngles.y, Player.Dir * 90)) < 1);
            Pointer("Fire", true);
            yield return new WaitForSecondsRealtime(.35f);
            Pointer("Fire", false);
            Check("Holding FIRE spends mounted ammo", Player.Ammo < GridRules.ROUND_AMMO, "ammo " + Player.Ammo);
            Submit("Light Jet");
            Check("JET lifts the cycle", Player.FlightKind == "jet");
            yield return Layouts("duel-hud");
            Submit("Pause");
            yield return null;
            double paused = Player.X + Player.Z;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Pause freezes the heat", solo.Phase == "paused" && Math.Abs(Player.X + Player.Z - paused) < 1e-9);
            Submit("Resume");
            Submit("Auto Drive");
            yield return new WaitForSecondsRealtime(.2f);
            double manual = Player.X + Player.Z;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Manual drive waits for FORWARD", Math.Abs(Player.X + Player.Z - manual) < 1e-9);
            Pointer("Forward", true);
            yield return new WaitForSecondsRealtime(.3f);
            Pointer("Forward", false);
            Check("Held FORWARD moves the cycle", Math.Abs(Player.X + Player.Z - manual) > 1);
            Submit("Auto Drive");
            yield return WaitFor(() => solo.Phase == "replay" || solo.Phase == "result" || solo.Phase == "finishing", 25, "Heat ends with a crash or win");
            if (solo.Phase == "replay")
            {
                Check("Lost heat plays a crash replay", Has("Skip Replay"));
                yield return Shot("replay-desktop.png", 1280, 720);
                Submit("Skip Replay");
            }
            yield return WaitFor(() => solo.Phase == "result", 2, "Heat result is shown");
            Check("Result explains the heat", (Label("Result details")?.text ?? "").Contains("Heat +"), Label("Result details")?.text);
            Check("Duel score is recorded", solo.Match.ResultRecorded && solo.Match.PlayerWins + solo.Match.RivalWins >= 0);
            yield return Layouts("result");
            Submit("Result Primary");
            yield return null;
            Check("Next round starts round 2", solo.Match.RoundNumber == 2 && solo.Phase == "countdown");
            yield return WaitFor(() => solo.Phase == "running", 3, "Round 2 launches after a short countdown");
            yield return ChangeMode();

            // ---- Arena ----
            Submit("Mode Arena");
            yield return null;
            Check("Arena setup offers wall rule", Has("Option Rule") && !Has("Option Rival"));
            if (!Caption("Option Rule").Contains("FADING")) Submit("Option Rule");
            Check("Fading walls rule selectable", Caption("Option Rule").Contains("FADING"));
            Submit("Enter Mode");
            yield return WaitFor(() => solo.Phase == "running", 4, "Arena heat launches");
            Check("Arena fields three AI rivals", GameObject.Find("Rider AXIOM") != null && GameObject.Find("Rider HELIX") != null && GameObject.Find("Rider PULSE") != null);
            Check("Arena uses the fading wall rule", solo.Round.Rule == "decay" && solo.Match.Arena);
            yield return Shot("arena-desktop.png", 1280, 720);
            yield return WaitFor(() => solo.Phase == "replay" || solo.Phase == "result" || solo.Phase == "finishing", 40, "Arena heat ends");
            if (solo.Phase == "replay") Submit("Skip Replay");
            yield return WaitFor(() => solo.Phase == "result", 2, "Arena heat result is shown");
            Check("Arena result reports heats survived", (Label("Result details")?.text ?? "").Contains("heats survived"));
            Submit("Result Change Mode");
            yield return null;

            // ---- Daily Signal ----
            Submit("Mode Daily");
            yield return null;
            var daily = SimMatches.DailyFor(DateTime.UtcNow);
            Check("Daily setup shows today's seed", (Label("Setup info")?.text ?? "").Contains(daily.Seed.ToString()));
            Submit("Enter Mode");
            yield return null;
            Check("Daily uses the fixed seed, map, rival, and Standard", solo.Daily != null && solo.Match.Seed == daily.Seed &&
                solo.Round.Map == daily.Map && solo.Match.Rival == daily.Rival && solo.Round.Difficulty == "standard");
            yield return WaitFor(() => solo.Phase == "running", 4, "Daily heat launches");
            yield return ChangeMode();

            // ---- Grid Shift ----
            Submit("Mode Shift");
            yield return null;
            Submit("Enter Mode");
            yield return null;
            Check("Grid Shift opens its briefing", solo.Phase == "brief" && Has("Launch"));
            Submit("Launch");
            yield return WaitFor(() => solo.Phase == "running", 4, "Grid Shift launches");
            Check("Echo rides against you", GameObject.Find("Rider ECHO") != null && solo.Round.Shift != null);
            yield return new WaitForSecondsRealtime(.3f);
            Submit("Form");
            yield return new WaitForSecondsRealtime(.5f);
            Check("FORM dismounts into the on-foot form", Player.Form == "foot");
            Check("On-foot pad replaces steering", Has("Move Up") && Has("Disc") && !Has("Left"));
            double footX = Player.X;
            Pointer("Move Right", true);
            yield return new WaitForSecondsRealtime(.5f);
            Pointer("Move Right", false);
            Check("Pad moves the rider east", Player.X > footX + 1, "x " + footX.ToString("0.0") + " -> " + Player.X.ToString("0.0"));
            yield return Layouts("shift-foot");
            Submit("Disc");
            Check("DISC throws a returning disc", solo.Round.Shift.Discs.Exists(d => d.OwnerId == "YOU"));
            yield return WaitFor(() => solo.Phase != "running" || !solo.Round.Shift.Discs.Exists(d => d.OwnerId == "YOU"), 4, "Disc returns to its owner");
            if (solo.Phase == "running")
            {
                yield return new WaitForSecondsRealtime(.4f);
                Submit("Form");
                yield return new WaitForSecondsRealtime(.2f);
                Check("FORM remounts in clear space", Player.Form == "cycle" || !Player.Alive);
            }
            else Check("Grid Shift round resolved before remount", solo.Phase == "result");
            yield return ChangeMode();

            // ---- Operations: Portal Run with the browser's winning route ----
            Submit("Mode Operations");
            yield return null;
            while (!Caption("Option Operation").Contains("PORTAL")) Submit("Option Operation");
            Submit("Enter Mode");
            yield return null;
            Check("Portal Run opens its briefing", solo.Phase == "brief" && (Label("Brief title")?.text ?? "") == "PORTAL RUN");
            yield return Shot("brief-desktop.png", 1280, 720);
            Submit("Launch");
            yield return WaitFor(() => solo.Phase == "running", 4, "Portal Run launches");
            Check("Gate set pieces are built", GameObject.Find("Gate shutter") != null && GameObject.Find("WEST REACTOR") != null);
            // The browser-recorded winning route turns at ticks 158 and 384 (about ±20 ticks of slack).
            yield return WaitFor(() => solo.Round.Tick >= 158 || solo.Phase != "running", 3, "Route reaches the first turn");
            solo.Turn(1);
            yield return WaitFor(() => solo.Round.Tick >= 384 || solo.Phase != "running", 4, "Route reaches the second turn");
            solo.Turn(-1);
            yield return WaitFor(() => solo.Phase == "result", 6, "Portal Run finishes");
            Check("Portal Run route wins all three gates", (Label("Result eyebrow")?.text ?? "") == "OPERATION COMPLETE" && solo.Round.Operation.Progress == 3,
                Label("Result details")?.text);
            Check("Portal Run unlocks the amber trail look", SoloProfile.Operation("portal").completed && SoloProfile.LookUnlocked("amber"));
            Submit("Result Change Mode");
            yield return null;

            // System Sweep marks a lane before the beam fires.
            Submit("Mode Operations");
            yield return null;
            while (!Caption("Option Operation").Contains("SWEEP")) Submit("Option Operation");
            Submit("Enter Mode"); yield return null;
            Submit("Launch");
            yield return WaitFor(() => solo.Round.Operation?.Beam != null || solo.Phase != "running" && solo.Phase != "countdown", 7, "Scanner marks a lane before firing");
            Check("Scanner craft and lane mark render", GameObject.Find("Search craft") != null && GameObject.Find("Scanner lane mark") != null);
            yield return Layouts("operation-hud");
            yield return ChangeMode();

            // Arena Collapse warns before each ring.
            Submit("Mode Operations");
            yield return null;
            while (!Caption("Option Operation").Contains("COLLAPSE")) Submit("Option Operation");
            Submit("Enter Mode"); yield return null;
            Submit("Launch");
            yield return WaitFor(() => solo.Round.Operation?.NextLimit != null || solo.Phase != "running" && solo.Phase != "countdown", 7, "Collapse warns before the ring closes");
            yield return Shot("collapse-desktop.png", 1280, 720);
            yield return ChangeMode();

            // Journey links three operations; the Sentinel is optional.
            Submit("Mode Operations");
            yield return null;
            while (!Caption("Option Operation").Contains("JOURNEY")) Submit("Option Operation");
            Check("Journey offers the optional Sentinel", Has("Option Final Rival"));
            if (!Caption("Option Final Rival").Contains("ON")) Submit("Option Final Rival");
            Submit("Enter Mode"); yield return null;
            Check("Journey briefing shows stage 1 of 4", (Label("Brief eyebrow")?.text ?? "").Contains("1/4"));
            Submit("Brief Change Mode"); yield return null;

            // ---- Lesson ----
            Submit("Mode Lesson");
            yield return null;
            Submit("Enter Mode");
            yield return WaitFor(() => solo.Phase == "running", 4, "Lesson launches");
            yield return new WaitForSecondsRealtime(.3f);
            Tap("Right");
            yield return new WaitForSecondsRealtime(.3f);
            Tap("Left");
            yield return new WaitForSecondsRealtime(.2f);
            Check("Lesson advances to step 3 after two turns", (Label("Status")?.text ?? "").StartsWith("STEP 3"), Label("Status")?.text);
            Submit("Auto Drive");
            yield return WaitFor(() => solo.Phase == "result", 8, "Lesson completes after six clear seconds");
            Check("Lesson completion is explained", (Label("Result eyebrow")?.text ?? "") == "LESSON COMPLETE");
            Submit("Main Menu");
            yield return null;

            // ---- Records and settings ----
            Check("Main menu returns", Has("Start Arena"));
            Submit("Records");
            yield return null;
            Check("Records list completed runs", (Label("Runs list")?.text ?? "").Contains("PTS"));
            Check("Grid Archive shows the Portal Run fragment", (Label("Archive list")?.text ?? "").Contains("PORTAL RUN / BEST"));
            yield return Layouts("records");
            Submit("Close Records");
            yield return null;
            Submit("Settings");
            yield return null;
            string look = Caption("Trail Look");
            Submit("Trail Look");
            Check("Unlocked trail look can be selected", Caption("Trail Look") != look, Caption("Trail Look"));
            yield return Layouts("settings");
            while (Caption("Trail Look") != "GRID CYAN") Submit("Trail Look");
            Submit("Close Settings");

            // ---- Solar Transit keeps its dedicated course ----
            Submit("Start Arena"); yield return null;
            Submit("Solar Transit");
            yield return new WaitForSecondsRealtime(1f);
            Check("Solar Transit opens its course", SceneManager.GetActiveScene().name == "PhaseThreeTransit");
            Submit("Offline Arena");
            yield return new WaitForSecondsRealtime(1f);
            Check("Transit returns to the main menu", SceneManager.GetActiveScene().name == "PhaseOneArena" && Has("Start Arena"));

            CancelInvoke(nameof(Timeout));
            Save();
            Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
