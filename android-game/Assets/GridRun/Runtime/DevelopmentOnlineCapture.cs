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
    // Two real Unity players run against one local Node server. Opt in with command-line flags.
    public sealed class DevelopmentOnlineCapture : MonoBehaviour
    {
        [Serializable] private sealed class CheckResult { public string name; public bool passed; }
        [Serializable] private sealed class Capture { public string file; public int width, height; }
        [Serializable] private sealed class Report
        {
            public string role, engine, platform;
            public string scope = "Two desktop Unity clients against local authoritative Node server; not Android network evidence";
            public List<CheckResult> checks = new List<CheckResult>();
            public List<Capture> captures = new List<Capture>();
        }
        private readonly Report report = new Report();
        private string output, role, server;
        private OnlineRoomClient room;
        private bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--phase2-capture-dir");
            if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("Phase 2 verification").AddComponent<DevelopmentOnlineCapture>();
            DontDestroyOnLoad(runner.gameObject);
            runner.output = Path.GetFullPath(args[index + 1]);
            runner.role = Option(args, "--phase2-role") ?? "host";
            runner.server = Option(args, "--phase2-origin") ?? "http://127.0.0.1:8096";
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            runner.Invoke(nameof(Timeout), 115);
            runner.StartCoroutine(runner.Run());
        }
        private static string Option(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        private void Check(string name, bool passed)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed });
            if (!passed) failed = true;
            Debug.Log("PHASE2_CHECK " + role + " " + name + ": " + passed);
        }
        private void Save()
        {
            File.WriteAllText(Path.Combine(output, role + "-verification.json"), JsonUtility.ToJson(report, true));
        }
        private void Timeout() { Check("Flow completed before timeout", false); Save(); Application.Quit(2); }
        private GameObject Control(string name)
        {
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button.gameObject;
            throw new InvalidOperationException("Missing active control: " + name);
        }
        private InputField Field(string name)
        {
            foreach (var field in FindObjectsByType<InputField>(FindObjectsSortMode.None))
                if (field.name == name) return field;
            throw new InvalidOperationException("Missing input: " + name);
        }
        private void Submit(string name) => ExecuteEvents.Execute(Control(name), new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        private IEnumerator WaitFor(Func<bool> ready, float timeout, string name)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < until) yield return null;
            Check(name, ready());
        }
        private static bool HasVisiblePixels(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= 1000) return false;
            var texture = new Texture2D(2, 2);
            bool visible = false;
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) return false;
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i += 97)
                    if (pixels[i].r > 25 || pixels[i].g > 25 || pixels[i].b > 25) { visible = true; break; }
                return visible;
            }
            finally { Destroy(texture); }
        }
        private bool ControlsFit()
        {
            bool fits = true;
            foreach (var button in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                var corners = new Vector3[4];
                ((RectTransform)button.transform).GetWorldCorners(corners);
                foreach (var point in corners)
                    fits &= point.x >= -1 && point.y >= -1 && point.x <= Screen.width + 1 && point.y <= Screen.height + 1;
            }
            return fits;
        }
        private IEnumerator CaptureLayout(string file, int width, int height)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(1);
            string path = Path.Combine(output, role + "-" + file);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1);
            Check(file + " contains visible UI", HasVisiblePixels(path));
            Check(file + " controls fit viewport", ControlsFit());
            report.captures.Add(new Capture { file = role + "-" + file, width = Screen.width, height = Screen.height });
        }

        private IEnumerator Run()
        {
            report.role = role; report.engine = Application.unityVersion; report.platform = Application.platform.ToString();
            yield return new WaitForSecondsRealtime(.5f);
            room = FindFirstObjectByType<OnlineRoomClient>();
            Check("Online client loaded", room != null);
            Field("Server origin").text = server;
            Field("Rider name").text = role == "host" ? "HOST" : "FRIEND";
            string codePath = Path.Combine(output, "room-code.txt");
            if (role == "host")
            {
                yield return CaptureLayout("setup-phone.png", 1170, 540);
                Submit("Create Room");
                yield return WaitFor(() => !string.IsNullOrEmpty(room.Code), 10, "Host created a room");
                if (string.IsNullOrEmpty(room.Code)) { Save(); Application.Quit(2); yield break; }
                Check("Host received six digits", System.Text.RegularExpressions.Regex.IsMatch(room.Code, "^[0-9]{6}$"));
                File.WriteAllText(codePath, room.Code);
                yield return WaitFor(() => room.View?.phase == "lobby", 10, "Host received lobby snapshot");
                yield return WaitFor(() => room.View?.players?.Length == 2 && room.View.players[1].connected, 20, "Friend joined the lobby");
                yield return CaptureLayout("lobby-desktop.png", 1280, 720);
                yield return CaptureLayout("lobby-phone.png", 1170, 540);
                Submit("Start Heat");
            }
            else
            {
                Field("Room code").text = "123";
                Submit("Join Room");
                yield return WaitFor(() => room.Status.Contains("six-digit"), 3, "Invalid code explained");
                yield return WaitFor(() => File.Exists(codePath), 20, "Host shared room code");
                if (!File.Exists(codePath)) { Save(); Application.Quit(2); yield break; }
                Field("Room code").text = File.ReadAllText(codePath).Trim();
                Submit("Join Room");
                yield return WaitFor(() => room.View?.phase == "lobby", 15, "Friend joined via six-digit code");
                string seatId = room.SelfId;
                room.BreakStreamForVerification();
                yield return WaitFor(() => !room.Connected, 5, "Guest stream interruption detected");
                yield return WaitFor(() => room.Connected && room.View?.selfId == seatId, 12, "Guest reconnected to the same seat");
                // A second hidden Unity window can render black on this Windows host.
                // Guest controls and network state are checked; host captures both viewports.
                Check("Guest lobby controls fit viewport", ControlsFit());
            }
            yield return WaitFor(() => room.View?.phase == "running", 15, "Arena heat started on both clients");
            if (room.View?.phase != "running") { Save(); Application.Quit(2); yield break; }
            int firstTick = room.View.round?.tick ?? -1;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Server tick advances", (room.View.round?.tick ?? -1) > firstTick);
            Check("Server streams two riders", room.View.round?.riders?.Length == 2);
            Check("Unity renders both riders", GameObject.Find("Online rider P1") != null && GameObject.Find("Online rider P2") != null);
            if (role == "host")
            {
                Submit("Host Pause");
                yield return WaitFor(() => room.View?.phase == "paused", 5, "Host pause stops the heat");
                yield return WaitFor(() => GameObject.Find("Start Heat") != null, 5, "Resume control is visible");
                Submit("Start Heat");
                yield return WaitFor(() => room.View?.phase == "running", 5, "Host resume restarts the heat");
                yield return CaptureLayout("arena-desktop.png", 1280, 720);
                yield return CaptureLayout("arena-phone.png", 1170, 540);
            }
            else Check("Guest Arena controls fit viewport", ControlsFit());
            yield return WaitFor(() => room.View?.phase == "round-result" || room.View?.phase == "match-result", 35, "Authoritative heat result arrived");
            Check("Result includes crash cause", room.View?.result?.crashes?.Length > 0);
            if (role == "host")
            {
                yield return CaptureLayout("result-phone.png", 1170, 540);
                Submit("Start Heat");
                yield return WaitFor(() => room.View?.roundNumber == 2, 10, "Host started next heat");
                yield return WaitFor(() => room.View?.phase == "connection-wait", 20, "Departure pauses the server heat");
                // Let the presentation component apply the fresh snapshot this frame.
                yield return null;
                Submit("Leave Room");
                yield return WaitFor(() => room.Code == null, 5, "Host left room cleanly");
                yield return WaitFor(() => GameObject.Find("Online setup") != null, 5, "Online setup reopened after leaving");
                Submit("Offline Arena");
                yield return WaitFor(() => FindFirstObjectByType<ArenaGame>() != null, 5, "Offline Arena remains available");
                yield return CaptureLayout("offline-landing-phone.png", 1170, 540);
                Submit("Online Room");
                yield return WaitFor(() => FindFirstObjectByType<OnlineRoomClient>() != null, 5, "Online Room can reopen from offline landing");
            }
            else
            {
                yield return WaitFor(() => room.View?.roundNumber == 2, 15, "Friend sees next heat");
                Submit("Leave Room");
                yield return WaitFor(() => room.Code == null, 5, "Friend left room cleanly");
            }
            CancelInvoke(nameof(Timeout)); Save(); Application.Quit(failed ? 2 : 0);
        }
    }
}
#endif
