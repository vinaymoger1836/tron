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
        // Snapshots arrive about 20 times a second. The drawn view blends the previous snapshot toward
        // the latest over one snapshot interval, so riders and trails move every frame (about 50 ms behind).
        private readonly SceneView latest = new SceneView(), earlier = new SceneView(), drawn = new SceneView();
        private bool blend;
        private int blendRound = -1;
        private float arrivedAt, interval = .05f;
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
        private SoloAudio sound;
        // Previous snapshot per rider, so sound cues can play for what changed.
        private readonly Dictionary<string, OnlineRoomClient.RiderView> previous = new Dictionary<string, OnlineRoomClient.RiderView>();
        private readonly HashSet<string> discOwners = new HashSet<string>();
        private int cueRound = -1, lastCountdown;
        private string lastPhase;
        private float goUntil;

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
            sound = gameObject.AddComponent<SoloAudio>();
            sound.Enabled = SoloProfile.Sound;
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
            latest.Riders.Clear(); latest.Segments.Clear(); latest.Bullets.Clear(); latest.Pickups.Clear(); latest.Discs.Clear();
            lastEffectId = 0;
            blend = false; blendRound = -1;
            latest.Map = mapId; latest.Rule = "classic"; latest.Elapsed = 0;
            latest.Limit = SimMaps.Get(mapId).Limit; latest.TrailLifetime = double.PositiveInfinity;
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
                bool unavailable = room.Unavailable;
                roomHeading.text = "ROOM " + room.Code + (unavailable ? "  /  UNAVAILABLE" : "  /  RECONNECTING");
                roster.text = room.Status + (unavailable ? "\nLeave this room to start again." :
                    "\nOffline for " + room.OfflineSeconds + " s. The server holds your seat for " + room.HoldSeconds + " s.");
            }
            var current = room.View;
            if (current != null && !ReferenceEquals(current, shown)) { shown = current; Present(current); }
            if (goUntil > 0 && Time.unscaledTime > goUntil) { if (current?.phase == "running") hud.Countdown.text = ""; goUntil = 0; }
            bool inHeat = room.Connected && (current?.phase == "running" || current?.phase == "countdown");
            sound.Music(inHeat ? MusicIntensity(current) : (float?)null);
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
            if (current.phase == "connection-wait")
            {
                var away = Array.FindAll(current.players, p => !p.connected);
                lines += "Waiting up to " + Mathf.CeilToInt(current.waitMs / 1000f) + " s for " +
                    (away.Length == 0 ? "a rider" : string.Join(", ", Array.ConvertAll(away, p => Seat(current, p.id)))) + " to reconnect.\n";
            }
            // A released seat leaves the room with fewer riders than a match needs.
            if (current.phase != "lobby" && current.players.Length < 2) lines += "Every other rider has left. Leave this room to start a new one.\n";
            if (current.result != null && (current.phase == "round-result" || current.phase == "match-result"))
            {
                // JsonUtility reads a null winner as an empty string.
                lines += string.IsNullOrEmpty(current.result.winnerId) ? "HEAT DRAW\n" : Seat(current, current.result.winnerId) + " WINS THE HEAT\n";
                if (current.result.crashes != null)
                    foreach (var crash in current.result.crashes)
                        lines += (Array.Exists(current.players, p => p.id == crash.riderId) || string.IsNullOrEmpty(crash.name) ?
                            Seat(current, crash.riderId) : crash.name) + ": " + crash.reason + "\n";
                if (current.phase == "match-result")
                    lines += current.result.matchDraw || string.IsNullOrEmpty(current.result.matchWinnerId) ? "MATCH DRAW" :
                        Seat(current, current.result.matchWinnerId) + " WINS THE MATCH";
            }
            roster.text = lines;
            bool host = current.selfId == current.hostId;
            startButton.gameObject.SetActive(host && (current.phase == "lobby" || current.phase == "round-result" || current.phase == "match-result" || current.phase == "paused"));
            startButton.interactable = current.players.Length >= 2 && current.players.Length == current.maxPlayers && Array.TrueForAll(current.players, p => p.connected);
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
            // GO stays up briefly after the heat starts; Update clears it.
            if (current.phase == "countdown") hud.Countdown.text = Mathf.Max(1, Mathf.CeilToInt(current.countdownMs / 1000f)).ToString();
            else if (current.phase != "running") hud.Countdown.text = "";
            ShiftPanel(current);
            if (current.round != null && current.roundNumber != lastRoundNumber)
            {
                _ = room.SendAsync("auto-drive", enabled: autoDrive);
                hud.SetAuto(autoDrive);
            }
            lastRoundNumber = current.roundNumber;
            RenderRound(current);
            PlayCues(current);
        }

        // The server forwards no events for these, so the solo game's cues play for what changed
        // between snapshots: countdown, start, own turns, gunfire, flight, forms, discs, and crashes.
        private void PlayCues(OnlineRoomClient.RoomView current)
        {
            string phase = current.phase;
            if (phase == "countdown")
            {
                int number = Mathf.Max(1, Mathf.CeilToInt(current.countdownMs / 1000f));
                if (number != lastCountdown) { lastCountdown = number; sound.Beep(440 + (3 - number) * 95, .08, "triangle", .03); }
            }
            else lastCountdown = 0;
            if (phase == "running" && lastPhase == "countdown")
            {
                hud.Countdown.text = "GO";
                goUntil = Time.unscaledTime + .42f;
                sound.Beep(730, .2, "triangle", .06);
                sound.Motif("wall");
            }
            if ((phase == "round-result" || phase == "match-result") && phase != lastPhase && current.result?.winnerId == current.selfId)
                sound.Motif("win");
            lastPhase = phase;
            var round = current.round;
            if (round == null || current.roundNumber != cueRound) { previous.Clear(); discOwners.Clear(); cueRound = round == null ? -1 : current.roundNumber; }
            if (round == null) return;
            var owners = new HashSet<string>();
            foreach (var d in round.shift?.discs ?? new OnlineRoomClient.DiscView[0]) owners.Add(d.ownerId);
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
            {
                bool you = r.id == current.selfId;
                if (previous.TryGetValue(r.id, out var p))
                {
                    if (p.alive && !r.alive)
                    {
                        world.Impact(r.x, r.z);
                        world.Derez(r.x, r.z, r.color ?? GridRules.Colors.Player, false);
                        sound.Motif("crash");
                        sound.Motif("derez");
                        if (!you) hud.ShowToast(Seat(current, r.id) + " DEREZZED");
                    }
                    if (r.ammo < p.ammo || (p.rocketReady && !r.rocketReady)) sound.Beep(you ? 930 : 690, .1, "sawtooth", .028);
                    if (you && r.alive && r.dir != p.dir && r.form != "foot") sound.Motif("turn");
                    if (you && string.IsNullOrEmpty(p.flightKind) && !string.IsNullOrEmpty(r.flightKind))
                    {
                        bool jet = r.flightKind == "jet";
                        hud.ShowToast(jet ? "LIGHT JET / LIFT" : "SOLAR SAIL / GLIDE");
                        hud.ShowCaption(jet ? "LIGHT JET" : "SOLAR SAIL");
                        sound.Beep(jet ? 840 : 610, .15, "triangle", .025);
                    }
                    if (!string.IsNullOrEmpty(p.form) && p.form != r.form)
                    {
                        hud.ShowToast((you ? "YOU" : Seat(current, r.id)) + (r.form == "foot" ? " DISMOUNTED" : " REMOUNTED"));
                        sound.Beep(you ? 650 : 460, .18, "triangle", .03);
                    }
                    if (owners.Contains(r.id) && !discOwners.Contains(r.id)) sound.Beep(you ? 780 : 580, .13, "sawtooth", .035);
                }
                previous[r.id] = r;
            }
            discOwners.Clear();
            discOwners.UnionWith(owners);
        }

        // Same rule as the solo bed: closer rivals, a longer heat, and a closer match raise the intensity.
        private static float MusicIntensity(OnlineRoomClient.RoomView current)
        {
            var round = current.round;
            var self = Self(current);
            if (current.phase == "countdown" || round == null || self == null) return .22f;
            double nearest = 20;
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
                if (r.id != self.id && r.alive) nearest = Math.Min(nearest, Math.Sqrt((r.x - self.x) * (r.x - self.x) + (r.z - self.z) * (r.z - self.z)));
            double pressure = Math.Max(0, 1 - nearest / 16), duration = Math.Min(1, round.elapsed / 38);
            int wins = 0;
            foreach (var p in current.players) wins += p.wins;
            double stakes = current.mode == "arena" ? 0 : Math.Min(1, wins / 4.0);
            return (float)Math.Min(1, .25 + duration * .2 + pressure * .35 + stakes * .2);
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
                        if (e.riderId == current.selfId) sound.Beep(980, .12, "triangle", .03);
                        var picker = latest.Riders.Find(r => r.Id == e.riderId);
                        if (picker.Id != null)
                            world.Ring(picker.X, picker.Z, e.pickup == "ammo" ? new[] { .25f, .85f, 1f } : e.pickup == "nitro" ? new[] { .25f, 1f, .4f } : new[] { 1f, .54f, .18f }, 1.4f);
                        break;
                    case "rocket-explode":
                        world.Impact(e.x, e.z);
                        world.Ring(e.x, e.z, new[] { 1f, .48f, .13f }, 3.2f);
                        hud.ShowCaption("ROCKET IMPACT");
                        sound.Motif("crash");
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
                if (latest.Map != current.map || latest.Riders.Count > 0) ShowMap(current.map);
                return;
            }
            // Blend only within one heat; the measured gap (clamped, so a pause does not stretch it) sets the pace.
            float now = Time.unscaledTime;
            blend = current.roundNumber == blendRound;
            if (blend)
            {
                earlier.CopyFrom(latest);
                interval = Mathf.Clamp(Mathf.Lerp(interval, now - arrivedAt, .2f), .03f, .12f);
            }
            blendRound = current.roundNumber;
            arrivedAt = now;
            latest.Map = round.map; latest.Rule = round.rule; latest.Elapsed = round.elapsed;
            latest.Limit = round.limit > 0 ? round.limit : SimMaps.Get(round.map).Limit;
            // JsonUtility reads the null lifetime of persistent walls as 0.
            latest.TrailLifetime = round.rule == "decay" && round.trailLifetime > 0 ? round.trailLifetime : double.PositiveInfinity;
            var discs = current.mode == "shift" ? round.shift?.discs ?? new OnlineRoomClient.DiscView[0] : new OnlineRoomClient.DiscView[0];
            latest.Riders.Clear(); latest.Segments.Clear(); latest.Bullets.Clear(); latest.Pickups.Clear(); latest.Discs.Clear();
            foreach (var r in round.riders ?? new OnlineRoomClient.RiderView[0])
                latest.Riders.Add(new Solo.RiderView
                {
                    Id = r.id, X = r.x, Z = r.z, Dir = r.dir, Alive = r.alive, IsPlayer = r.id == current.selfId,
                    Form = string.IsNullOrEmpty(r.form) ? "cycle" : r.form, Style = r.style, Color = r.color,
                    FormChangedAt = r.formChangedAt, HasDiscOut = Array.Exists(discs, d => d.ownerId == r.id),
                    FlightKind = !string.IsNullOrEmpty(r.flightKind) && round.elapsed < r.flightUntil ? r.flightKind : null,
                    FlightStartedAt = r.flightStartedAt, FlightUntil = r.flightUntil,
                });
            foreach (var s in round.segments ?? new OnlineRoomClient.SegmentView[0])
                latest.Segments.Add(new Solo.SegmentView
                { Id = s.id, OwnerId = s.ownerId, X1 = s.x1, Z1 = s.z1, X2 = s.x2, Z2 = s.z2, Active = s.active, ClosedAt = s.closedAt });
            foreach (var b in round.bullets ?? new OnlineRoomClient.BulletView[0])
                latest.Bullets.Add(new Solo.BulletView { X = b.x, Z = b.z, Rocket = b.rocket, FromPlayer = b.ownerId == current.selfId, Vx = b.vx, Vz = b.vz });
            foreach (var p in round.pickups ?? new OnlineRoomClient.PickupView[0])
                if (p.active) latest.Pickups.Add(new Solo.PickupView { Id = p.id, Type = p.type, X = p.x, Z = p.z });
            foreach (var d in discs)
                latest.Discs.Add(new Solo.DiscView { X = d.x, Z = d.z, FromPlayer = d.ownerId == current.selfId });
            PlayEffects(current);
        }

        private void LateUpdate()
        {
            if (view == null) return;
            if (blend) drawn.Interpolate(earlier, latest, Mathf.Clamp01((Time.unscaledTime - arrivedAt) / interval));
            else drawn.CopyFrom(latest);
            world.Render(drawn, Time.deltaTime);
            if (playHud.activeSelf) hud.DrawRadar(drawn, Time.deltaTime);
            Vector3 target = new Vector3(0, 65, -65), aim = Vector3.zero;
            var self = world.Player;
            if (room?.View?.phase == "running" && self != null && self.gameObject.activeSelf)
            {
                if (world.PlayerOnFoot) ChaseCamera.NorthUp(self.position, out target, out aim);
                else ChaseCamera.Target(self.position, self.rotation, out target, out aim);
            }
            ChaseCamera.Follow(view.transform, ref cameraAim, target, aim, Time.deltaTime);
        }
        private void ReleaseHolds()
        {
            if (room == null) return;
            hud?.ClearHolds();
            lastThrottle = lastFire = false;
            _ = room.SendAsync("throttle", held: false);
            _ = room.SendAsync("fire", held: false);
            if (room.View?.mode == "shift") { lastMove = Vector2.zero; _ = room.SendAsync(new OnlineRoomClient.ActionBody { type = "move" }); }
        }
        // In the background the rider cannot steer, so the stream closes and the server holds the heat
        // for everyone until this seat reconnects or the hold runs out.
        private void OnApplicationPause(bool paused)
        {
            if (room == null) return;
            if (!paused) { room.Resume(); return; }
            ReleaseHolds();
            room.Suspend();
        }
        // Losing focus alone (a notification shade or dialog) only releases held controls.
        private void OnApplicationFocus(bool focused)
        {
            if (!focused && !verificationNoFocusPause) ReleaseHolds();
        }
        private void OnDestroy()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
        }
    }
}
