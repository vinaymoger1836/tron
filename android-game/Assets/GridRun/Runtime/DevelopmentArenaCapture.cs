#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GridRun
{
    // Opt-in end-to-end desktop player capture for the Phase 1 Arena flow.
    public sealed class DevelopmentArenaCapture : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult { public string name; public bool passed; }
        [Serializable] private sealed class Capture { public string file; public int width, height; }
        [Serializable] private sealed class Report
        {
            public string scope = "Desktop Arena flow and viewport verification; physical Android validation is separate";
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
            int index = Array.IndexOf(args, "--phase1-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Phase 1 verification").AddComponent<DevelopmentArenaCapture>();
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 90);
            runner.StartCoroutine(runner.Run());
        }

        private void Check(string name, bool passed)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed });
            Debug.Log("PHASE1_CHECK " + name + ": " + passed);
            if (!passed) failed = true;
        }
        private void Save()
        {
            File.WriteAllText(Path.Combine(output, "arena-verification.json"), JsonUtility.ToJson(report, true));
        }
        private void Timeout() { Check("Flow completed before timeout", false); Save(); Application.Quit(2); }
        private GameObject Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button.gameObject;
            throw new InvalidOperationException("Missing active control: " + name);
        }
        private void Submit(string name) => ExecuteEvents.Execute(Control(name), new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        private void Pointer(string name, bool down)
        {
            var data = new PointerEventData(EventSystem.current) { pointerId = 77 };
            if (down) ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerUpHandler);
        }
        private IEnumerator CaptureLayout(string file, int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1);
            string path = Path.Combine(output, file);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1);
            Check(file + " saved", File.Exists(path) && new FileInfo(path).Length > 1000);
            bool fits = true;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var corner in corners)
                    fits &= corner.x >= -1 && corner.y >= -1 && corner.x <= Screen.width + 1 && corner.y <= Screen.height + 1;
            }
            Check(file + " controls fit viewport", fits);
            report.captures.Add(new Capture { file = file, width = Screen.width, height = Screen.height });
        }
        private IEnumerator Run()
        {
            report.engine = Application.unityVersion;
            report.platform = Application.platform.ToString();
            yield return new WaitForSecondsRealtime(.5f);
            yield return CaptureLayout("landing-phone.png", 1170, 540);
            Submit("Start Arena");
            var player = GameObject.Find("Player cycle").transform;
            Vector3 before = player.position;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Start Arena begins moving", player.position.z > before.z + 1);
            Submit("Pause");
            yield return null; before = player.position;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Pause stops the heat", Vector3.Distance(before, player.position) < .01f);
            Submit("Resume");
            Submit("Auto Drive");
            yield return null; before = player.position;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Manual mode waits for forward", Vector3.Distance(before, player.position) < .01f);
            Pointer("Forward", true);
            yield return new WaitForSecondsRealtime(.3f);
            Check("Held forward moves the rider", player.position.z > before.z + 1);
            Pointer("Forward", false);
            Pointer("Left", true); Pointer("Left", false);
            yield return new WaitForSecondsRealtime(.2f);
            Check("Touch left turns relative to rider", Mathf.Abs(Mathf.DeltaAngle(player.eulerAngles.y, 270)) < 1);
            yield return CaptureLayout("arena-desktop.png", 1280, 720);
            yield return CaptureLayout("arena-phone.png", 1170, 540);
            yield return CaptureLayout("arena-tablet.png", 1024, 768);
            Submit("Auto Drive");
            yield return new WaitForSecondsRealtime(8);
            Check("A crash produces a result", GameObject.Find("Result") != null);
            Submit("Next Heat");
            yield return null;
            Check("Next heat restarts playable Arena", GameObject.Find("Result") == null && player.position.z < -20);
            CancelInvoke(nameof(Timeout)); Save();
            Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
