using System;
using System.Collections.Generic;
using GridRun.Sim;
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
        // The solo renderer draws server snapshots too, so maps, trails, and fading walls look the same online.
        private SoloWorld world;
        private readonly SceneView scene = new SceneView();
        private GameObject setupPanel, roomPanel, playHud;
        private InputField serverField, nameField, codeField;
        private Button createButton, joinButton, startButton, pauseButton, mapButton, wallsButton;
        private Text notice, roomHeading, roster, score, phase, startLabel, pauseLabel, autoLabel, mapLabel, wallsLabel;
        private HoldControl forward, fire;
        private OnlineRoomClient.RoomView shown;
        // Seat colours follow createLanRound's order (by position in the room's player list).
        private static readonly string[] SeatColours = { "CYAN", "ORANGE", "VIOLET", "GOLD" };
        private Text seatsLabel, fireLabel, toast;
        private float toastUntil;
        private int lastEffectId;
        private int seats = 2;
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

        private void BuildWorld()
        {
            world = new SoloWorld(transform, floorMaterial, cycleMaterial, glowMaterial);
            var cameraObject = new GameObject("Online camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 260; view.fieldOfView = 60;
            view.transform.position = new Vector3(0, 65, -65);
            view.transform.rotation = Quaternion.Euler(45, 0, 0);
            GridLook.Stage(view);
            ShowMap("classic");
        }

        // Lobby and setup backdrop: the chosen map with no riders.
        private void ShowMap(string mapId)
        {
            world.Clear();
            scene.Riders.Clear(); scene.Segments.Clear(); scene.Bullets.Clear(); scene.Pickups.Clear();
            lastEffectId = 0; toastUntil = 0;
            scene.Map = mapId; scene.Rule = "classic"; scene.Elapsed = 0;
            scene.Limit = SimMaps.Get(mapId).Limit; scene.TrailLifetime = double.PositiveInfinity;
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
            // Four rider names can outgrow one line; shrink rather than spill into the phase line.
            score.resizeTextForBestFit = true; score.resizeTextMinSize = 20; score.resizeTextMaxSize = 32;
            phase = Label("Online phase", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -80), new Vector2(1300, 55), 26);
            pauseButton = Button("Host Pause", "PAUSE", hud, Vector2.one, Vector2.one, new Vector2(-32, -27), new Vector2(190, 85), Pause);
            pauseLabel = pauseButton.GetComponentInChildren<Text>();
            TurnButton(Button("Left", "LEFT", hud, Vector2.zero, Vector2.zero, new Vector2(32, 28), new Vector2(225, 140), null), -1);
            TurnButton(Button("Right", "RIGHT", hud, Vector2.zero, Vector2.zero, new Vector2(278, 28), new Vector2(225, 140), null), 1);
            forward = Button("Forward", "FORWARD", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 28), new Vector2(260, 140), null).gameObject.AddComponent<HoldControl>();
            var fireButton = Button("Fire", "FIRE", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-310, 28), new Vector2(230, 140), null);
            fire = fireButton.gameObject.AddComponent<HoldControl>();
            fireLabel = fireButton.GetComponentInChildren<Text>();
            toast = Label("Online toast", "", hud, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -190), new Vector2(1100, 60), 34, TextAnchor.MiddleCenter);
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
            seatsLabel = Button("Room Size", "RIDERS: 2", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-390, -410), new Vector2(170, 85), CycleSeats).GetComponentInChildren<Text>();
            createButton = Button("Create Room", "CREATE ROOM", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-165, -410), new Vector2(260, 85), Create);
            joinButton = Button("Join Room", "JOIN ROOM", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(255, -410), new Vector2(440, 85), Join);
            notice = Label("Online notice", "", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 205), new Vector2(1080, 75), 25, TextAnchor.MiddleCenter);
            Label("Setup rules", "Arena for 2-4 riders. The host picks the room size, then the map and walls in the lobby; START opens when every seat is connected.", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 105), new Vector2(1100, 60), 22, TextAnchor.MiddleCenter);
            Button("Offline Arena", "MAIN MENU", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(350, 78), () => SceneManager.LoadScene("PhaseOneArena"));
            var lobby = Rect("Online room", safe.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 760));
            var lobbyImage = lobby.gameObject.AddComponent<Image>();
            UiSkin.Panel(lobbyImage); roomPanel = lobby.gameObject;
            roomHeading = Label("Room heading", "ROOM", lobby, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(1120, 75), 47, TextAnchor.MiddleCenter);
            roster = Label("Room roster", "", lobby, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 70), new Vector2(1040, 380), 29, TextAnchor.MiddleCenter);
            mapButton = Button("Room Map", "MAP: CLASSIC GRID", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-240, 165), new Vector2(425, 80), CycleMap);
            mapLabel = mapButton.GetComponentInChildren<Text>();
            wallsButton = Button("Room Walls", "WALLS: PERSISTENT", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(240, 165), new Vector2(425, 80), CycleWalls);
            wallsLabel = wallsButton.GetComponentInChildren<Text>();
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
            try { await room.CreateAsync(serverField.text, nameField.text, seats); }
            catch { /* The client exposes a clear status message. */ }
        }
        private void CycleSeats()
        {
            seats = seats == 4 ? 2 : seats + 1;
            seatsLabel.text = "RIDERS: " + seats;
        }
        private async void Join()
        {
            try { await room.JoinAsync(serverField.text, codeField.text.Trim(), nameField.text); }
            catch { /* The client exposes a clear status message. */ }
        }
        // The server accepts map and wall choices from the host in the lobby only.
        private async void CycleMap()
        {
            var current = room.View;
            if (current == null || current.phase != "lobby") return;
            string next = SimMaps.Order[(Array.IndexOf(SimMaps.Order, current.map) + 1) % SimMaps.Order.Length];
            await room.SendAsync("map", map: next);
        }
        private async void CycleWalls()
        {
            var current = room.View;
            if (current == null || current.phase != "lobby") return;
            await room.SendAsync("rule", rule: current.rule == "decay" ? "classic" : "decay");
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
            toast.gameObject.SetActive(Time.unscaledTime < toastUntil);
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
            string lines = "ARENA / " + SimMaps.Get(current.map).Name.ToUpperInvariant() + " / " + WallsName(current.rule) + " / " +
                current.players.Length + "/" + current.maxPlayers + " RIDERS\n";
            for (int i = 0; i < current.players.Length; i++)
            {
                var player = current.players[i];
                lines += SeatColours[i % SeatColours.Length] + "  " + player.name + (player.id == current.selfId ? " / YOU" : "") +
                    "  -  " + player.wins + " HEAT WINS  -  " + (player.connected ? "CONNECTED" : "OFFLINE") + "\n";
            }
            int open = current.maxPlayers - current.players.Length;
            if (current.phase == "lobby" && open > 0) lines += "Waiting for " + open + (open == 1 ? " more rider.\n" : " more riders.\n");
            if (current.phase == "connection-wait") lines += "Waiting for a rider to reconnect.\n";
            if (current.result != null && (current.phase == "round-result" || current.phase == "match-result"))
            {
                // JsonUtility reads a null winner as an empty string.
                lines += string.IsNullOrEmpty(current.result.winnerId) ? "HEAT DRAW\n" : Seat(current, current.result.winnerId) + " WINS THE HEAT\n";
                if (current.result.crashes != null)
                    foreach (var crash in current.result.crashes) lines += Seat(current, crash.riderId) + ": " + crash.reason + "\n";
                if (current.phase == "match-result")
                    lines += current.result.matchDraw || string.IsNullOrEmpty(current.result.matchWinnerId) ? "MATCH DRAW" :
                        Seat(current, current.result.matchWinnerId) + " WINS THE MATCH";
            }
            roster.text = lines;
            bool host = current.selfId == current.hostId;
            startButton.gameObject.SetActive(host && (current.phase == "lobby" || current.phase == "round-result" || current.phase == "match-result" || current.phase == "paused"));
            startButton.interactable = current.players.Length == current.maxPlayers && Array.TrueForAll(current.players, p => p.connected);
            startLabel.text = current.phase == "paused" ? "RESUME" : current.phase == "match-result" ? "REMATCH" : current.phase == "round-result" ? "NEXT HEAT" : "START ARENA";
            bool choosing = host && current.phase == "lobby";
            mapButton.gameObject.SetActive(choosing); wallsButton.gameObject.SetActive(choosing);
            mapLabel.text = "MAP: " + SimMaps.Get(current.map).Name.ToUpperInvariant();
            wallsLabel.text = "WALLS: " + WallsName(current.rule);
            pauseButton.gameObject.SetActive(host);
            pauseLabel.text = current.phase == "paused" ? "RESUME" : "PAUSE";
            score.text = "ROOM " + current.code + "  /  HEAT " + current.roundNumber + "  /  " +
                string.Join("   ", Array.ConvertAll(current.players, p => Seat(current, p.id) + " " + p.wins));
            phase.text = current.phase == "countdown" ? "STARTING IN " + Mathf.CeilToInt(current.countdownMs / 1000f) :
                current.phase == "running" ? "LIVE ARENA  /  " + (room.Connected ? "CONNECTED" : "RECONNECTING") + Weapon(current) :
                current.phase.ToUpperInvariant();
            if (current.round != null && current.roundNumber != lastRoundNumber)
                _ = room.SendAsync("auto-drive", enabled: autoDrive);
            lastRoundNumber = current.roundNumber;
            RenderRound(current);
        }

        // Colour plus name, so riders who share a name stay distinct without relying on colour alone.
        private static string Seat(OnlineRoomClient.RoomView view, string id)
        {
            int i = Array.FindIndex(view.players, p => p.id == id);
            return i < 0 ? id : SeatColours[i % SeatColours.Length] + " " + view.players[i].name;
        }

        private static string WallsName(string rule) => rule == "decay" ? "FADING WALLS" : "PERSISTENT WALLS";

        // Same wording as the solo HUD's weapon line and FIRE button.
        private string Weapon(OnlineRoomClient.RoomView current)
        {
            var self = Array.Find(current.round?.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == current.selfId);
            if (self == null) return "";
            fireLabel.text = self.rocketReady ? "ROCKET" : "FIRE " + self.ammo;
            return "  /  " + (self.rocketReady ? "ROCKET 1" : "GUN " + self.ammo + "/48") +
                (current.round.elapsed < self.nitroUntil ? "  /  NITRO " + Mathf.CeilToInt(self.nitroUntil - current.round.elapsed) + "S" : "");
        }

        private void ShowToast(string text)
        {
            toast.text = text;
            toastUntil = Time.unscaledTime + 2;
        }

        // Effects arrive in several snapshots while the server keeps them, so each id plays once.
        private void PlayEffects(OnlineRoomClient.RoomView current)
        {
            foreach (var e in current.round.effects ?? new OnlineRoomClient.EffectView[0])
            {
                if (e.id <= lastEffectId) continue;
                lastEffectId = e.id;
                string item = (e.pickup ?? "").ToUpperInvariant();
                switch (e.type)
                {
                    case "pickup-spawn": ShowToast(item + " SPAWNED"); break;
                    case "pickup":
                        ShowToast(e.riderId == current.selfId ? item + " PICKED UP" : Seat(current, e.riderId) + " TOOK " + item);
                        var picker = scene.Riders.Find(r => r.Id == e.riderId);
                        if (picker.Id != null)
                            world.Ring(picker.X, picker.Z, e.pickup == "ammo" ? new[] { .25f, .85f, 1f } : e.pickup == "nitro" ? new[] { .25f, 1f, .4f } : new[] { 1f, .54f, .18f }, 1.4f);
                        break;
                    case "rocket-explode":
                        world.Impact(e.x, e.z);
                        world.Ring(e.x, e.z, new[] { 1f, .48f, .13f }, 3.2f);
                        ShowToast("ROCKET IMPACT");
                        break;
                    case "arena-shrink": ShowToast("GRID SHRINKING / " + Mathf.RoundToInt(e.limit)); break;
                }
            }
        }

        // Copies the server snapshot into the solo renderer's view; the own seat counts as the player.
        private void RenderRound(OnlineRoomClient.RoomView current)
        {
            var round = current.round;
            if (round == null)
            {
                if (scene.Map != current.map || scene.Riders.Count > 0) ShowMap(current.map);
                return;
            }
            scene.Map = round.map; scene.Rule = round.rule; scene.Elapsed = round.elapsed;
            scene.Limit = round.limit > 0 ? round.limit : SimMaps.Get(round.map).Limit;
            // JsonUtility reads the null lifetime of persistent walls as 0.
            scene.TrailLifetime = round.rule == "decay" && round.trailLifetime > 0 ? round.trailLifetime : double.PositiveInfinity;
            scene.Riders.Clear(); scene.Segments.Clear();
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
                scene.Riders.Add(new Solo.RiderView
                {
                    Id = r.id, X = r.x, Z = r.z, Dir = r.dir, Alive = r.alive, IsPlayer = r.id == current.selfId,
                    Form = string.IsNullOrEmpty(r.form) ? "cycle" : r.form, Style = r.style, Color = r.color, FormChangedAt = -10,
                });
            foreach (var s in round.segments ?? new OnlineRoomClient.SegmentView[0])
                scene.Segments.Add(new Solo.SegmentView
                { Id = s.id, OwnerId = s.ownerId, X1 = s.x1, Z1 = s.z1, X2 = s.x2, Z2 = s.z2, Active = s.active, ClosedAt = s.closedAt });
            scene.Bullets.Clear(); scene.Pickups.Clear();
            foreach (var b in round.bullets ?? new OnlineRoomClient.BulletView[0])
                scene.Bullets.Add(new Solo.BulletView { X = b.x, Z = b.z, Rocket = b.rocket, FromPlayer = b.ownerId == current.selfId, Vx = b.vx, Vz = b.vz });
            foreach (var p in round.pickups ?? new OnlineRoomClient.PickupView[0])
                if (p.active) scene.Pickups.Add(new Solo.PickupView { Id = p.id, Type = p.type, X = p.x, Z = p.z });
            PlayEffects(current);
        }

        private void LateUpdate()
        {
            if (view == null) return;
            world.Render(scene, Time.deltaTime);
            Vector3 target = new Vector3(0, 65, -65), aim = Vector3.zero;
            var self = world.Player;
            if (room?.View?.phase == "running" && self != null && self.gameObject.activeSelf)
                ChaseCamera.Target(self.position, self.rotation, out target, out aim);
            ChaseCamera.Follow(view.transform, ref cameraAim, target, aim, Time.deltaTime);
        }
        private void ClearWorld() => ShowMap("classic");
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
        private void OnDestroy()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }
    }
}
