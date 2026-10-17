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
    // Opt-in player-flow verification. No effect without the explicit command-line flag.
    public sealed class DevelopmentCapture : MonoBehaviour
    {
        [Serializable] private sealed class Result { public string name; public bool passed; }
        [Serializable] private sealed class Capture { public string file; public int width; public int height; }
        [Serializable] private sealed class Report
        {
            public string engine;
            public string platform;
            public string scope = "Desktop UI flow verification; not physical Android performance evidence";
            public List<Result> checks = new List<Result>();
            public List<Capture> captures = new List<Capture>();
        }
        private string output;
        private readonly Report report = new Report();
        private bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartCaptureIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--phase0-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Development verification").AddComponent<DevelopmentCapture>();
            runner.output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 90);
            runner.StartCoroutine(runner.Run());
        }

        private void Timeout()
        {
            Check("Verification completed before timeout", false);
            File.WriteAllText(Path.Combine(output, "desktop-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(2);
        }

        private void Check(string name, bool passed)
        {
            report.checks.Add(new Result { name = name, passed = passed });
            if (!passed) failed = true;
            Debug.Log("PHASE0_CHECK " + name + ": " + passed);
        }

        private GameObject Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button.gameObject;
            throw new InvalidOperationException("Missing UI control: " + name);
        }
        private void Submit(string name) => ExecuteEvents.Execute(Control(name), new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        private void Pointer(string name, bool down)
        {
            var data = new PointerEventData(EventSystem.current) { pointerId = 77 };
            if (down) ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerDownHandler);
            else ExecuteEvents.Execute(Control(name), data, ExecuteEvents.pointerUpHandler);
        }

        private IEnumerator CaptureLayout(string filename, int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1);
            string path = Path.Combine(output, filename);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1);
            Check(filename + " saved", File.Exists(path) && new FileInfo(path).Length > 1000);
            bool fits = true;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var point in corners)
                    fits &= point.x >= -1 && point.y >= -1 && point.x <= Screen.width + 1 && point.y <= Screen.height + 1;
            }
            Check(filename + " controls fit viewport", fits);
            report.captures.Add(new Capture { file = filename, width = Screen.width, height = Screen.height });
        }

        private IEnumerator Run()
        {
            report.engine = Application.unityVersion;
            report.platform = Application.platform.ToString();
            yield return new WaitForSecondsRealtime(1);
            Submit("RESTART");
            // Let LateUpdate copy reset simulation state into the rendered cycle.
            yield return new WaitForSecondsRealtime(.1f);
            var cycle = GameObject.Find("Player cycle").transform;
            Vector3 before = cycle.position;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Auto Drive moves forward", cycle.position.z > before.z + 1);
            Submit("PAUSE");
            yield return null;
            before = cycle.position;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Pause holds position", Vector3.Distance(cycle.position, before) < .01f);
            Submit("PAUSE");
            Submit("AUTO DRIVE: ON");
            yield return null;
            before = cycle.position;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Manual drive stops without forward", Vector3.Distance(cycle.position, before) < .01f);
            Pointer("HOLD FORWARD", true);
            yield return new WaitForSecondsRealtime(.3f);
            Check("Held forward moves", cycle.position.z > before.z + 1);
            Pointer("HOLD FORWARD", false);
            yield return null;
            before = cycle.position;
            yield return new WaitForSecondsRealtime(.3f);
            Check("Releasing forward stops", Vector3.Distance(cycle.position, before) < .01f);
            Pointer("LEFT", true); Pointer("LEFT", false);
            yield return new WaitForSecondsRealtime(.2f);
            Check("Touch left turns relative to rider", Mathf.Abs(Mathf.DeltaAngle(cycle.eulerAngles.y, 270)) < 1);
            Submit("RIGHT");
            yield return new WaitForSecondsRealtime(.2f);
            Check("Keyboard submit turns right", Mathf.Abs(Mathf.DeltaAngle(cycle.eulerAngles.y, 0)) < 1);
            yield return CaptureLayout("desktop.png", 1280, 720);
            yield return CaptureLayout("phone-landscape.png", 1170, 540);
            yield return CaptureLayout("tablet.png", 1024, 768);
            Submit("TARGET: 60 FPS");
            Check("30 FPS option sets target", Application.targetFrameRate == 30);
            Submit("TARGET: 60 FPS");
            Check("60 FPS option sets target", Application.targetFrameRate == 60);
            Submit("AUTO DRIVE: ON");
            Submit("RESTART");
            yield return new WaitForSecondsRealtime(6.3f);
            Check("Arena boundary stops rider", Mathf.Abs(cycle.position.z - ProbeSimulation.ArenaLimit) < .01f);
            Submit("RESTART");
            yield return null;
            Check("Restart recovers from boundary hit", cycle.position.z < -18);
            CancelInvoke(nameof(Timeout));
            File.WriteAllText(Path.Combine(output, "desktop-verification.json"), JsonUtility.ToJson(report, true));
            Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
