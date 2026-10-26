using System;
using System.Collections.Generic;
using GridRun.Solo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GridRun
{
    // Phase 2 two-client prototype: the Node server owns all match state and outcomes.
    public sealed class OnlineArenaGame : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        [SerializeField] private string defaultServerOrigin = "https://tron-2nkq.onrender.com";
        private OnlineRoomClient room;
        private Font font;
        private Camera view;
        private Vector3 cameraAim;
        private readonly Dictionary<string, Transform> riders = new Dictionary<string, Transform>();
        private readonly Dictionary<int, Transform> trails = new Dictionary<int, Transform>();
        private readonly Transform[] walls = new Transform[4];
        private readonly Transform[] wallEdges = new Transform[4];
        private GameObject setupPanel, roomPanel, playHud;
        private InputField serverField, nameField, codeField;
        private Button createButton, joinButton, startButton, pauseButton;
        private Text notice, roomHeading, roster, score, phase, startLabel, pauseLabel, autoLabel;
        private HoldControl forward, fire;
        private OnlineRoomClient.RoomView shown;
        private bool autoDrive = true;
        private int lastRoundNumber;
        private bool verificationNoFocusPause;

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            verificationNoFocusPause = Array.IndexOf(Environment.GetCommandLineArgs(), "--phase2-capture-dir") >= 0;
#endif
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            { Debug.LogError("Phase 2 scene is not prepared."); enabled = false; return; }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            room = gameObject.AddComponent<OnlineRoomClient>();
            BuildWorld();
            BuildUI();
            ShowSetup();
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var obj = GridLook.Cube(name, material);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = size;
            return obj;
        }

        private void BuildWorld()
        {
            GridLook.Floor(transform, Vector3.zero, new Vector2(120, 120), 4, floorMaterial);
            GridLook.Stadium(transform, 56);
            GridLook.Skyline(transform, 125, 180, 70, 11);
            for (int side = 0; side < 4; side++)
            {
                walls[side] = Box("Moving boundary", Vector3.zero, Vector3.one, GridLook.Assets.Boundary, transform).transform;
                wallEdges[side] = Box("Boundary edge", Vector3.zero, Vector3.one, glowMaterial, transform).transform;
            }
            var cameraObject = new GameObject("Online camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 260; view.fieldOfView = 60;
            view.transform.position = new Vector3(0, 65, -65);
            view.transform.rotation = Quaternion.Euler(45, 0, 0);
            GridLook.Stage(view);
            SetBoundary(52);
        }

        private void SetBoundary(float limit)
        {
            float edge = limit + .4f, span = limit * 2 + 1;
            Vector3[] centers = { new Vector3(-edge, 0, 0), new Vector3(edge, 0, 0), new Vector3(0, 0, -edge), new Vector3(0, 0, edge) };
            for (int side = 0; side < 4; side++)
            {
                bool alongZ = side < 2;
                walls[side].localPosition = centers[side] + Vector3.up;
                walls[side].localScale = alongZ ? new Vector3(.3f, 2, span) : new Vector3(span, 2, .3f);
                wallEdges[side].localPosition = centers[side] + Vector3.up * 2.03f;
                wallEdges[side].localScale = alongZ ? new Vector3(.34f, .06f, span) : new Vector3(span, .06f, .34f);
            }
        }

        private Transform Bike(string id, bool self)
        {
            return CycleVisual.Create(transform, "Online rider " + id, cycleMaterial,
                self ? glowMaterial : amberMaterial).transform;
        }

        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
            rect.anchoredPosition = offset; rect.sizeDelta = size;
            return rect;
        }

        private Text Label(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, int fontSize, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize; text.color = new Color(.84f, .97f, 1);
            text.text = value; text.alignment = align; text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        private Button Button(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            UiSkin.Button(button, image);
            if (action != null) button.onClick.AddListener(action);
            Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 25, TextAnchor.MiddleCenter);
            return button;
        }

        private InputField Field(string name, string value, Transform parent, Vector2 position, Vector2 size, int limit)
        {
            var rect = Rect(name, parent, new Vector2(.5f, 1), new Vector2(.5f, 1), position, size);
            var image = rect.gameObject.AddComponent<Image>();
            UiSkin.Field(image);
            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image; input.characterLimit = limit;
            var label = Label(name + " text", value, rect, Vector2.zero, Vector2.zero, new Vector2(18, 0), size - new Vector2(36, 0), 28);
            input.textComponent = label;
            input.text = value;
            return input;
        }

        private void TurnButton(Button button, int sign)
        {
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var pointer = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pointer.callback.AddListener(_ => Turn(sign)); trigger.triggers.Add(pointer);
            var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
            submit.callback.AddListener(_ => Turn(sign)); trigger.triggers.Add(submit);
        }

        private void BuildUI()
        {
            new GameObject("UI input", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = new GameObject("Online UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("Safe area", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvas.transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            var hud = Rect("Online HUD", safe.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            hud.anchorMin = Vector2.zero; hud.anchorMax = Vector2.one; playHud = hud.gameObject;
            var scrim = Rect("Top contrast", hud, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(2300, 175));
            scrim.gameObject.AddComponent<Image>().color = new Color(.005f, .015f, .03f, .88f);
            score = Label("Online score", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -17), new Vector2(1320, 54), 32);
            phase = Label("Online phase", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -80), new Vector2(1300, 55), 26);
            pauseButton = Button("Host Pause", "PAUSE", hud, Vector2.one, Vector2.one, new Vector2(-32, -27), new Vector2(190, 85), Pause);
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
            TurnButton(Button("Left", "LEFT", hud, Vector2.zero, Vector2.zero, new Vector2(32, 28), new Vector2(225, 140), null), -1);
            TurnButton(Button("Right", "RIGHT", hud, Vector2.zero, Vector2.zero, new Vector2(278, 28), new Vector2(225, 140), null), 1);
            forward = Button("Forward", "FORWARD", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 28), new Vector2(260, 140), null).gameObject.AddComponent<HoldControl>();
            fire = Button("Fire", "FIRE", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-310, 28), new Vector2(230, 140), null).gameObject.AddComponent<HoldControl>();
            autoLabel = Button("Auto Drive", "AUTO: ON", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 190), new Vector2(260, 78), ToggleAuto).GetComponentInChildren<Text>();
            var setup = Rect("Online setup", safe.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 820));
            var setupImage = setup.gameObject.AddComponent<Image>();
            UiSkin.Panel(setupImage); setupPanel = setup.gameObject;
            Label("Setup heading", "GRID RUN  /  ONLINE ARENA", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(1120, 75), 48, TextAnchor.MiddleCenter);
            Label("Server caption", "GAME SERVER HTTPS URL", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -125), new Vector2(900, 40), 22);
            serverField = Field("Server origin", defaultServerOrigin, setup, new Vector2(0, -170), new Vector2(920, 70), 160);
            Label("Name caption", "RIDER NAME", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-265, -260), new Vector2(390, 40), 22);
            nameField = Field("Rider name", "RIDER", setup, new Vector2(-265, -305), new Vector2(390, 70), 18);
            Label("Code caption", "FRIEND'S SIX-DIGIT CODE", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(265, -260), new Vector2(390, 40), 22);
            codeField = Field("Room code", "", setup, new Vector2(265, -305), new Vector2(390, 70), 6);
            codeField.contentType = InputField.ContentType.IntegerNumber;
            createButton = Button("Create Room", "CREATE ROOM", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-255, -410), new Vector2(440, 85), Create);
            joinButton = Button("Join Room", "JOIN ROOM", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(255, -410), new Vector2(440, 85), Join);
            notice = Label("Online notice", "", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 205), new Vector2(1080, 75), 25, TextAnchor.MiddleCenter);
            Label("Setup rules", "Arena / Classic Grid / 2 riders. Host shares the code; both riders connect before START.", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 105), new Vector2(1100, 60), 22, TextAnchor.MiddleCenter);
            Button("Offline Arena", "MAIN MENU", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(350, 78), () => SceneManager.LoadScene("PhaseOneArena"));
            var lobby = Rect("Online room", safe.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 760));
            var lobbyImage = lobby.gameObject.AddComponent<Image>();
            UiSkin.Panel(lobbyImage); roomPanel = lobby.gameObject;
            roomHeading = Label("Room heading", "ROOM", lobby, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(1120, 75), 47, TextAnchor.MiddleCenter);
            roster = Label("Room roster", "", lobby, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 50), new Vector2(1040, 400), 29, TextAnchor.MiddleCenter);
            startButton = Button("Start Heat", "START ARENA", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-240, 55), new Vector2(425, 95), StartHeat);
            startLabel = startButton.GetComponentInChildren<Text>();
            Button("Leave Room", "LEAVE ROOM", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(240, 55), new Vector2(425, 95), Leave);
        }

        private void ShowSetup()
        {
            setupPanel.SetActive(true); roomPanel.SetActive(false); playHud.SetActive(false);
            notice.text = room.Status;
            shown = null; lastRoundNumber = 0;
            ClearWorld();
        }
        private async void Create()
        {
            try { await room.CreateAsync(serverField.text, nameField.text, 2); }
            catch { /* The client exposes a clear status message. */ }
        }
        private async void Join()
        {
            try { await room.JoinAsync(serverField.text, codeField.text.Trim(), nameField.text); }
            catch { /* The client exposes a clear status message. */ }
        }
        private async void Leave() { await room.LeaveAsync(); ShowSetup(); }
        private async void StartHeat() { await room.SendAsync(room.View?.phase == "paused" ? "pause" : "start"); }
        private async void Pause() { await room.SendAsync("pause"); }
        private async void ToggleAuto()
        {
            autoDrive = !autoDrive;
            autoLabel.text = autoDrive ? "AUTO: ON" : "AUTO: OFF";
            await room.SendAsync("auto-drive", enabled: autoDrive);
        }
        private async void Turn(int sign)
        {
            if (room.View?.phase == "running" && room.Connected) await room.SendAsync("turn", sign: sign);
        }
        private bool lastThrottle, lastFire;
        private void Update()
        {
            if (room == null) return;
            createButton.interactable = joinButton.interactable = !room.Busy;
            notice.text = room.Status;
            if (!room.Connected && !string.IsNullOrEmpty(room.Code) && room.View != null)
            {
                playHud.SetActive(false);
                roomPanel.SetActive(true);
                startButton.gameObject.SetActive(false);
                bool unavailable = room.Status.StartsWith("Room expired", StringComparison.Ordinal);
                roomHeading.text = "ROOM " + room.Code + (unavailable ? "  /  UNAVAILABLE" : "  /  RECONNECTING");
                roster.text = room.Status + (unavailable ? "\nLeave this room to start again." :
                    "\nYour seat is held while the server allows reconnection.");
            }
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Turn(-1);
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Turn(1);
            if (Input.GetKeyDown(KeyCode.Escape) && room.IsHost &&
                (room.View?.phase == "running" || room.View?.phase == "paused")) Pause();
            var current = room.View;
            if (current != null && !ReferenceEquals(current, shown)) { shown = current; Present(current); }
            bool active = room.Connected && room.Supported && current?.phase == "running";
            bool throttle = active && (forward.Held || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));
            bool firing = active && (fire.Held || Input.GetKey(KeyCode.E));
            if (throttle != lastThrottle) { lastThrottle = throttle; _ = room.SendAsync("throttle", held: throttle); }
            if (firing != lastFire) { lastFire = firing; _ = room.SendAsync("fire", held: firing); }
        }

        private void Present(OnlineRoomClient.RoomView current)
        {
            if (!room.Supported)
            {
                setupPanel.SetActive(false); roomPanel.SetActive(true); playHud.SetActive(false);
                roomHeading.text = "UNSUPPORTED ROOM";
                roster.text = room.Status + "\nLeave this room to create or join a supported Arena match.";
                startButton.gameObject.SetActive(false);
                return;
            }
            bool active = current.phase == "running" || current.phase == "countdown";
            setupPanel.SetActive(false);
            if (active) EventSystem.current.SetSelectedGameObject(null);
            roomPanel.SetActive(!active);
            playHud.SetActive(active);
            roomHeading.text = "ROOM " + current.code + "  /  " + current.phase.ToUpperInvariant();
            string lines = "ARENA / CLASSIC GRID / " + current.players.Length + "/" + current.maxPlayers + " RIDERS\n";
            foreach (var player in current.players)
                lines += player.name + (player.id == current.selfId ? " / YOU" : "") +
                    "  -  " + player.wins + " HEAT WINS  -  " + (player.connected ? "CONNECTED" : "OFFLINE") + "\n";
            if (current.phase == "connection-wait") lines += "Waiting for a rider to reconnect.\n";
            if (current.result != null && (current.phase == "round-result" || current.phase == "match-result"))
            {
                lines += current.result.winnerName == null ? "HEAT DRAW\n" : current.result.winnerName + " WINS THE HEAT\n";
                if (current.result.crashes != null)
                    foreach (var crash in current.result.crashes) lines += crash.riderId + ": " + crash.reason + "\n";
                if (current.phase == "match-result")
                    lines += current.result.matchDraw ? "MATCH DRAW" : current.result.matchWinnerName + " WINS THE MATCH";
            }
            roster.text = lines;
            bool host = current.selfId == current.hostId;
            startButton.gameObject.SetActive(host && (current.phase == "lobby" || current.phase == "round-result" || current.phase == "match-result" || current.phase == "paused"));
            startButton.interactable = current.players.Length == current.maxPlayers && Array.TrueForAll(current.players, p => p.connected);
            startLabel.text = current.phase == "paused" ? "RESUME" : current.phase == "match-result" ? "REMATCH" : current.phase == "round-result" ? "NEXT HEAT" : "START ARENA";
            pauseButton.gameObject.SetActive(host);
            pauseLabel.text = current.phase == "paused" ? "RESUME" : "PAUSE";
            score.text = "ROOM " + current.code + "  /  HEAT " + current.roundNumber + "  /  " +
                string.Join("   ", Array.ConvertAll(current.players, p => p.name + " " + p.wins));
            phase.text = current.phase == "countdown" ? "STARTING IN " + Mathf.CeilToInt(current.countdownMs / 1000f) :
                current.phase == "running" ? "LIVE ARENA  /  " + (room.Connected ? "CONNECTED" : "RECONNECTING") : current.phase.ToUpperInvariant();
            if (current.round != null && current.roundNumber != lastRoundNumber)
                _ = room.SendAsync("auto-drive", enabled: autoDrive);
            lastRoundNumber = current.roundNumber;
            RenderRound(current);
        }

        private void RenderRound(OnlineRoomClient.RoomView current)
        {
            var round = current.round;
            if (round == null) return;
            SetBoundary(round.limit > 0 ? round.limit : 52);
            if (round.riders != null)
                foreach (var rider in round.riders)
                {
                    if (!riders.TryGetValue(rider.id, out var visual))
                    { visual = Bike(rider.id, rider.id == current.selfId); riders.Add(rider.id, visual); }
                    visual.gameObject.SetActive(rider.alive);
                    // Same sim-to-Unity mapping as offline play, so relative turns read the same way.
                    visual.position = SoloWorld.W(rider.x, rider.z);
                    visual.rotation = Quaternion.Euler(0, SoloWorld.Yaw(rider.dir), 0);
                }
            var seen = new HashSet<int>();
            if (round.segments != null)
                foreach (var segment in round.segments)
                {
                    seen.Add(segment.id);
                    if (!trails.TryGetValue(segment.id, out var visual))
                    {
                        visual = Box("Online light trail", Vector3.zero, Vector3.one,
                            GridLook.Trail(segment.ownerId == current.selfId), transform).transform;
                        trails.Add(segment.id, visual);
                    }
                    visual.position = SoloWorld.W((segment.x1 + segment.x2) * .5f, (segment.z1 + segment.z2) * .5f, .55f);
                    visual.localScale = new Vector3(Mathf.Max(.14f, Mathf.Abs(segment.x2 - segment.x1)), 1.1f,
                        Mathf.Max(.14f, Mathf.Abs(segment.z2 - segment.z1)));
                    visual.gameObject.SetActive(segment.active);
                }
            var removed = new List<int>();
            foreach (var pair in trails) if (!seen.Contains(pair.Key)) { Destroy(pair.Value.gameObject); removed.Add(pair.Key); }
            foreach (var id in removed) trails.Remove(id);
        }

        private void LateUpdate()
        {
            if (view == null) return;
            Vector3 target = new Vector3(0, 65, -65), aim = Vector3.zero;
            if (room?.View?.phase == "running" && riders.TryGetValue(room.SelfId ?? "", out var self) && self.gameObject.activeSelf)
                ChaseCamera.Target(self.position, self.rotation, out target, out aim);
            ChaseCamera.Follow(view.transform, ref cameraAim, target, aim, Time.deltaTime);
        }
        private void ClearWorld()
        {
            foreach (var rider in riders.Values) Destroy(rider.gameObject);
            foreach (var trail in trails.Values) Destroy(trail.gameObject);
            riders.Clear(); trails.Clear(); SetBoundary(52);
        }
        private void OnApplicationPause(bool paused)
        {
            if (!paused || room == null) return;
            forward?.Clear(); fire?.Clear();
            lastThrottle = lastFire = false;
            _ = room.SendAsync("throttle", held: false);
            _ = room.SendAsync("fire", held: false);
        }
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && !verificationNoFocusPause) OnApplicationPause(true);
        }
        private void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
