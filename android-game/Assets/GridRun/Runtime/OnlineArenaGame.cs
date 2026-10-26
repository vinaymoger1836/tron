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
    // Online rooms for 2-4 riders (Arena, Duel, Grid Shift): the Node server owns all match state and outcomes.
    // Snapshots are drawn with the solo renderer and HUD, so online play looks and controls like solo play.
    public sealed class OnlineArenaGame : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        [SerializeField] private string defaultServerOrigin = "https://tron-2nkq.onrender.com";
        private OnlineRoomClient room;
        private Font font;
        private Camera view;
        private Vector3 cameraAim;
        private SoloWorld world;
        private SoloHud hud;
        private readonly SceneView scene = new SceneView();
        private GameObject setupPanel, roomPanel, playHud;
        private InputField serverField, nameField, codeField;
        private Button createButton, joinButton, startButton, modeButton, mapButton, wallsButton;
        private Text notice, roomHeading, roster, startLabel, modeLabel, mapLabel, wallsLabel, seatsLabel;
        private OnlineRoomClient.RoomView shown;
        // Seat colours follow createLanRound's order (by position in the room's player list).
        private static readonly string[] SeatColours = { "CYAN", "ORANGE", "VIOLET", "GOLD" };
        private static readonly string[] Modes = { "arena", "duel", "shift" };
        private int lastEffectId;
        private int seats = 2;
        private bool autoDrive = true;
        private int lastRoundNumber;
        private bool verificationNoFocusPause;
        private bool lastThrottle, lastFire;
        private Vector2 lastMove;

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
            scene.Riders.Clear(); scene.Segments.Clear(); scene.Bullets.Clear(); scene.Pickups.Clear(); scene.Discs.Clear();
            lastEffectId = 0;
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
            hud = new SoloHud(safe.transform, Turn, Pause, ToggleAuto, () => Flight("jet"), () => Flight("sail"), Form, Disc);
            hud.SetObjective(false); hud.SetShiftPanel(false);
            playHud = hud.Root;
            var setup = Rect("Online setup", safe.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 820));
            var setupImage = setup.gameObject.AddComponent<Image>();
            UiSkin.Panel(setupImage); setupPanel = setup.gameObject;
            Label("Setup heading", "GRID RUN  /  ONLINE ROOM", setup, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(1120, 75), 48, TextAnchor.MiddleCenter);
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
            Label("Setup rules", "Arena, Duel, or Grid Shift for 2-4 riders. The host picks the room size here, then the mode, map, and walls in the lobby; START opens when every seat is connected.", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 105), new Vector2(1100, 60), 22, TextAnchor.MiddleCenter);
            Button("Offline Arena", "MAIN MENU", setup, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(350, 78), () => SceneManager.LoadScene("PhaseOneArena"));
            var lobby = Rect("Online room", safe.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1280, 760));
            var lobbyImage = lobby.gameObject.AddComponent<Image>();
            UiSkin.Panel(lobbyImage); roomPanel = lobby.gameObject;
            roomHeading = Label("Room heading", "ROOM", lobby, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(1120, 75), 47, TextAnchor.MiddleCenter);
            roster = Label("Room roster", "", lobby, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 70), new Vector2(1040, 380), 29, TextAnchor.MiddleCenter);
            modeButton = Button("Room Mode", "MODE: ARENA", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-390, 165), new Vector2(360, 80), CycleMode);
            modeLabel = modeButton.GetComponentInChildren<Text>();
            mapButton = Button("Room Map", "MAP: CLASSIC GRID", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 165), new Vector2(360, 80), CycleMap);
            mapLabel = mapButton.GetComponentInChildren<Text>();
            wallsButton = Button("Room Walls", "WALLS: PERSISTENT", lobby, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(390, 165), new Vector2(360, 80), CycleWalls);
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
            ShowMap("classic");
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
        // The server accepts mode, map, and wall choices from the host in the lobby only.
        private async void CycleMode()
        {
            var current = room.View;
            if (current == null || current.phase != "lobby") return;
            string next = Modes[(Array.IndexOf(Modes, current.mode) + 1) % Modes.Length];
            await room.SendAsync(new OnlineRoomClient.ActionBody { type = "mode", mode = next });
        }
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
            if (current == null || current.phase != "lobby" || current.mode != "arena") return;
            await room.SendAsync("rule", rule: current.rule == "decay" ? "classic" : "decay");
        }
        private async void Leave() { await room.LeaveAsync(); ShowSetup(); }
        private async void StartHeat() { await room.SendAsync(room.View?.phase == "paused" ? "pause" : "start"); }
        private async void Pause() { await room.SendAsync("pause"); }
        private async void ToggleAuto()
        {
            autoDrive = !autoDrive;
            hud.SetAuto(autoDrive);
            hud.ClearHolds();
            hud.ShowToast(autoDrive ? "AUTO DRIVE ON" : "MANUAL DRIVE / HOLD FORWARD");
            await room.SendAsync("auto-drive", enabled: autoDrive);
        }
        private bool Running => room.View?.phase == "running" && room.Connected;
        private async void Turn(int sign)
        {
            if (Running) await room.SendAsync("turn", sign: sign);
        }
        private async void Flight(string kind)
        {
            if (!Running) return;
            if (Self(room.View)?.form == "foot") { hud.ShowToast("MOUNT TO FLY"); return; }
            await room.SendAsync(new OnlineRoomClient.ActionBody { type = "flight", kind = kind });
        }
        private async void Form()
        {
            if (!Running || room.View.mode != "shift") return;
            string refused = await room.SendAsync(new OnlineRoomClient.ActionBody { type = "form" });
            if (refused != null) hud.ShowToast(refused.TrimEnd('.').ToUpperInvariant());
        }
        private async void Disc()
        {
            if (!Running || room.View.mode != "shift") return;
            bool mounted = Self(room.View)?.form != "foot";
            string refused = await room.SendAsync(new OnlineRoomClient.ActionBody { type = "disc" });
            if (refused != null) hud.ShowToast(mounted ? "DISMOUNT TO THROW" : "DISC NOT READY");
        }

        private static OnlineRoomClient.RiderView Self(OnlineRoomClient.RoomView current) =>
            current?.round == null ? null : Array.Find(current.round.riders ?? new OnlineRoomClient.RiderView[0], r => r.id == current.selfId);

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
            var current = room.View;
            if (current != null && !ReferenceEquals(current, shown)) { shown = current; Present(current); }
            var self = Self(current);
            bool shift = current?.mode == "shift", foot = shift && self?.form == "foot";
            if (playHud.activeSelf) hud.Sync(HudFor(current, self), shift ? "shift" : "arena", Running);
            if (!foot && (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))) Turn(-1);
            if (!foot && (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))) Turn(1);
            if (Input.GetKeyDown(KeyCode.Q)) Flight("jet");
            if (Input.GetKeyDown(KeyCode.R)) Flight("sail");
            if (Input.GetKeyDown(KeyCode.F)) Form();
            if (Input.GetKeyDown(KeyCode.Space)) Disc();
            if (Input.GetKeyDown(KeyCode.Escape) && room.IsHost &&
                (current?.phase == "running" || current?.phase == "paused")) Pause();
            bool active = room.Connected && room.Supported && current?.phase == "running";
            bool throttle = active && !foot && (hud.ForwardHeld || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));
            bool firing = active && (hud.FireHeld || Input.GetKey(KeyCode.E));
            if (throttle != lastThrottle) { lastThrottle = throttle; _ = room.SendAsync("throttle", held: throttle); }
            if (firing != lastFire) { lastFire = firing; _ = room.SendAsync("fire", held: firing); }
            if (shift)
            {
                // On foot the pad moves in arena directions; screen up is north, which is -z in simulation coordinates.
                var move = active && foot ? hud.FootMove : Vector2.zero;
                if (active && foot)
                {
                    move.x += (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                    move.y += (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                }
                move.x = Mathf.Clamp(move.x, -1, 1); move.y = Mathf.Clamp(move.y, -1, 1);
                if (move != lastMove && (active || move == Vector2.zero))
                {
                    lastMove = move;
                    _ = room.SendAsync(new OnlineRoomClient.ActionBody { type = "move", x = move.x, z = -move.y });
                }
            }
        }

        private static HudPlayer HudFor(OnlineRoomClient.RoomView current, OnlineRoomClient.RiderView self)
        {
            if (self == null) return default;
            return new HudPlayer
            {
                Present = true, Alive = self.alive, RocketReady = self.rocketReady, Ammo = self.ammo,
                Form = string.IsNullOrEmpty(self.form) ? "cycle" : self.form,
                // JsonUtility reads a null flight kind as an empty string.
                FlightKind = string.IsNullOrEmpty(self.flightKind) ? null : self.flightKind,
                Elapsed = current.round.elapsed, JetReadyAt = self.jetReadyAt, SailReadyAt = self.sailReadyAt,
                FlightUntil = self.flightUntil, DiscReadyAt = self.discReadyAt, NitroUntil = self.nitroUntil,
                DiscOut = Array.Exists(current.round.shift?.discs ?? new OnlineRoomClient.DiscView[0], d => d.ownerId == self.id),
            };
        }

        private static string ModeName(string mode) => mode == "duel" ? "DUEL" : mode == "shift" ? "GRID SHIFT" : "ARENA";

        private void Present(OnlineRoomClient.RoomView current)
        {
            if (!room.Supported)
            {
                setupPanel.SetActive(false); roomPanel.SetActive(true); playHud.SetActive(false);
                roomHeading.text = "UNSUPPORTED ROOM";
                roster.text = room.Status + "\nLeave this room to create or join a supported match.";
                startButton.gameObject.SetActive(false);
                return;
            }
            bool active = current.phase == "running" || current.phase == "countdown";
            bool arena = current.mode == "arena";
            string mode = ModeName(current.mode), mapName = SimMaps.Get(current.map).Name.ToUpperInvariant();
            setupPanel.SetActive(false);
            if (active && !playHud.activeSelf) { EventSystem.current.SetSelectedGameObject(null); hud.ClearMessages(); }
            roomPanel.SetActive(!active);
            playHud.SetActive(active);
            roomHeading.text = "ROOM " + current.code + "  /  " + current.phase.ToUpperInvariant();
            string lines = mode + " / " + mapName + (arena ? " / " + WallsName(current.rule) : "") + " / " +
                current.players.Length + "/" + current.maxPlayers + " RIDERS\n" +
                (arena ? "THREE HEATS / MOST HEAT WINS TAKES THE MATCH\n" : current.mode == "shift" ?
                    "FIRST TO THREE / CYCLE OR ON FOOT / TWO DISC HITS DEREZ\n" : "FIRST TO THREE HEAT WINS\n");
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
            startLabel.text = current.phase == "paused" ? "RESUME" : current.phase == "match-result" ? "REMATCH" : current.phase == "round-result" ? "NEXT HEAT" : "START " + mode;
            bool choosing = host && current.phase == "lobby";
            modeButton.gameObject.SetActive(choosing); mapButton.gameObject.SetActive(choosing); wallsButton.gameObject.SetActive(choosing);
            modeLabel.text = "MODE: " + mode;
            mapLabel.text = "MAP: " + mapName;
            // Only Arena has fading walls; the server plays Duel and Grid Shift with persistent walls.
            wallsButton.interactable = arena;
            wallsLabel.text = "WALLS: " + (arena && current.rule == "decay" ? "FADING" : "PERSISTENT");
            hud.SetPauseVisible(host);
            hud.SetPaused(current.phase == "paused");
            hud.Matchline.text = mode + " / HEAT " + current.roundNumber + (arena ? "/3" : " / FIRST TO 3") + " / " + mapName;
            hud.Status.text = (room.Connected ? "CONNECTED" : "RECONNECTING") + "  /  " +
                string.Join("   ", Array.ConvertAll(current.players, p => Seat(current, p.id) + " " + p.wins));
            hud.Score.text = "ROOM " + current.code;
            hud.Countdown.text = current.phase == "countdown" ? Mathf.Max(1, Mathf.CeilToInt(current.countdownMs / 1000f)).ToString() : "";
            ShiftPanel(current);
            if (current.round != null && current.roundNumber != lastRoundNumber)
            {
                _ = room.SendAsync("auto-drive", enabled: autoDrive);
                hud.SetAuto(autoDrive);
            }
            lastRoundNumber = current.roundNumber;
            RenderRound(current);
        }

        // Integrity, form, and time left, like the solo Grid Shift panel but for every seat.
        private void ShiftPanel(OnlineRoomClient.RoomView current)
        {
            var round = current.round;
            if (current.mode != "shift" || round == null) { hud.SetShiftPanel(false); return; }
            var self = Self(current);
            double left = Math.Max(0, Math.Ceiling(round.shift.timeLimit - round.elapsed));
            string FormName(OnlineRoomClient.RiderView r) => r.form == "foot" ? "ON FOOT" : "CYCLE";
            string text = (self == null ? "" : "YOU " + Math.Max(0, self.hp) + "/2 INTEGRITY  /  " + FormName(self) + "      ") +
                ((int)left / 60) + ":" + ((int)left % 60).ToString("00") + "\n";
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
                if (r.id != current.selfId)
                    text += SeatColours[Math.Max(0, Array.FindIndex(current.players, p => p.id == r.id)) % SeatColours.Length] + " " +
                        Math.Max(0, r.hp) + "/2 " + (r.alive ? FormName(r) : "DEREZZED") + "    ";
            hud.SetShiftPanel(true, text.TrimEnd());
        }

        // Colour plus name, so riders who share a name stay distinct without relying on colour alone.
        private static string Seat(OnlineRoomClient.RoomView view, string id)
        {
            int i = Array.FindIndex(view.players, p => p.id == id);
            return i < 0 ? id : SeatColours[i % SeatColours.Length] + " " + view.players[i].name;
        }

        private static string WallsName(string rule) => rule == "decay" ? "FADING WALLS" : "PERSISTENT WALLS";

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
                    case "pickup-spawn": hud.ShowToast(item + " SPAWNED"); break;
                    case "pickup":
                        hud.ShowToast(e.riderId == current.selfId ? item + " PICKED UP" : Seat(current, e.riderId) + " TOOK " + item);
                        var picker = scene.Riders.Find(r => r.Id == e.riderId);
                        if (picker.Id != null)
                            world.Ring(picker.X, picker.Z, e.pickup == "ammo" ? new[] { .25f, .85f, 1f } : e.pickup == "nitro" ? new[] { .25f, 1f, .4f } : new[] { 1f, .54f, .18f }, 1.4f);
                        break;
                    case "rocket-explode":
                        world.Impact(e.x, e.z);
                        world.Ring(e.x, e.z, new[] { 1f, .48f, .13f }, 3.2f);
                        hud.ShowCaption("ROCKET IMPACT");
                        break;
                    case "arena-shrink": hud.ShowToast("GRID SHRINKING / " + Mathf.RoundToInt(e.limit)); break;
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
            var discs = current.mode == "shift" ? round.shift?.discs ?? new OnlineRoomClient.DiscView[0] : new OnlineRoomClient.DiscView[0];
            scene.Riders.Clear(); scene.Segments.Clear(); scene.Bullets.Clear(); scene.Pickups.Clear(); scene.Discs.Clear();
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
                scene.Riders.Add(new Solo.RiderView
                {
                    Id = r.id, X = r.x, Z = r.z, Dir = r.dir, Alive = r.alive, IsPlayer = r.id == current.selfId,
                    Form = string.IsNullOrEmpty(r.form) ? "cycle" : r.form, Style = r.style, Color = r.color,
                    FormChangedAt = r.formChangedAt, HasDiscOut = Array.Exists(discs, d => d.ownerId == r.id),
                    FlightKind = !string.IsNullOrEmpty(r.flightKind) && round.elapsed < r.flightUntil ? r.flightKind : null,
                    FlightStartedAt = r.flightStartedAt, FlightUntil = r.flightUntil,
                });
            foreach (var s in round.segments ?? new OnlineRoomClient.SegmentView[0])
                scene.Segments.Add(new Solo.SegmentView
                { Id = s.id, OwnerId = s.ownerId, X1 = s.x1, Z1 = s.z1, X2 = s.x2, Z2 = s.z2, Active = s.active, ClosedAt = s.closedAt });
            foreach (var b in round.bullets ?? new OnlineRoomClient.BulletView[0])
                scene.Bullets.Add(new Solo.BulletView { X = b.x, Z = b.z, Rocket = b.rocket, FromPlayer = b.ownerId == current.selfId, Vx = b.vx, Vz = b.vz });
            foreach (var p in round.pickups ?? new OnlineRoomClient.PickupView[0])
                if (p.active) scene.Pickups.Add(new Solo.PickupView { Id = p.id, Type = p.type, X = p.x, Z = p.z });
            foreach (var d in discs)
                scene.Discs.Add(new Solo.DiscView { X = d.x, Z = d.z, FromPlayer = d.ownerId == current.selfId });
            PlayEffects(current);
        }

        private void LateUpdate()
        {
            if (view == null) return;
            world.Render(scene, Time.deltaTime);
            if (playHud.activeSelf) hud.DrawRadar(scene, Time.deltaTime);
            Vector3 target = new Vector3(0, 65, -65), aim = Vector3.zero;
            var self = world.Player;
            if (room?.View?.phase == "running" && self != null && self.gameObject.activeSelf)
            {
                if (world.PlayerOnFoot) ChaseCamera.NorthUp(self.position, out target, out aim);
                else ChaseCamera.Target(self.position, self.rotation, out target, out aim);
            }
            ChaseCamera.Follow(view.transform, ref cameraAim, target, aim, Time.deltaTime);
        }
        private void OnApplicationPause(bool paused)
        {
            if (!paused || room == null) return;
            hud?.ClearHolds();
            lastThrottle = lastFire = false;
            _ = room.SendAsync("throttle", held: false);
            _ = room.SendAsync("fire", held: false);
            if (room.View?.mode == "shift") { lastMove = Vector2.zero; _ = room.SendAsync(new OnlineRoomClient.ActionBody { type = "move" }); }
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
