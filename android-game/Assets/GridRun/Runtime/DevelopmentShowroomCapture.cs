#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // Opt-in player-flow capture; it makes no simulated purchase or entitlement grant.
    public sealed class DevelopmentShowroomCapture : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult { public string name; public bool passed; }
        [Serializable] private sealed class Capture { public string file; public int width, height; }
        [Serializable] private sealed class Report
        {
            public string scope = "Cosmetic showroom desktop player flow; no purchases or Android device validation";
            public string engine, platform;
            public List<CheckResult> checks = new List<CheckResult>();
            public List<Capture> captures = new List<Capture>();
        }
        private readonly Report report = new Report();
        private string output;
        private bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--phase4-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Phase 4 verification").AddComponent<DevelopmentShowroomCapture>();
            DontDestroyOnLoad(runner.gameObject);
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 80);
            runner.StartCoroutine(runner.RunFlow());
        }

        private void Check(string name, bool passed)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed });
            Debug.Log("PHASE4_CHECK " + name + ": " + passed);
            if (!passed) failed = true;
        }

        private void Save() =>
            File.WriteAllText(Path.Combine(output, "showroom-verification.json"), JsonUtility.ToJson(report, true));
        private void Timeout()
        {
            Check("Flow completed before timeout", false);
            Save();
            Application.Quit(2);
        }
        private Button Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button;
            throw new InvalidOperationException("Missing active control: " + name);
        }
        private void Submit(string name) =>
            ExecuteEvents.Execute(Control(name).gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);

        private IEnumerator CaptureLayout(string file, int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.6f);
            string path = Path.Combine(output, file);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(.8f);
            Check(file + " saved", File.Exists(path) && new FileInfo(path).Length > 1000);
            bool fits = true;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                    fits &= corner.x >= -1 && corner.y >= -1 &&
                        corner.x <= Screen.width + 1 && corner.y <= Screen.height + 1;
            }
            Check(file + " controls fit viewport", fits);
            report.captures.Add(new Capture { file = file, width = Screen.width, height = Screen.height });
        }

        private IEnumerator RunFlow()
        {
            report.engine = Application.unityVersion;
            report.platform = Application.platform.ToString();
            yield return new WaitForSecondsRealtime(.4f);
            var game = FindFirstObjectByType<CosmeticShowroom>();
            Check("Showroom scene loaded", game != null);
            if (game == null) { Save(); Application.Quit(2); yield break; }
            Check("Catalog starts with included cycle", game.CurrentEntry?.Id == "cycle.grid.v1" &&
                game.CurrentEntry.Included);
            bool hasPurchaseAction = false;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name.ToLowerInvariant().Contains("buy") ||
                    button.name.ToLowerInvariant().Contains("purchase")) hasPurchaseAction = true;
            Check("No purchase action is offered", !hasPurchaseAction);
            yield return CaptureLayout("showroom-cycle-phone.png", 1170, 540);
            yield return CaptureLayout("showroom-cycle-desktop.png", 1280, 720);
            Submit("Next Style");
            Check("Solar Cycle is preview only", game.CurrentEntry?.Id == "cycle.solar.v1" &&
                !game.CurrentEntry.Included && game.DisplayedBodyTheme == "solar" &&
                game.DisplayedTrailTheme == "grid");
            Check("Preview status is explicit",
                GameObject.Find("Item availability").GetComponent<Text>().text.Contains("UNAVAILABLE TO EQUIP OR BUY"));
            yield return CaptureLayout("showroom-solar-cycle-phone.png", 1170, 540);
            Submit("Trail Styles");
            Check("Trail category starts with included style", game.CurrentEntry?.Id == "trail.grid.v1");
            Submit("Next Style");
            Check("Solar Trail changes only trail", game.CurrentEntry?.Id == "trail.solar.v1" &&
                game.DisplayedBodyTheme == "grid" && game.DisplayedTrailTheme == "solar");
            yield return CaptureLayout("showroom-solar-trail-desktop.png", 1280, 720);
            Submit("Sets");
            Submit("Next Style");
            Check("Solar Set previews both looks", game.CurrentEntry?.Id == "bundle.solar.v1" &&
                game.DisplayedBodyTheme == "solar" && game.DisplayedTrailTheme == "solar");
            yield return CaptureLayout("showroom-solar-set-phone.png", 1170, 540);
            yield return CaptureLayout("showroom-solar-set-tablet.png", 1024, 768);
            Submit("Previous Style");
            Check("Previous returns to included set", game.CurrentEntry?.Id == "bundle.grid.v1" &&
                game.CurrentEntry.Included);
            Submit("Back to Arena");
            yield return null;
            Check("Back returns to offline Arena", SceneManager.GetActiveScene().name == "PhaseOneArena" &&
                FindFirstObjectByType<ArenaGame>() != null);
            Check("Previous modes remain linked", Control("Online Room") != null &&
                Control("Solar Transit") != null);
            Submit("Start Arena");
            yield return new WaitForSecondsRealtime(.25f);
            Check("Arena still begins a match", GameObject.Find("Player cycle") != null &&
                GameObject.Find("Play HUD") != null);
            Submit("Pause");
            Submit("Main Menu");
            Submit("Style Preview");
            yield return null;
            game = FindFirstObjectByType<CosmeticShowroom>();
            Check("Showroom reopens without ownership state",
                SceneManager.GetActiveScene().name == "PhaseFourShowroom" &&
                game != null && game.CurrentEntry?.Id == "cycle.grid.v1");
            yield return CaptureLayout("showroom-return-phone.png", 1170, 540);
            CancelInvoke(nameof(Timeout));
            Save();
            Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
