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
    // Opt-in end-to-end flow in a real desktop player; evidence stays in an ignored directory.
    public sealed class DevelopmentTransitCapture : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult { public string name; public bool passed; }
        [Serializable] private sealed class Capture { public string file; public int width, height; }
        [Serializable] private sealed class Report
        {
            public string scope = "Solar Transit desktop player flow and viewports; Android device validation is separate";
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
            int index = Array.IndexOf(args, "--phase3-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Phase 3 verification").AddComponent<DevelopmentTransitCapture>();
            DontDestroyOnLoad(runner.gameObject);
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 100);
            runner.StartCoroutine(runner.RunFlow());
        }

        private void Check(string name, bool passed)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed });
            Debug.Log("PHASE3_CHECK " + name + ": " + passed);
            if (!passed) failed = true;
        }
        private void Save() =>
            File.WriteAllText(Path.Combine(output, "transit-verification.json"), JsonUtility.ToJson(report, true));
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
        private void Pointer(string name, bool down)
        {
            var data = new PointerEventData(EventSystem.current) { pointerId = 82 };
            if (down) ExecuteEvents.Execute(Control(name).gameObject, data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(Control(name).gameObject, data, ExecuteEvents.pointerUpHandler);
        }
        private IEnumerator WaitUntil(Func<bool> condition, float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition() && Time.realtimeSinceStartup - start < seconds) yield return null;
        }
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
            var game = FindFirstObjectByType<TransitGame>();
            Check("Transit scene loaded", game != null);
            if (game == null) { Save(); Application.Quit(2); yield break; }
            yield return CaptureLayout("transit-landing-phone.png", 1170, 540);
            Submit("Start Transit");
            Check("Course starts from beginning", game.Run.Outcome == TransitRun.State.Running &&
                Mathf.Abs(game.Run.Z - TransitRun.StartZ) < .1f);
            yield return new WaitForSecondsRealtime(.2f);
            Submit("Pause");
            float stoppedAt = game.Run.Elapsed, stoppedZ = game.Run.Z;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Pause freezes timer and movement", Mathf.Abs(game.Run.Elapsed - stoppedAt) < .01f &&
                Mathf.Abs(game.Run.Z - stoppedZ) < .01f);
            yield return CaptureLayout("transit-pause-desktop.png", 1280, 720);
            Submit("Resume");
            yield return WaitUntil(() => game.Run.Outcome != TransitRun.State.Running, 3);
            Check("Unassisted jet gap causes failure", game.Run.Outcome == TransitRun.State.Crashed &&
                game.Run.Failure.Contains("JET GAP"));
            yield return CaptureLayout("transit-failure-phone.png", 1170, 540);

            Submit("Retry Transit");
            Submit("Auto Drive");
            yield return CaptureLayout("transit-course-desktop.png", 1280, 720);
            yield return CaptureLayout("transit-course-phone.png", 1170, 540);
            float waitingZ = game.Run.Z;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Manual mode stays in place", Mathf.Abs(game.Run.Z - waitingZ) < .01f);
            Pointer("Forward", true);
            yield return new WaitForSecondsRealtime(.28f);
            Pointer("Forward", false);
            Check("Held touch Forward advances", game.Run.Z > waitingZ + 1);
            Submit("Auto Drive");
            yield return WaitUntil(() => game.Run.Z >= -18.5f || game.Run.Outcome != TransitRun.State.Running, 2);
            Submit("Light Jet");
            Check("Light jet launches before amber gap", game.Run.ActiveFlight == TransitRun.Flight.Jet);
            yield return WaitUntil(() => game.Run.GapsCleared >= 1 || game.Run.Outcome != TransitRun.State.Running, 2);
            Check("Jet clears first gap", game.Run.GapsCleared >= 1 &&
                game.Run.Outcome == TransitRun.State.Running);
            yield return WaitUntil(() => game.Run.Z >= -.5f || game.Run.Outcome != TransitRun.State.Running, 2);
            Submit("Solar Sail");
            Check("Solar sail launches before cyan gap", game.Run.ActiveFlight == TransitRun.Flight.Sail);
            yield return WaitUntil(() => game.Run.Outcome != TransitRun.State.Running, 5);
            Check("Sail reaches finish before timeout", game.Run.Outcome == TransitRun.State.Won &&
                game.Run.GapsCleared == 2 && game.Run.Elapsed < TransitRun.TimeLimit);
            Check("Best time saved locally", game.RecordMilliseconds > 0 &&
                game.RecordMilliseconds <= Mathf.RoundToInt(game.Run.Elapsed * 1000) + 5);
            yield return CaptureLayout("transit-success-desktop.png", 1280, 720);
            yield return CaptureLayout("transit-success-phone.png", 1170, 540);

            Submit("Retry Transit");
            Submit("Auto Drive");
            yield return WaitUntil(() => game.Run.Outcome != TransitRun.State.Running, 14);
            Check("Idle course times out", game.Run.Outcome == TransitRun.State.TimedOut &&
                game.Run.Failure.Contains("TIME EXPIRED"));
            Submit("Retry Transit");
            Check("Retry resets timer and checkpoints", game.Run.Outcome == TransitRun.State.Running &&
                game.Run.Elapsed < .1f && game.Run.GapsCleared == 0);
            Submit("Pause");
            Submit("Transit Menu");
            Check("Pause menu returns to landing", GameObject.Find("Transit landing") != null);
            Submit("Offline Arena");
            yield return null;
            Check("Offline Arena remains accessible", SceneManager.GetActiveScene().name == "PhaseOneArena" &&
                FindFirstObjectByType<ArenaGame>() != null);
            Submit("Solar Transit");
            yield return null;
            game = FindFirstObjectByType<TransitGame>();
            Check("Solar Transit reopens from Arena", SceneManager.GetActiveScene().name == "PhaseThreeTransit" &&
                game != null);
            Check("Best time survives scene change", game != null && game.RecordMilliseconds > 0);
            yield return CaptureLayout("transit-return-phone.png", 1170, 540);
            CancelInvoke(nameof(Timeout));
            Save();
            Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
