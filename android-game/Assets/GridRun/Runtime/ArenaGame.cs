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
        private Font font;
        private HoldControl forward;
        private Text score, status, resultText, autoText, pauseText, graphicsText;
        private GameObject landing, resultPanel, pausePanel, playHud;
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

        private static string GraphicsLabel() => GridLook.HighQuality ? "GFX: HIGH" : "GFX: LOW";

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

        private Button Button(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            UiSkin.Round(image);
            image.color = new Color(.025f, .16f, .23f, .97f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(.3f, .63f, .72f);
            colors.pressedColor = new Color(.15f, .45f, .58f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 26, TextAnchor.MiddleCenter);
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
            var image = rect.gameObject.AddComponent<Image>();
            UiSkin.Round(image);
            image.color = new Color(.006f, .025f, .045f, .97f);
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
            landing = Overlay("Landing", safe.transform, "GRID RUN  /  ARENA", "Offline Arena vs AI\nFirst to three heats wins. Avoid walls and both light trails.\nSteer with LEFT / RIGHT; Auto Drive starts on.");
            bool onlineAvailable = Application.CanStreamedLevelBeLoaded("PhaseTwoOnline");
            Button("Start Arena", "START ARENA", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(onlineAvailable ? -225 : 0, 45), new Vector2(420, 95), StartMatch);
            if (onlineAvailable)
                Button("Online Room", "ONLINE ROOM", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
                    new Vector2(225, 45), new Vector2(420, 95), () => SceneManager.LoadScene("PhaseTwoOnline"));
            if (Application.CanStreamedLevelBeLoaded("PhaseThreeTransit"))
                Button("Solar Transit", "SOLAR TRANSIT", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
                    new Vector2(0, 165), new Vector2(420, 85), () => SceneManager.LoadScene("PhaseThreeTransit"));
            if (Application.CanStreamedLevelBeLoaded("PhaseFourShowroom"))
                Button("Style Preview", "STYLE PREVIEW", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
                    new Vector2(0, 275), new Vector2(420, 85), () => SceneManager.LoadScene("PhaseFourShowroom"));
            graphicsText = Button("Graphics", GraphicsLabel(), landing.transform, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-25, 165), new Vector2(200, 85), ToggleGraphics).GetComponentInChildren<Text>();
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
            playHud.SetActive(false); resultPanel.SetActive(false); pausePanel.SetActive(false); landing.SetActive(true);
            SnapCamera();
        }

        public void StartMatch()
        {
            match.NewMatch();
            ClearTrails();
            autoDrive = true; autoText.text = "AUTO: ON";
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
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
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
            var target = match.HeatEnded ? new Vector3(0, 55, -55) : playerVisual.position + new Vector3(0, 18, -20);
            view.transform.position = Vector3.Lerp(view.transform.position, target, 1 - Mathf.Exp(-7 * Time.deltaTime));
            view.transform.rotation = Quaternion.Euler(45, 0, 0);
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
            view.transform.position = new Vector3(match.Player.X, 18, match.Player.Z - 20);
            view.transform.rotation = Quaternion.Euler(45, 0, 0);
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
