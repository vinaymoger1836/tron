using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // Standalone Phase 3 traversal presentation; TransitRun owns timing and gap outcomes.
    public sealed class TransitGame : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        private const string BestKey = "gridrun.transit.best.v1";
        private TransitRun run = new TransitRun();
        private Camera view;
        private Font font;
        private Transform cycle;
        private CycleVisual cycleArt;
        private GameObject landing, pausePanel, resultPanel, playHud;
        private HoldControl forward;
        private Text hud, status, autoText, resultText, bestText;
        private bool playing, paused, autoDrive = true, ignoreFocusForCapture;
        public TransitRun Run => run;
        public int RecordMilliseconds => PlayerPrefs.GetInt(BestKey, 0);

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ignoreFocusForCapture = Array.IndexOf(Environment.GetCommandLineArgs(), "--phase3-capture-dir") >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), "--look-capture-dir") >= 0;
#endif
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = .1f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            {
                Debug.LogError("Phase 3 Solar Transit scene is not prepared.");
                enabled = false;
                return;
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildWorld();
            BuildUI();
            ShowLanding();
        }

        private GameObject Box(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(obj.GetComponent<Collider>());
            return obj;
        }

        private void Platform(string name, float from, float to)
        {
            float center = (from + to) * .5f, length = to - from;
            GridLook.Floor(transform, new Vector3(0, 0, center), new Vector2(104, length), 8, floorMaterial).name = name;
            Box(name + " structure", new Vector3(0, -.32f, center), new Vector3(104, .6f, length), wallMaterial, transform);
        }

        private void BuildWorld()
        {
            Platform("Start platform", -52, TransitRun.JetGapStart);
            Platform("Mid platform", TransitRun.JetGapEnd, TransitRun.SailGapStart);
            Platform("Finish platform", TransitRun.SailGapEnd, 52);
            Box("Jet void", new Vector3(0, -2.3f, -12.5f), new Vector3(104, .3f, 5), wallMaterial, transform);
            Box("Sail void", new Vector3(0, -2.3f, 7), new Vector3(104, .3f, 10), wallMaterial, transform);
            foreach (float z in new[] { TransitRun.JetGapStart, TransitRun.JetGapEnd })
                Box("Amber jet gap edge", new Vector3(0, .02f, z), new Vector3(104, .06f, .18f), amberMaterial, transform);
            foreach (float z in new[] { TransitRun.SailGapStart, TransitRun.SailGapEnd })
                Box("Cyan sail gap edge", new Vector3(0, .02f, z), new Vector3(104, .06f, .18f), glowMaterial, transform);
            Box("Finish gate", new Vector3(0, .04f, TransitRun.FinishZ), new Vector3(104, .18f, .5f), glowMaterial, transform);
            foreach (float x in new[] { -52f, 52f })
            {
                Box("Course side rail", new Vector3(x, .6f, 0), new Vector3(.35f, 1.2f, 104), wallMaterial, transform);
                Box("Course rail light", new Vector3(x, 1.22f, 0), new Vector3(.38f, .05f, 104), glowMaterial, transform);
            }
            GridLook.Stadium(transform, 58);
            GridLook.Skyline(transform, 125, 185, 80, 23);
            cycleArt = CycleVisual.Create(transform, "Transit cycle", cycleMaterial, glowMaterial);
            cycle = cycleArt.transform;
            cycle.localScale = Vector3.one * 1.4f;
            var cameraObject = new GameObject("Transit camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 260; view.fieldOfView = 55;
            view.transform.rotation = Quaternion.Euler(26, 0, 0);
            GridLook.Stage(view);
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

        private Text Label(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, int pointSize, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = pointSize; text.text = value;
            text.color = new Color(.86f, .97f, 1); text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.raycastTarget = false;
            return text;
        }

        private Button Button(string name, string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(name, parent, anchor, pivot, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            UiSkin.Button(button, image);
            if (action != null) button.onClick.AddListener(action);
            Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 28, TextAnchor.MiddleCenter);
            return button;
        }

        private GameObject Overlay(string name, Transform parent, string title, string detail)
        {
            var rect = Rect(name, parent, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1080, 650));
            var panelImage = rect.gameObject.AddComponent<Image>();
            UiSkin.Panel(panelImage);
            Label(name + " title", title, rect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(980, 85), 53, TextAnchor.MiddleCenter);
            Label(name + " detail", detail, rect, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -145), new Vector2(970, 205), 29, TextAnchor.MiddleCenter);
            return rect.gameObject;
        }

        private void BuildUI()
        {
            new GameObject("Transit input", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = new GameObject("Transit UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("Safe area", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvas.transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            var hudRect = Rect("Transit HUD", safe.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            hudRect.anchorMin = Vector2.zero; hudRect.anchorMax = Vector2.one; playHud = hudRect.gameObject;
            var scrim = Rect("Top contrast", hudRect, new Vector2(.5f, 1), new Vector2(.5f, 1), Vector2.zero, new Vector2(2200, 180));
            var scrimImage = scrim.gameObject.AddComponent<Image>();
            scrimImage.color = new Color(.005f, .015f, .03f, .88f); scrimImage.raycastTarget = false;
            hud = Label("Transit timer", "", hudRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -15), new Vector2(1200, 65), 36);
            status = Label("Transit status", "", hudRect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -85), new Vector2(1250, 65), 26);
            Button("Pause", "PAUSE", hudRect, Vector2.one, Vector2.one, new Vector2(-32, -28), new Vector2(190, 88), TogglePause);
            Button("Light Jet", "JET / Q", hudRect, Vector2.zero, Vector2.zero, new Vector2(36, 35), new Vector2(240, 145), Jet);
            Button("Solar Sail", "SAIL / R", hudRect, Vector2.zero, Vector2.zero, new Vector2(298, 35), new Vector2(240, 145), Sail);
            forward = Button("Forward", "FORWARD", hudRect, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 35), new Vector2(280, 145), null).gameObject.AddComponent<HoldControl>();
            autoText = Button("Auto Drive", "AUTO: ON", hudRect, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 203), new Vector2(280, 85), ToggleAuto).GetComponentInChildren<Text>();
            landing = Overlay("Transit landing", safe.transform, "SOLAR TRANSIT", "Cross the amber gap with JET, then the cyan gap with SAIL.\nReach the finish before 12 seconds. Auto Drive starts ON.");
            bestText = Label("Best time", "", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 195), new Vector2(900, 60), 28, TextAnchor.MiddleCenter);
            Button("Start Transit", "START TRANSIT", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-230, 45), new Vector2(420, 95), StartCourse);
            Button("Offline Arena", "MAIN MENU", landing.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(230, 45), new Vector2(420, 95), () => SceneManager.LoadScene("PhaseOneArena"));
            pausePanel = Overlay("Transit pause", safe.transform, "PAUSED", "The timer and cycle are paused. Resume when ready.");
            Button("Resume", "RESUME", pausePanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-230, 45), new Vector2(420, 95), TogglePause);
            Button("Transit Menu", "TRANSIT MENU", pausePanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(230, 45), new Vector2(420, 95), ShowLanding);
            resultPanel = Overlay("Transit result", safe.transform, "TRANSIT RESULT", "");
            resultText = resultPanel.transform.Find("Transit result detail").GetComponent<Text>();
            Button("Retry Transit", "RETRY", resultPanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-230, 45), new Vector2(420, 95), StartCourse);
            Button("Result Menu", "TRANSIT MENU", resultPanel.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(230, 45), new Vector2(420, 95), ShowLanding);
        }

        public void ShowLanding()
        {
            playing = paused = false;
            run = new TransitRun();
            forward.Clear();
            playHud.SetActive(false); pausePanel.SetActive(false); resultPanel.SetActive(false); landing.SetActive(true);
            int best = RecordMilliseconds;
            bestText.text = best > 0 ? "BEST TIME  " + (best / 1000f).ToString("0.00") + "s" : "NO FINISH RECORDED";
        }

        public void StartCourse()
        {
            run = new TransitRun();
            run.Start();
            playing = true; paused = false; autoDrive = true;
            forward.Clear(); autoText.text = "AUTO: ON";
            landing.SetActive(false); pausePanel.SetActive(false); resultPanel.SetActive(false); playHud.SetActive(true);
            cycle.gameObject.SetActive(true);
            view.transform.position = new Vector3(0, 9, TransitRun.StartZ - 12);
        }

        private void ToggleAuto()
        {
            autoDrive = !autoDrive;
            forward.Clear();
            autoText.text = autoDrive ? "AUTO: ON" : "AUTO: OFF";
        }

        public void TogglePause()
        {
            if (!playing || run.Outcome != TransitRun.State.Running) return;
            paused = !paused;
            forward.Clear();
            pausePanel.SetActive(paused); playHud.SetActive(!paused);
        }

        private void Jet() { if (playing && !paused) run.Launch(TransitRun.Flight.Jet); }
        private void Sail() { if (playing && !paused) run.Launch(TransitRun.Flight.Sail); }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if (Input.GetKeyDown(KeyCode.Q)) Jet();
            if (Input.GetKeyDown(KeyCode.R)) Sail();
            if (!playing) return;
            hud.text = "SOLAR TRANSIT  /  " + Mathf.Max(0, TransitRun.TimeLimit - run.Elapsed).ToString("0.0") +
                "s LEFT  /  SPANS " + run.GapsCleared + "/2";
            status.text = run.ActiveFlight != TransitRun.Flight.None ? run.ActiveFlight.ToString().ToUpper() + " ACTIVE" :
                run.GapsCleared == 0 ? "AMBER GAP: JET BEFORE THE EDGE" :
                run.GapsCleared == 1 ? "CYAN GAP: SAIL BEFORE THE EDGE" : "FINISH GATE AHEAD";
            if (run.ActiveFlight == TransitRun.Flight.None && run.GapsCleared < 2)
                status.text += "  /  JET " + run.JetCooldown.ToString("0.0") + "s  SAIL " + run.SailCooldown.ToString("0.0") + "s";
        }

        private void FixedUpdate()
        {
            if (!playing || paused || run.Outcome != TransitRun.State.Running) return;
            run.Step(Time.fixedDeltaTime, autoDrive || forward.Held || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow));
            if (run.Outcome == TransitRun.State.Running) return;
            forward.Clear();
            playHud.SetActive(false);
            string heading;
            if (run.Outcome == TransitRun.State.Won)
            {
                heading = "TRANSIT COMPLETE";
                int time = Mathf.RoundToInt(run.Elapsed * 1000);
                int best = RecordMilliseconds;
                if (best <= 0 || time < best)
                {
                    try { PlayerPrefs.SetInt(BestKey, time); PlayerPrefs.Save(); }
                    catch (Exception error) { Debug.LogWarning("Transit record could not be saved: " + error.GetType().Name); }
                }
                resultText.text = heading + "\nFINISH TIME  " + run.Elapsed.ToString("0.00") + "s\nBEST  " +
                    (RecordMilliseconds > 0 ? (RecordMilliseconds / 1000f).ToString("0.00") + "s" : "UNAVAILABLE");
            }
            else
                resultText.text = "TRANSIT FAILED\n" + run.Failure + "\nSPANS CLEARED  " + run.GapsCleared + "/2";
            resultPanel.SetActive(true);
        }

        private void LateUpdate()
        {
            float lift = run.ActiveFlight == TransitRun.Flight.None ? 0 :
                Mathf.Sin(run.FlightFraction * Mathf.PI) * (run.ActiveFlight == TransitRun.Flight.Jet ? 3.4f : 2.4f);
            cycle.position = new Vector3(0, lift, run.Z);
            cycleArt.SetFlight(run.ActiveFlight == TransitRun.Flight.Jet,
                run.ActiveFlight == TransitRun.Flight.Sail);
            var target = new Vector3(0, 9, run.Z - 12);
            view.transform.position = Vector3.Lerp(view.transform.position, target, 1 - Mathf.Exp(-7 * Time.deltaTime));
            view.transform.rotation = Quaternion.Euler(26, 0, 0);
        }

        private void Suspend()
        {
            if (playing && !paused && run.Outcome == TransitRun.State.Running) TogglePause();
            if (forward != null) forward.Clear();
        }
        private void OnApplicationPause(bool background) { if (background) Suspend(); }
        private void OnApplicationFocus(bool focused) { if (!focused && !ignoreFocusForCapture) Suspend(); }
        private void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
