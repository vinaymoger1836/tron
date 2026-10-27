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
        private string map = "classic", rule = "classic", mode = "arena";
        // interrupt: background drops, a forfeit, and a released seat; close: a silent server, a server
        // restart, and the host closing the room. Null runs the standard match flow.
        private string scenario;
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
            runner.mode = Option(args, "--phase2-mode") ?? "arena";
            runner.rule = Option(args, "--phase2-rule") ?? "classic";
            runner.scenario = Option(args, "--phase2-scenario");
            Directory.CreateDirectory(runner.output);
            Application.runInBackground = true;
            // Scenarios wait out seat holds and a frozen server, so they get longer.
            runner.Invoke(nameof(Timeout), runner.scenario != null ? 170 : 115);
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

        // JET through the real control: the server starts the flight, the rider lifts on screen,
        // lands, and the button counts down its cooldown.
        private IEnumerator CheckJet()
        {
            string selfId = room.View.selfId;
            OnlineRoomClient.RiderView Self() => Array.Find(room.View?.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == selfId);
            var body = GameObject.Find("Player cycle").transform;
            Submit("Light Jet");
            yield return WaitFor(() => Self()?.flightKind == "jet", 3, "JET starts a flight on the server");
            yield return WaitFor(() => body.localPosition.y > .5f, 1, "Rider lifts off on screen");
            yield return WaitFor(() => string.IsNullOrEmpty(Self()?.flightKind) && body.localPosition.y < .05f, 3, "Rider lands");
            var jet = Control("Light Jet");
            yield return WaitFor(() => System.Text.RegularExpressions.Regex.IsMatch(jet.GetComponentInChildren<Text>().text, @"^JET \d+\.\dS$") &&
                !jet.GetComponent<Button>().interactable, 1, "JET shows its cooldown");
            Check("JET sound played", Heard("beep 840"));
        }

        private static string ModeName(string id) => id == "duel" ? "DUEL" : id == "shift" ? "GRID SHIFT" : "ARENA";
        private static bool Has(string name) => GameObject.Find(name) != null;
        private static string TextOf(string name) => GameObject.Find(name)?.GetComponent<Text>()?.text ?? "";

        // Grid Shift through the real controls: FORM dismounts, the pad moves north on screen and in
        // the simulation (-z), DISC throws a disc that is drawn, and the HUD switches to the foot set.
        private IEnumerator CheckShift()
        {
            string selfId = room.View.selfId;
            OnlineRoomClient.RiderView Self() => Array.Find(room.View?.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == selfId);
            Submit("Form");
            yield return WaitFor(() => Self()?.form == "foot", 3, "FORM dismounts on the server");
            yield return WaitFor(() => Has("Move Up") && Has("Disc") && !Has("Left"), 2, "On-foot pad replaces steering");
            yield return WaitFor(() => Has("On-foot rider"), 2, "Rider drawn on foot");
            Check("Dismount sound played", Heard("beep 650"));
            yield return WaitFor(() => TextOf("Shift detail").Contains("YOU 2/2 INTEGRITY  /  ON FOOT"), 2, "Shift panel shows integrity and form");
            var body = GameObject.Find("Player cycle").transform;
            float simZ = Self().z, screenZ = body.position.z;
            var pad = Control("Move Up");
            var press = new PointerEventData(EventSystem.current) { pointerId = 8 };
            ExecuteEvents.Execute(pad, press, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSecondsRealtime(.6f);
            ExecuteEvents.Execute(pad, press, ExecuteEvents.pointerUpHandler);
            Check("UP moves north in the simulation", Self().z < simZ - .5f);
            Check("UP moves up the screen", body.position.z > screenZ + .5f);
            yield return new WaitForSecondsRealtime(.4f);
            var camera = Camera.main.transform;
            Check("Camera is north-up on foot", Vector3.Dot(camera.forward, Vector3.forward) > .5f && camera.position.z < body.position.z);
            Submit("Disc");
            yield return WaitFor(() => Array.Exists(room.View?.round?.shift?.discs ?? new OnlineRoomClient.DiscView[0], d => d.ownerId == selfId), 3, "DISC throws on the server");
            yield return WaitFor(() => Has("Thrown disc"), 1, "Thrown disc drawn");
            yield return WaitFor(() => Control("Disc").GetComponentInChildren<Text>().text == "DISC OUT", 1, "DISC button shows the disc is out");
            Check("Disc throw sound played", Heard("beep 780"));
        }

        private static bool Heard(string cue) => FindFirstObjectByType<Solo.SoloAudio>()?.Heard.Contains(cue) ?? false;

        // Records, after each rendered frame, whether snapshot bullets and pickups were actually drawn.
        private bool sawBullet, sawPickup, sawSpawnToast, heardMusic;
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
                if ((GameObject.Find("Toast")?.GetComponent<Text>()?.text ?? "").EndsWith(" SPAWNED")) sawSpawnToast = true;
                if (room.View?.phase == "running" && (FindFirstObjectByType<Solo.SoloAudio>()?.MusicOn ?? false)) heardMusic = true;
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
            yield return WaitFor(() => (GameObject.Find("Weapon status")?.GetComponent<Text>()?.text ?? "").Contains("GUN " + Ammo() + "/48"), 1, "HUD shows the gun");
            Check("Gunfire sound played", Heard("beep 930"));
        }

        // The rider must travel the way its model faces, and LEFT must turn it left on screen.
        // Snapshots arrive at about 20 Hz. With blending, the drawn rider should move on nearly every
        // rendered frame and never step backwards; without it, it moves on roughly one frame in three at 60 fps.
        private IEnumerator CheckSmoothMotion(Transform rider, float seconds)
        {
            int frames = 0, moved = 0, backwards = 0;
            Vector3 last = rider.position;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForEndOfFrame();
                Vector3 step = rider.position - last;
                last = rider.position;
                frames++;
                if (step.magnitude > .001f) moved++;
                if (Vector3.Dot(step, rider.forward) < -.01f) backwards++;
            }
            Debug.Log("PHASE2_MOTION " + role + " frames " + frames + " moved " + moved + " backwards " + backwards);
            Check("Enough frames to judge motion (" + frames + ")", frames >= seconds * 35);
            Check("Rider moves on most frames between snapshots (" + moved + "/" + frames + ")", moved >= frames * .8f);
            Check("Rider never steps backwards", backwards == 0);
        }

        private IEnumerator CheckTurnReadsOnScreen()
        {
            string selfId = room.View.selfId;
            int Dir() => Array.Find(room.View?.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == selfId)?.dir ?? -1;
            var self = GameObject.Find("Player cycle")?.transform;
            Check("Own rider rendered", self != null);
            if (self == null) yield break;
            Vector3 start = self.position, before = self.forward;
            yield return CheckSmoothMotion(self, .4f);
            Check("Rider travels the way it faces", Vector3.Dot(self.position - start, before) > 0);
            int dir = Dir();
            Submit("Left");
            yield return WaitFor(() => Dir() != dir, 3, "Server applied the LEFT turn");
            // SoloWorld redraws in LateUpdate, after this coroutine resumes.
            yield return WaitFor(() => Vector3.Angle(before, self.forward) > 45, 1, "Rendered rider turned");
            Vector3 after = self.forward;
            Check("LEFT turns the rider left on screen", Vector3.Cross(before, after).y < -.5f);
            Check("Turn sound played", Heard("turn"));
            start = self.position;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Rider travels the way it faces after the turn", Vector3.Dot(self.position - start, after) > 0);
            // The segment closed by the turn is still a wall; the old online renderer hid it.
            var own = Array.FindAll(room.View.round.segments, s => s.ownerId == selfId && Mathf.Abs(s.x2 - s.x1) + Mathf.Abs(s.z2 - s.z1) > .06f);
            int drawn = Array.FindAll(FindObjectsByType<Transform>(FindObjectsSortMode.None), t => t.name == "Light trail").Length;
            Check("Trail from before the turn stays visible", own.Length >= 2 && Array.Exists(own, s => !s.active) && drawn >= own.Length);
        }

        // Calls the scene's own OnApplicationPause, as Android does when the app leaves or returns to the screen.
        private static void Background(bool away) => FindFirstObjectByType<OnlineArenaGame>().SendMessage("OnApplicationPause", away);
        private bool InResult(int round) => room.View?.roundNumber == round && (room.View.phase == "round-result" || room.View.phase == "match-result");
        private bool AllConnected(int count) => room.View?.players?.Length == count && Array.TrueForAll(room.View.players, p => p.connected);
        // Verify-Online.ps1 watches for these files to freeze or restart the server.
        private void Signal(string request) => File.WriteAllText(Path.Combine(output, request + ".request"), role);

        private IEnumerator HostRoom(string codePath, int count)
        {
            var sizeLabel = Control("Room Size").GetComponentInChildren<Text>();
            for (int i = 0; i < 3 && sizeLabel.text != "RIDERS: " + count; i++) Submit("Room Size");
            Submit("Create Room");
            yield return WaitFor(() => !string.IsNullOrEmpty(room.Code), 10, "Host created a room");
            if (string.IsNullOrEmpty(room.Code)) yield break;
            File.WriteAllText(codePath + ".tmp", room.Code);
            File.Move(codePath + ".tmp", codePath);
            yield return WaitFor(() => AllConnected(count), 30, "Every seat joined the lobby");
        }
        // The lobby enables START when it presents a snapshot, which can be a frame after the client applies it.
        private IEnumerator StartWhenReady()
        {
            yield return WaitFor(() => GameObject.Find("Start Heat")?.GetComponent<Button>().interactable ?? false, 3, "START is open");
            Submit("Start Heat");
        }
        private IEnumerator JoinRoom(string codePath)
        {
            yield return WaitFor(() => File.Exists(codePath), 40, role + " found the room code");
            if (!File.Exists(codePath)) yield break;
            Field("Room code").text = File.ReadAllText(codePath).Trim();
            Submit("Join Room");
            yield return WaitFor(() => room.View?.phase == "lobby", 15, role + " joined the room");
        }

        // Three riders, a 10 s hold. FRIEND backgrounds the app during the countdown and returns; THIRD
        // leaves during that hold; in heat 2 FRIEND stays away past the hold and loses the seat.
        private IEnumerator InterruptHost(string codePath)
        {
            yield return HostRoom(codePath, 3);
            yield return StartWhenReady();
            yield return WaitFor(() => room.View?.phase == "connection-wait", 15, "A guest in the background holds the heat for everyone");
            if (room.View?.phase != "connection-wait") yield break;
            int firstWait = room.View.waitMs;
            yield return WaitFor(() => room.View.players.Length == 2, 5, "A rider who leaves during the hold gives up the seat at once");
            Check("Room shrinks to two seats", room.View.maxPlayers == 2);
            Check("The hold continues for the rider still away", room.View.phase == "connection-wait");
            yield return new WaitForSecondsRealtime(1.2f);
            Check("Hold countdown counts down", room.View.phase == "connection-wait" && room.View.waitMs < firstWait);
            yield return null;
            string waiting = TextOf("Room roster");
            Check("Room names who is being waited for", waiting.Contains("Waiting up to") && waiting.Contains("FRIEND to reconnect"));
            yield return CaptureLayout("hold-phone.png", 1170, 540);
            yield return WaitFor(() => room.View?.phase == "countdown" || room.View?.phase == "running", 15, "The heat resumes when the guest returns");
            yield return WaitFor(() => room.View?.phase == "running", 5, "Heat 1 runs");
            int tick = room.View?.round?.tick ?? -1;
            yield return new WaitForSecondsRealtime(.4f);
            Check("Server tick advances after the hold", (room.View?.round?.tick ?? -1) > tick);
            yield return WaitFor(() => InResult(1), 20, "Heat 1 ends");
            yield return null;
            Check("Result names the rider who left", TextOf("Room roster").Contains("THIRD: left"));
            yield return StartWhenReady();
            yield return WaitFor(() => room.View?.roundNumber == 2, 10, "Host starts heat 2 with the two riders left");
            yield return WaitFor(() => room.View?.phase == "connection-wait", 10, "A second background drop holds heat 2");
            yield return WaitFor(() => InResult(2), 25, "The hold runs out and heat 2 ends");
            yield return null;
            var view = room.View;
            Check("The guest away too long gives up the seat", view?.players?.Length == 1 && view.maxPlayers == 2);
            Check("Host wins the heat the guest abandoned", view?.result?.winnerId == view?.selfId);
            string roster = TextOf("Room roster");
            Check("Result names the disconnected rider", roster.Contains("FRIEND: disconnect"));
            Check("Room explains every other rider left", roster.Contains("Every other rider has left"));
            Check("A lone host cannot start", !Control("Start Heat").GetComponent<Button>().interactable);
            yield return CaptureLayout("released-phone.png", 1170, 540);
            // Closing the room first would change the message the returning guest sees.
            string seen = Path.Combine(output, "guest-returned.txt");
            yield return WaitFor(() => File.Exists(seen), 20, "Guest returned and saw the released seat");
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "Host left the room");
        }
        private IEnumerator InterruptGuest(string codePath)
        {
            yield return JoinRoom(codePath);
            string seat = room.SelfId;
            yield return WaitFor(() => room.View?.phase == "countdown", 30, "Heat 1 countdown reached the guest");
            yield return new WaitForSecondsRealtime(.3f);
            Background(true);
            yield return WaitFor(() => !room.Connected, 2, "Going to the background closes the stream");
            yield return new WaitForSecondsRealtime(6);
            Background(false);
            yield return WaitFor(() => room.Connected && room.View?.selfId == seat, 10, "Returning reconnects to the same seat");
            yield return WaitFor(() => room.View?.phase == "running" || InResult(1), 10, "Heat 1 resumes for the returning guest");
            yield return WaitFor(() => room.View?.roundNumber == 2, 40, "Guest sees heat 2");
            Background(true);
            yield return new WaitForSecondsRealtime(13);
            Background(false);
            yield return WaitFor(() => room.Unavailable, 10, "Returning after the hold reports the seat is gone");
            yield return null;
            Check("Status says the guest was away too long (" + room.Status + ")", room.Status.Contains("away too long"));
            Check("Room heading shows unavailable", TextOf("Room heading").EndsWith("UNAVAILABLE"));
            File.WriteAllText(Path.Combine(output, "guest-returned.txt"), room.Status);
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "Guest left the unavailable room");
        }
        private IEnumerator InterruptLeaver(string codePath)
        {
            yield return JoinRoom(codePath);
            yield return WaitFor(() => room.View?.phase == "connection-wait", 30, "THIRD sees the hold");
            yield return new WaitForSecondsRealtime(.5f);
            Check("The hold shows LEAVE ROOM", GameObject.Find("Leave Room") != null);
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "THIRD left during the hold");
        }

        // Two riders. The script freezes the server in the quiet lobby (only heartbeats would arrive),
        // then restarts it mid-heat; afterwards the host opens a new room and closes it during a heat.
        private IEnumerator CloseHost(string codePath, string secondCode)
        {
            yield return HostRoom(codePath, 2);
            string seat = room.SelfId;
            Signal("freeze");
            yield return WaitFor(() => !room.Connected, 12, "A silent server is noticed in the quiet lobby");
            yield return null;
            Check("Room shows reconnecting", TextOf("Room heading").EndsWith("RECONNECTING"));
            yield return WaitFor(() => room.Connected && room.View?.selfId == seat, 30, "Host reconnects once the server answers");
            yield return WaitFor(() => AllConnected(2), 15, "Both riders are back in the lobby");
            yield return StartWhenReady();
            yield return WaitFor(() => room.View?.phase == "running", 10, "Heat runs before the restart");
            Signal("restart");
            yield return WaitFor(() => room.Unavailable, 30, "A server restart ends the room with an explanation");
            yield return null;
            Check("Status explains the restart", room.Status.Contains("server restarted"));
            yield return CaptureLayout("restart-phone.png", 1170, 540);
            Submit("Leave Room");
            yield return WaitFor(() => GameObject.Find("Online setup") != null, 5, "Setup reopens after the restart");
            yield return HostRoom(secondCode, 2);
            yield return StartWhenReady();
            yield return WaitFor(() => room.View?.phase == "running", 10, "The restarted server runs a new heat");
            yield return WaitFor(() => GameObject.Find("Pause") != null, 3, "Host sees PAUSE");
            Submit("Pause");
            yield return WaitFor(() => room.View?.phase == "paused", 5, "Host pauses to leave");
            yield return null;
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "Host closed the room");
        }
        private IEnumerator CloseGuest(string codePath, string secondCode)
        {
            yield return JoinRoom(codePath);
            string seat = room.SelfId;
            yield return WaitFor(() => !room.Connected, 15, "Guest notices the silent server");
            yield return WaitFor(() => room.Connected && room.View?.selfId == seat, 30, "Guest reconnects to the same seat");
            yield return WaitFor(() => room.View?.phase == "running", 20, "Guest heat runs before the restart");
            yield return WaitFor(() => room.Unavailable, 30, "Guest is told the room ended with the restart");
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "Guest left the lost room");
            yield return JoinRoom(secondCode);
            yield return WaitFor(() => room.Unavailable, 30, "Guest is told when the host closes the room");
            yield return null;
            Check("Status says the host closed the room", room.Status == "The host closed the room.");
            Check("Room heading shows unavailable", TextOf("Room heading").EndsWith("UNAVAILABLE"));
            Submit("Leave Room");
            yield return WaitFor(() => room.Code == null, 5, "Guest left the closed room");
        }

        private IEnumerator Run()
        {
            report.role = role; report.engine = Application.unityVersion; report.platform = Application.platform.ToString();
            yield return new WaitForSecondsRealtime(.5f);
            room = FindFirstObjectByType<OnlineRoomClient>();
            Check("Online client loaded", room != null);
            Field("Server origin").text = server;
            Field("Rider name").text = role == "host" ? "HOST" : scenario != null && role == "guest2" ? "THIRD" : "FRIEND";
            string codePath = Path.Combine(output, "room-code.txt");
            if (scenario != null)
            {
                string secondCode = Path.Combine(output, "room-code-2.txt");
                if (scenario == "interrupt")
                    yield return role == "host" ? InterruptHost(codePath) : role == "guest" ? InterruptGuest(codePath) : InterruptLeaver(codePath);
                else yield return role == "host" ? CloseHost(codePath, secondCode) : CloseGuest(codePath, secondCode);
                CancelInvoke(nameof(Timeout)); Save(); Application.Quit(failed ? 2 : 0);
                yield break;
            }
            if (role == "outdated")
            {
                // Plays a build one protocol behind the server: Create and Join must stop with an update message.
                OnlineRoomClient.SpokenProtocol = OnlineRoomClient.Protocol - 1;
                Submit("Create Room");
                yield return WaitFor(() => !room.Busy && room.Status.Contains("Update Grid Run"), 5, "Outdated app cannot create a room");
                Check("Outdated app stays out of rooms", room.Code == null);
                Field("Room code").text = "123456";
                Submit("Join Room");
                yield return WaitFor(() => !room.Busy && room.Status.Contains("Update Grid Run"), 5, "Outdated app cannot join a room");
                yield return null;
                Check("Setup screen explains the update", TextOf("Online notice").Contains("Update Grid Run"));
                yield return CaptureLayout("outdated-phone.png", 1170, 540);
                CancelInvoke(nameof(Timeout)); Save(); Application.Quit(failed ? 2 : 0);
                yield break;
            }
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
                    Check("Walls button names fading walls", Control("Room Walls").GetComponentInChildren<Text>().text == "WALLS: FADING");
                }
                Check("Room settled on " + map + " with " + rule + " walls", room.View?.map == map && room.View?.rule == rule);
                // Walls are chosen first: the server accepts a wall rule only while the room is in Arena.
                for (int i = 0; i < 2 && room.View?.mode != mode; i++)
                {
                    string before = room.View?.mode;
                    Submit("Room Mode");
                    yield return WaitFor(() => room.View?.mode != before, 5, "Host changed the mode from " + before);
                    yield return null;
                }
                Check("Room plays " + mode, room.View?.mode == mode);
                Check("Mode button names " + ModeName(mode), Control("Room Mode").GetComponentInChildren<Text>().text == "MODE: " + ModeName(mode));
                Check("Start button names " + ModeName(mode), Control("Start Heat").GetComponentInChildren<Text>().text == "START " + ModeName(mode));
                Check("Walls choice is Arena only", Control("Room Walls").GetComponent<Button>().interactable == (mode == "arena"));
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
            Check("Heat plays " + mode, room.View.mode == mode &&
                (GameObject.Find("Matchline")?.GetComponent<Text>()?.text ?? "").StartsWith(ModeName(mode) + " / HEAT"));
            var colours = new HashSet<string>();
            foreach (var rider in streamed) colours.Add(rider.color == null ? "" : string.Join(",", rider.color));
            Check("Each seat has its own colour", colours.Count == seats && !colours.Contains(""));
            StartCoroutine(WatchCombat());
            var audio = FindFirstObjectByType<Solo.SoloAudio>();
            Check("Online sound follows the Sound setting", audio != null && audio.Enabled == Solo.SoloProfile.Sound && audio.Enabled);
            Check("Countdown and start sounds played", Heard("beep 440") && Heard("beep 730") && Heard("wall"));
            // The Classic Grid host measures motion inside its turn check; everyone else measures here.
            var own = GameObject.Find("Player cycle")?.transform;
            if (own != null && !(role == "host" && map == "classic")) yield return CheckSmoothMotion(own, .4f);
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
                yield return CheckJet();
                if (mode == "shift") yield return CheckShift();
                Submit("Pause");
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
            // The result snapshot can carry the final crash; the scene plays its cues on the next frame.
            yield return null;
            Check("Music played during the heat", heardMusic);
            Check("Crash sounds played", Heard("crash") && Heard("derez"));
            string roster = GameObject.Find("Room roster")?.GetComponent<Text>()?.text ?? "";
            Check("Result names the winner or a draw", (roster.Contains("WINS THE HEAT") || roster.Contains("HEAT DRAW")) && !roster.Contains("\n WINS"));
            if (role == "host")
            {
                yield return CaptureLayout("result-phone.png", 1170, 540);
                Submit("Start Heat");
                yield return WaitFor(() => room.View?.roundNumber == 2, 10, "Host started next heat");
                // Guests leave on purpose here, which forfeits at once rather than holding the heat.
                yield return WaitFor(() => InResult(2) && room.View.players.Length == 1, 20, "Departures end heat 2 and free their seats");
                // Let the presentation component apply the fresh snapshot this frame.
                yield return null;
                Check("Room explains every other rider left", TextOf("Room roster").Contains("Every other rider has left"));
                Check("A lone host cannot start", !Control("Start Heat").GetComponent<Button>().interactable);
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
