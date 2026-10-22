using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GridRun
{
    // Phase 1 offline Arena presentation. Match rules live in ArenaMatch.
    public sealed class ArenaGame : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        private readonly ArenaMatch match = new ArenaMatch();
        private readonly List<Transform> trailVisuals = new List<Transform>();
        private readonly Transform[] boundaryVisuals = new Transform[8];
        private Transform playerVisual, rivalVisual;
        private Camera view;
        private Vector3 cameraAim;
        private Font font;
        private HoldControl forward;
        private Text score, status, resultText, autoText, pauseText, graphicsText;
        private GameObject landing, resultPanel, pausePanel, playHud, settingsPanel, settingsClose, newGameButton;
        private Text autoDefaultText;
        private bool playing, paused, autoDrive = true;
        private bool ignoreHostFocusForVerification;

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = System.Environment.GetCommandLineArgs();
            ignoreHostFocusForVerification = System.Array.IndexOf(args, "--phase1-capture-dir") >= 0 ||
                System.Array.IndexOf(args, "--look-capture-dir") >= 0;
#endif
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = .1f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            {
                Debug.LogError("Phase 1 Arena setup is missing. Run Grid Run > Phase 1 > Prepare Arena.");
                enabled = false;
                return;
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildWorld();
            BuildUI();
            ShowLanding();
        }

        private GameObject Box(string name, Vector3 position, Vector3 size, Material material, Transform parent)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = size;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(obj.GetComponent<Collider>());
            return obj;
        }

        private Transform Bike(string name, Material light)
        {
            return CycleVisual.Create(transform, name, cycleMaterial, light).transform;
        }

        private void BuildWorld()
        {
            GridLook.Floor(transform, Vector3.zero, new Vector2(110, 110), 4, floorMaterial);
            GridLook.Stadium(transform, 52);
            GridLook.Skyline(transform, 120, 175, 70, 7);
            for (int edge = 0; edge < 2; edge++)
            {
                int sign = edge == 0 ? -1 : 1;
                boundaryVisuals[edge * 4] = Box("Wall X", new Vector3(sign * 49, 1, 0), new Vector3(.3f, 2, 100), GridLook.Assets.Boundary, transform).transform;
                boundaryVisuals[edge * 4 + 1] = Box("Wall Z", new Vector3(0, 1, sign * 49), new Vector3(100, 2, .3f), GridLook.Assets.Boundary, transform).transform;
                boundaryVisuals[edge * 4 + 2] = Box("Edge X", new Vector3(sign * 49, 2.03f, 0), new Vector3(.34f, .06f, 100), glowMaterial, transform).transform;
                boundaryVisuals[edge * 4 + 3] = Box("Edge Z", new Vector3(0, 2.03f, sign * 49), new Vector3(100, .06f, .34f), glowMaterial, transform).transform;
            }
            playerVisual = Bike("Player cycle", glowMaterial);
            rivalVisual = Bike("Rival cycle", amberMaterial);
            var cameraObject = new GameObject("Arena camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 260f; view.fieldOfView = 60f;
            GridLook.Stage(view);
        }

        private void ToggleGraphics()
        {
            GridLook.HighQuality = !GridLook.HighQuality;
            GridLook.ApplyQuality(view);
            graphicsText.text = GraphicsLabel();
        }

        private static string GraphicsLabel() => GridLook.HighQuality ? "HIGH" : "LOW";

        private const string AutoDriveKey = "gridrun.autodrive.v1";
        private static bool AutoDriveDefault
        {
            get { try { return PlayerPrefs.GetInt(AutoDriveKey, 1) == 1; } catch (System.Exception) { return true; } }
            set
            {
                try { PlayerPrefs.SetInt(AutoDriveKey, value ? 1 : 0); PlayerPrefs.Save(); }
                catch (System.Exception error) { Debug.LogWarning("Auto Drive preference could not be saved: " + error.GetType().Name); }
            }
        }

        private void ToggleAutoDefault()
        {
            AutoDriveDefault = !AutoDriveDefault;
            autoDefaultText.text = AutoDriveDefault ? "ON" : "OFF";
        }

        // Launch screen: title, primary actions, and settings over a slow orbit of the parked cycle.
        private void BuildMainMenu(Transform safe)
        {
            var root = Rect("Landing", safe, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            landing = root.gameObject;
            var shade = Rect("Menu shade", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            shade.anchorMin = Vector2.zero; shade.anchorMax = new Vector2(.72f, 1);
            UiSkin.Scrim(shade.gameObject.AddComponent<Image>());

            var kicker = Label("Menu kicker", "LIGHT CYCLE ARENA", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(114, -96), new Vector2(700, 36), 24);
            kicker.color = UiSkin.Accent;
            var title = Label("Menu title", "GRID RUN", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -128), new Vector2(900, 128), 112);
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(.92f, .99f, 1f);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.gameObject.AddComponent<Outline>().effectColor = new Color(.1f, .75f, 1f, .35f);
            var rule = Rect("Menu rule", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(114, -262), new Vector2(420, 3));
            var ruleImage = rule.gameObject.AddComponent<Image>();
            ruleImage.color = UiSkin.Accent; ruleImage.raycastTarget = false;
            Label("Menu tagline", "Ride the Grid. Outlast every rival.", root, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(114, -280), new Vector2(800, 40), 26).color = UiSkin.Muted;

            float y = 70;
            newGameButton = MenuButton("Start Arena", "NEW GAME", root, y, StartMatch, true).gameObject;
            if (Application.CanStreamedLevelBeLoaded("PhaseTwoOnline"))
                MenuButton("Online Room", "ONLINE GAME", root, y -= 100, () => SceneManager.LoadScene("PhaseTwoOnline"));
            if (Application.CanStreamedLevelBeLoaded("PhaseThreeTransit"))
                MenuButton("Solar Transit", "SOLAR TRANSIT", root, y -= 100, () => SceneManager.LoadScene("PhaseThreeTransit"));
            if (Application.CanStreamedLevelBeLoaded("PhaseFourShowroom"))
                MenuButton("Style Preview", "STYLE PREVIEW", root, y -= 100, () => SceneManager.LoadScene("PhaseFourShowroom"));
            MenuButton("Settings", "SETTINGS", root, y -= 100, OpenSettings);
            Label("Menu footer", "OFFLINE ARENA  ·  FIRST TO THREE HEATS  ·  v" + Application.version + " PREVIEW", root,
                Vector2.zero, Vector2.zero, new Vector2(114, 34), new Vector2(1100, 34), 20).color = UiSkin.Muted;

            var overlay = Rect("Settings", root, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one;
            overlay.gameObject.AddComponent<Image>().color = new Color(0, .004f, .01f, .86f); // dims and blocks the menu
            settingsPanel = overlay.gameObject;
            var panel = Rect("Settings panel", overlay, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1040, 700));
            UiSkin.Panel(panel.gameObject.AddComponent<Image>());
            Label("Settings heading", "SETTINGS", panel, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -34), new Vector2(940, 70), 46, TextAnchor.MiddleCenter).fontStyle = FontStyle.Bold;
            graphicsText = SettingRow(panel, -140, "Graphics", "GRAPHICS", "High adds floor reflections and glow. Low runs faster on older phones.", GraphicsLabel(), ToggleGraphics);
            autoDefaultText = SettingRow(panel, -275, "Auto Drive Default", "AUTO DRIVE", "Cycles move forward on their own. Turn off to hold FORWARD to move.", AutoDriveDefault ? "ON" : "OFF", ToggleAutoDefault);
            Label("Controls heading", "CONTROLS", panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -410), new Vector2(600, 40), 28).fontStyle = FontStyle.Bold;
            Label("Controls detail", "LEFT / RIGHT turn relative to your cycle. FORWARD moves in manual drive. PAUSE stops the heat at any time.\nKeyboard: A / D or arrows to turn, W to move, Esc to pause.",
                panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -455), new Vector2(920, 110), 23).color = UiSkin.Muted;
            settingsClose = Button("Close Settings", "BACK", panel, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 40), new Vector2(360, 86), CloseSettings, true).gameObject;
            settingsPanel.SetActive(false);
        }

        private Text SettingRow(RectTransform panel, float y, string name, string title, string detail, string value, UnityEngine.Events.UnityAction action)
        {
            Label(name + " title", title, panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(600, 40), 28).fontStyle = FontStyle.Bold;
            Label(name + " detail", detail, panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y - 44), new Vector2(660, 70), 23).color = UiSkin.Muted;
            return Button(name, value, panel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y - 10), new Vector2(220, 84), action).GetComponentInChildren<Text>();
        }

        private void OpenSettings()
        {
            settingsPanel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(settingsClose);
        }

        private void CloseSettings()
        {
            settingsPanel.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(newGameButton);
        }

        private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot; rect.anchoredPosition = offset; rect.sizeDelta = size;
            return rect;
        }

        private Text Label(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, int pointSize, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font; label.fontSize = pointSize; label.text = value;
            label.color = new Color(.85f, .97f, 1); label.alignment = align;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private Button Button(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            UiSkin.Button(button, image, primary);
            if (action != null) button.onClick.AddListener(action);
            Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 26, TextAnchor.MiddleCenter);
            return button;
        }

        // Main-menu entry: left-aligned label with a luminous marker bar.
        private Button MenuButton(string name, string value, Transform parent, float y, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var size = new Vector2(540, 84);
            var button = Button(name, value, parent, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(110, y), size, action, primary);
            var label = button.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = 30;
            label.fontStyle = FontStyle.Bold;
            label.rectTransform.anchoredPosition = new Vector2(58, 0);
            label.rectTransform.sizeDelta = size - new Vector2(80, 0);
            var marker = Rect(name + " marker", button.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(26, 0), new Vector2(5, 34));
            var markerImage = marker.gameObject.AddComponent<Image>();
            markerImage.color = primary ? UiSkin.Accent : new Color(.18f, .55f, .68f);
            markerImage.raycastTarget = false;
            return button;
        }

        private void TurnButton(Button button, int direction)
        {
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var pointer = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            pointer.callback.AddListener(_ => Turn(direction));
            trigger.triggers.Add(pointer);
            var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
            submit.callback.AddListener(_ => Turn(direction));
            trigger.triggers.Add(submit);
        }

        private GameObject Overlay(string name, Transform safe, string heading, string detail)
        {
            var rect = Rect(name, safe, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 570));
            UiSkin.Panel(rect.gameObject.AddComponent<Image>());
            Label(name + " heading", heading, rect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -35), new Vector2(780, 75), 50, TextAnchor.MiddleCenter);
            Label(name + " details", detail, rect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -135), new Vector2(770, 160), 27, TextAnchor.MiddleCenter);
            return rect.gameObject;
        }

        private void BuildUI()
        {
            new GameObject("UI input", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = new GameObject("Arena UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("Safe area", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvas.transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            var hud = Rect("Play HUD", safe.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            hud.anchorMin = Vector2.zero; hud.anchorMax = Vector2.one;
            playHud = hud.gameObject;
            var top = Rect("Top contrast", hud, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(2200, 180));
            var topImage = top.gameObject.AddComponent<Image>();
            topImage.color = new Color(.005f, .015f, .03f, .85f); topImage.raycastTarget = false;
            score = Label("Score", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -18), new Vector2(1000, 50), 37);
            status = Label("Status", "", hud, new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -80), new Vector2(1260, 70), 26);
            pauseText = Button("Pause", "PAUSE", hud, Vector2.one, Vector2.one, new Vector2(-32, -30), new Vector2(190, 92), TogglePause).GetComponentInChildren<Text>();
            TurnButton(Button("Left", "LEFT", hud, Vector2.zero, Vector2.zero, new Vector2(36, 36), new Vector2(230, 150), null), -1);
            TurnButton(Button("Right", "RIGHT", hud, Vector2.zero, Vector2.zero, new Vector2(290, 36), new Vector2(230, 150), null), 1);
            forward = Button("Forward", "FORWARD", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 36), new Vector2(280, 150), null).gameObject.AddComponent<HoldControl>();
            autoText = Button("Auto Drive", "AUTO: ON", hud, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 210), new Vector2(280, 85), ToggleAuto).GetComponentInChildren<Text>();
            Label("Control hint", "A/D or arrows: steer  |  W: forward  |  Esc: pause", hud, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 205), new Vector2(900, 60), 23, TextAnchor.MiddleCenter);
            BuildMainMenu(safe.transform);
            resultPanel = Overlay("Result", safe.transform, "HEAT COMPLETE", "");
            resultText = resultPanel.transform.Find("Result details").GetComponent<Text>();
            Button("Next Heat", "NEXT HEAT", resultPanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 45), new Vector2(440, 95), NextHeat);
            pausePanel = Overlay("Pause menu", safe.transform, "PAUSED", "The Arena is paused. Your steering input has been cleared.");
            Button("Resume", "RESUME", pausePanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-230, 45), new Vector2(400, 95), TogglePause);
            Button("Main Menu", "MAIN MENU", pausePanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(230, 45), new Vector2(400, 95), ShowLanding);
        }

        private void ShowLanding()
        {
            playing = false; paused = false;
            match.NewMatch();
            ClearTrails();
            if (forward != null) forward.Clear();
            playHud.SetActive(false); resultPanel.SetActive(false); pausePanel.SetActive(false);
            settingsPanel.SetActive(false); landing.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(newGameButton);
            SnapCamera();
        }

        public void StartMatch()
        {
            match.NewMatch();
            ClearTrails();
            autoDrive = AutoDriveDefault; autoText.text = autoDrive ? "AUTO: ON" : "AUTO: OFF";
            EventSystem.current?.SetSelectedGameObject(null);
            playing = true; paused = false;
            landing.SetActive(false); resultPanel.SetActive(false); pausePanel.SetActive(false); playHud.SetActive(true);
            if (forward != null) forward.Clear();
            SnapCamera();
        }

        public void NextHeat()
        {
            if (match.MatchEnded) match.NewMatch(); else match.BeginHeat();
            ClearTrails();
            resultPanel.SetActive(false); playHud.SetActive(true);
            paused = false; playing = true;
            if (forward != null) forward.Clear();
            SnapCamera();
        }

        private void ToggleAuto()
        {
            autoDrive = !autoDrive;
            forward.Clear();
            autoText.text = autoDrive ? "AUTO: ON" : "AUTO: OFF";
        }

        public void TogglePause()
        {
            if (!playing || match.HeatEnded) return;
            paused = !paused;
            forward.Clear();
            pausePanel.SetActive(paused);
            pauseText.text = paused ? "RESUME" : "PAUSE";
        }

        private void Turn(int direction)
        {
            if (playing && !paused && !match.HeatEnded) match.TurnPlayer(direction);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (settingsPanel.activeSelf) CloseSettings();
                else TogglePause();
            }
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Turn(-1);
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Turn(1);
            if (Input.GetKeyDown(KeyCode.Return) && match.HeatEnded && playing) NextHeat();
            if (!playing) return;
            score.text = $"YOU {match.PlayerScore}  :  {match.OpponentScore} RIVAL   /   HEAT {match.Heat}";
            status.text = match.Elapsed >= ArenaMatch.ShrinkStartsAt ? "BOUNDARY CLOSING" :
                match.Elapsed >= ArenaMatch.ShrinkStartsAt - 10 ? "WARNING: BOUNDARY CLOSES IN " + Mathf.CeilToInt(ArenaMatch.ShrinkStartsAt - match.Elapsed) + " SECONDS" :
                autoDrive ? "AUTO DRIVE  /  AVOID LIGHT TRAILS" : "MANUAL DRIVE  /  HOLD FORWARD TO MOVE";
        }

        private void FixedUpdate()
        {
            if (!playing || paused || match.HeatEnded) return;
            match.Step(Time.fixedDeltaTime, autoDrive || forward.Held || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));
            if (!match.HeatEnded) return;
            forward.Clear();
            resultText.text = match.Result + "\n" +
                (match.PlayerCrash.Length > 0 ? "YOU: " + match.PlayerCrash + "\n" : "") +
                (match.OpponentCrash.Length > 0 ? "RIVAL: " + match.OpponentCrash + "\n" : "") +
                $"SCORE  {match.PlayerScore} : {match.OpponentScore}";
            resultPanel.transform.Find("Next Heat").GetComponentInChildren<Text>().text = match.MatchEnded ? "PLAY AGAIN" : "NEXT HEAT";
            resultPanel.SetActive(true);
        }

        private void LateUpdate()
        {
            PositionBike(playerVisual, match.Player);
            PositionBike(rivalVisual, match.Opponent);
            SyncTrails();
            SyncBoundary();
            // Move away from the wall on a crash so near-field boundary geometry
            // cannot cover the result card while the player is at the edge.
            if (!playing && landing.activeSelf) { OrbitLanding(); return; }
            Vector3 target = new Vector3(0, 55, -55), aim = Vector3.zero;
            if (!match.HeatEnded) ChaseCamera.Target(playerVisual.position, playerVisual.rotation, out target, out aim);
            ChaseCamera.Follow(view.transform, ref cameraAim, target, aim, Time.deltaTime);
        }

        // Slow showcase orbit of the parked cycle, framed to the right of the menu column.
        private void OrbitLanding()
        {
            float angle = (Time.unscaledTime * 6f + 35f) * Mathf.Deg2Rad;
            Vector3 bike = playerVisual.position;
            Vector3 position = bike + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 7.5f + Vector3.up * 2.3f;
            Vector3 look = bike + Vector3.up * .75f;
            Vector3 right = Vector3.Cross(Vector3.up, (look - position).normalized).normalized;
            view.transform.position = position;
            view.transform.LookAt(look - right * 2.4f);
            cameraAim = look;
        }

        private static void PositionBike(Transform visual, ArenaMatch.Rider rider)
        {
            visual.position = new Vector3(rider.X, 0, rider.Z);
            visual.rotation = Quaternion.Euler(0, rider.Heading * 90, 0);
            visual.gameObject.SetActive(!rider.Crashed);
        }

        private void SyncTrails()
        {
            for (int i = 0; i < match.Trails.Count; i++)
            {
                var trail = match.Trails[i];
                if (trailVisuals.Count <= i)
                    trailVisuals.Add(Box("Light trail", Vector3.zero, Vector3.one, GridLook.Trail(trail.Owner == 0), transform).transform);
                var visual = trailVisuals[i];
                visual.GetComponent<Renderer>().sharedMaterial = GridLook.Trail(trail.Owner == 0);
                visual.position = new Vector3((trail.X0 + trail.X1) * .5f, .55f, (trail.Z0 + trail.Z1) * .5f);
                visual.localScale = new Vector3(Mathf.Max(.14f, Mathf.Abs(trail.X1 - trail.X0)), 1.1f, Mathf.Max(.14f, Mathf.Abs(trail.Z1 - trail.Z0)));
            }
        }

        private void SyncBoundary()
        {
            float extent = match.Limit + .4f;
            float span = match.Limit * 2 + 1;
            for (int edge = 0; edge < 2; edge++)
            {
                int sign = edge == 0 ? -1 : 1;
                boundaryVisuals[edge * 4].localPosition = new Vector3(sign * extent, 1, 0);
                boundaryVisuals[edge * 4].localScale = new Vector3(.3f, 2, span);
                boundaryVisuals[edge * 4 + 1].localPosition = new Vector3(0, 1, sign * extent);
                boundaryVisuals[edge * 4 + 1].localScale = new Vector3(span, 2, .3f);
                boundaryVisuals[edge * 4 + 2].localPosition = new Vector3(sign * extent, 2.03f, 0);
                boundaryVisuals[edge * 4 + 2].localScale = new Vector3(.34f, .06f, span);
                boundaryVisuals[edge * 4 + 3].localPosition = new Vector3(0, 2.03f, sign * extent);
                boundaryVisuals[edge * 4 + 3].localScale = new Vector3(span, .06f, .34f);
            }
        }

        private void ClearTrails()
        {
            foreach (var visual in trailVisuals) Destroy(visual.gameObject);
            trailVisuals.Clear();
        }

        private void SnapCamera()
        {
            if (view == null) return;
            ChaseCamera.Target(new Vector3(match.Player.X, 0, match.Player.Z), Quaternion.Euler(0, match.Player.Heading * 90, 0),
                out var position, out var aim);
            ChaseCamera.Snap(view.transform, ref cameraAim, position, aim);
        }

        private void Suspend()
        {
            if (playing && !match.HeatEnded && !paused) TogglePause();
            if (forward != null) forward.Clear();
        }
        private void OnApplicationPause(bool background) { if (background) Suspend(); }
        private void OnApplicationFocus(bool focused) { if (!focused && !ignoreHostFocusForVerification) Suspend(); }
        private void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
