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
        private int seats = 2;
        private string map = "classic", rule = "classic";
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
            if (int.TryParse(Option(args, "--phase2-seats"), out int seats)) runner.seats = seats;
            runner.map = Option(args, "--phase2-map") ?? "classic";
            runner.rule = Option(args, "--phase2-rule") ?? "classic";
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

        // Each map has a landmark obstacle that SoloWorld names by its id; Classic Grid has none.
        private static bool MapShown(string id)
        {
            bool solar = GameObject.Find("WEST REACTOR") != null, vault = GameObject.Find("VAULT CORE") != null;
            return id == "solar" ? solar && !vault : id == "vault" ? vault && !solar : !solar && !vault;
        }
        private IEnumerator ChooseMap(string next)
        {
            Submit("Room Map");
            yield return WaitFor(() => room.View?.map == next, 5, "Host chose " + next);
            // The map rebuilds in LateUpdate and the old one is destroyed at the end of that frame.
            yield return WaitFor(() => MapShown(next), 3, "Lobby backdrop shows " + next);
            Check("Map button names " + next, Control("Room Map").GetComponentInChildren<Text>().text ==
                "MAP: " + Sim.SimMaps.Get(next).Name.ToUpperInvariant());
        }

        // Records, after each rendered frame, whether snapshot bullets and pickups were actually drawn.
        private bool sawBullet, sawPickup, sawSpawnToast;
        private IEnumerator WatchCombat()
        {
            while (room.View != null && room.View.phase != "round-result" && room.View.phase != "match-result")
            {
                yield return new WaitForEndOfFrame();
                var round = room.View?.round;
                if (round == null) continue;
                if (round.bullets?.Length > 0 && (GameObject.Find("Bullet") != null || GameObject.Find("Rocket") != null)) sawBullet = true;
                foreach (var p in round.pickups ?? new OnlineRoomClient.PickupView[0])
                    if (p.active && GameObject.Find("Pickup " + p.type) != null) sawPickup = true;
                if ((GameObject.Find("Online toast")?.GetComponent<Text>()?.text ?? "").EndsWith(" SPAWNED")) sawSpawnToast = true;
            }
        }

        // Holding FIRE through the real control spends server ammo and updates the HUD.
        private IEnumerator CheckFire()
        {
            string selfId = room.View.selfId;
            int Ammo() => Array.Find(room.View?.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == selfId)?.ammo ?? -1;
            int before = Ammo();
            var control = Control("Fire");
            var press = new PointerEventData(EventSystem.current) { pointerId = 7 };
            ExecuteEvents.Execute(control, press, ExecuteEvents.pointerDownHandler);
            yield return WaitFor(() => Ammo() >= 0 && Ammo() < before, 3, "FIRE spends ammo on the server");
            ExecuteEvents.Execute(control, press, ExecuteEvents.pointerUpHandler);
            // Snapshots keep arriving, so wait for the labels to catch up with the newest ammo count.
            yield return WaitFor(() => control.GetComponentInChildren<Text>().text == "FIRE " + Ammo(), 1, "FIRE button shows the ammo left");
            yield return WaitFor(() => (GameObject.Find("Online phase")?.GetComponent<Text>()?.text ?? "").Contains("GUN " + Ammo() + "/48"), 1, "HUD shows the gun");
        }

        // The rider must travel the way its model faces, and LEFT must turn it left on screen.
        private IEnumerator CheckTurnReadsOnScreen()
        {
            string selfId = room.View.selfId;
            int Dir() => Array.Find(room.View?.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == selfId)?.dir ?? -1;
            var self = GameObject.Find("Player cycle")?.transform;
            Check("Own rider rendered", self != null);
            if (self == null) yield break;
            Vector3 start = self.position, before = self.forward;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Rider travels the way it faces", Vector3.Dot(self.position - start, before) > 0);
            int dir = Dir();
            Submit("Left");
            yield return WaitFor(() => Dir() != dir, 3, "Server applied the LEFT turn");
            // SoloWorld redraws in LateUpdate, after this coroutine resumes.
            yield return WaitFor(() => Vector3.Angle(before, self.forward) > 45, 1, "Rendered rider turned");
            Vector3 after = self.forward;
            Check("LEFT turns the rider left on screen", Vector3.Cross(before, after).y < -.5f);
            start = self.position;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Rider travels the way it faces after the turn", Vector3.Dot(self.position - start, after) > 0);
            // The segment closed by the turn is still a wall; the old online renderer hid it.
            var own = Array.FindAll(room.View.round.segments, s => s.ownerId == selfId && Mathf.Abs(s.x2 - s.x1) + Mathf.Abs(s.z2 - s.z1) > .06f);
            int drawn = Array.FindAll(FindObjectsByType<Transform>(FindObjectsSortMode.None), t => t.name == "Light trail").Length;
            Check("Trail from before the turn stays visible", own.Length >= 2 && Array.Exists(own, s => !s.active) && drawn >= own.Length);
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
                var sizeLabel = Control("Room Size").GetComponentInChildren<Text>();
                for (int i = 0; i < 3 && sizeLabel.text != "RIDERS: " + seats; i++) Submit("Room Size");
                Check("Room size shows " + seats + " riders", sizeLabel.text == "RIDERS: " + seats);
                yield return CaptureLayout("setup-phone.png", 1170, 540);
                Submit("Create Room");
                yield return WaitFor(() => !string.IsNullOrEmpty(room.Code), 10, "Host created a room");
                if (string.IsNullOrEmpty(room.Code)) { Save(); Application.Quit(2); yield break; }
                Check("Host received six digits", System.Text.RegularExpressions.Regex.IsMatch(room.Code, "^[0-9]{6}$"));
                // Write then rename, so guests polling for the file never read it half-written.
                File.WriteAllText(codePath + ".tmp", room.Code);
                File.Move(codePath + ".tmp", codePath);
                yield return WaitFor(() => room.View?.phase == "lobby", 10, "Host received lobby snapshot");
                Check("Server room has " + seats + " seats", room.View?.maxPlayers == seats);
                yield return WaitFor(() => room.View?.players?.Length == seats && Array.TrueForAll(room.View.players, p => p.connected), 30, "Every seat joined the lobby");
                // Cycle every map once (the backdrop must follow), then settle on the requested map and walls.
                foreach (string next in new[] { "solar", "vault", "classic" }) yield return ChooseMap(next);
                foreach (string next in map == "vault" ? new[] { "solar", "vault" } : map == "solar" ? new[] { "solar" } : new string[0])
                    yield return ChooseMap(next);
                if (rule == "decay")
                {
                    Submit("Room Walls");
                    yield return WaitFor(() => room.View?.rule == "decay", 5, "Host chose fading walls");
                    yield return null;
                    Check("Walls button names fading walls", Control("Room Walls").GetComponentInChildren<Text>().text == "WALLS: FADING WALLS");
                }
                Check("Room settled on " + map + " with " + rule + " walls", room.View?.map == map && room.View?.rule == rule);
                yield return CaptureLayout("lobby-desktop.png", 1280, 720);
                yield return CaptureLayout("lobby-phone.png", 1170, 540);
                Submit("Start Heat");
            }
            else if (role != "guest")
            {
                // Extra seats join directly. guest2 also leaves the lobby and rejoins, which reopens a
                // seat in the middle of the list; the host can only start if seat ids stay unique.
                yield return WaitFor(() => File.Exists(codePath), 20, role + " found the room code");
                if (!File.Exists(codePath)) { Save(); Application.Quit(2); yield break; }
                Field("Room code").text = File.ReadAllText(codePath).Trim();
                Submit("Join Room");
                yield return WaitFor(() => room.View?.phase == "lobby", 15, role + " joined via six-digit code");
                if (role == "guest2")
                {
                    // The lobby panel appears once the presentation component applies the snapshot.
                    yield return WaitFor(() => GameObject.Find("Leave Room") != null, 3, "guest2 sees the lobby");
                    Submit("Leave Room");
                    yield return WaitFor(() => room.Code == null, 5, "guest2 left the lobby");
                    yield return new WaitForSecondsRealtime(.5f);
                    Submit("Join Room");
                    yield return WaitFor(() => room.View?.phase == "lobby", 15, "guest2 rejoined the lobby");
                }
                Check(role + " lobby controls fit viewport", ControlsFit());
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
            // The host may spend a while choosing maps and walls in the lobby first.
            yield return WaitFor(() => room.View?.phase == "running", 45, "Arena heat started on every client");
            if (room.View?.phase != "running") { Save(); Application.Quit(2); yield break; }
            int firstTick = room.View.round?.tick ?? -1;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Server tick advances", (room.View.round?.tick ?? -1) > firstTick);
            var streamed = room.View.round?.riders ?? new OnlineRoomClient.RiderView[0];
            Check("Server streams " + seats + " riders", streamed.Length == seats);
            // SoloWorld names the own seat "Player cycle" and every other seat "Rider <id>".
            Check("Unity renders every rider", Array.TrueForAll(streamed, r =>
                GameObject.Find(r.id == room.View.selfId ? "Player cycle" : "Rider " + r.id) != null));
            Check("Heat runs on " + map + " with " + rule + " walls", room.View.round?.map == map && room.View.round?.rule == rule &&
                (rule == "decay" ? room.View.round.trailLifetime == 10 : room.View.round.trailLifetime == 0));
            Check("Arena shows the " + map + " map", MapShown(map));
            var colours = new HashSet<string>();
            foreach (var rider in streamed) colours.Add(rider.color == null ? "" : string.Join(",", rider.color));
            Check("Each seat has its own colour", colours.Count == seats && !colours.Contains(""));
            StartCoroutine(WatchCombat());
            // Solar Circuit and Data Vault heats can end within two seconds on Auto Drive (spawns face
            // obstacles or each other), so the timed turn and pause checks run on Classic Grid only.
            if (role == "host" && map != "classic")
            {
                yield return CaptureLayout("arena-desktop.png", 1280, 720);
                yield return CaptureLayout("arena-phone.png", 1170, 540);
            }
            else if (role == "host")
            {
                yield return CheckTurnReadsOnScreen();
                yield return CheckFire();
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
            // Classic Grid heats outlast the first pickup (4 s) and include the host's gunfire.
            if (map == "classic")
            {
                Check("Bullets were drawn", sawBullet);
                Check("A spawned pickup was drawn", sawPickup);
                Check("Pickup spawn was announced", sawSpawnToast);
            }
            yield return null;
            string roster = GameObject.Find("Room roster")?.GetComponent<Text>()?.text ?? "";
            Check("Result names the winner or a draw", (roster.Contains("WINS THE HEAT") || roster.Contains("HEAT DRAW")) && !roster.Contains("\n WINS"));
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
