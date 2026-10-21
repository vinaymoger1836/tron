using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace GridRun
{
    public sealed class ArenaProbe : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material gridMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material cycleMaterial;
        [SerializeField] private Material glowMaterial;
        [SerializeField] private Material amberMaterial;
        private readonly ProbeSimulation simulation = new ProbeSimulation();
        private Transform rider;
        private Camera view;
        private Text status, metrics, autoLabel, pauseLabel, fpsLabel;
        private HoldControl forward;
        private Font font;
        private bool paused, autoDrive = true;
        private bool ignoreHostFocusForVerification;
        private int targetFps = 60;
        private float metricTime;
        private int metricFrames;
        private readonly float[] frameTimes = new float[240];
        private readonly float[] sortedTimes = new float[240];
        private int frameIndex, frameCount;

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // The hidden desktop capture player loses OS focus; this opt-in test
            // checks controls, while real device testing covers focus interruption.
            ignoreHostFocusForVerification = System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "--phase0-capture-dir") >= 0;
#endif
            Application.targetFrameRate = targetFps;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = .1f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            {
                Debug.LogError("Phase 0 setup is missing. Run Grid Run > Phase 0 > Prepare Project.");
                enabled = false;
                return;
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildArena();
            BuildInterface();
            Restart();
        }

        private GameObject Box(string label, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = label;
            if (parent != null) box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(box.GetComponent<Collider>());
            return box;
        }

        private void BuildArena()
        {
            Box("Arena floor", new Vector3(0, -.3f, 0), new Vector3(100, .5f, 100), floorMaterial, transform);
            for (int i = -48; i <= 48; i += 4)
            {
                Box("Grid X", new Vector3(i, -.035f, 0), new Vector3(.035f, .02f, 96), gridMaterial, transform);
                Box("Grid Z", new Vector3(0, -.035f, i), new Vector3(96, .02f, .035f), gridMaterial, transform);
            }
            foreach (int edge in new[] { -1, 1 })
            {
                Box("Boundary east/west", new Vector3(edge * 49, 1, 0), new Vector3(.4f, 2, 100), wallMaterial, transform);
                Box("Boundary north/south", new Vector3(0, 1, edge * 49), new Vector3(100, 2, .4f), wallMaterial, transform);
                Box("Boundary light", new Vector3(edge * 49, 2.1f, 0), new Vector3(.45f, .12f, 100), glowMaterial, transform);
                Box("Boundary light", new Vector3(0, 2.1f, edge * 49), new Vector3(100, .12f, .45f), glowMaterial, transform);
            }
            for (int i = -40; i <= 40; i += 20)
                foreach (int edge in new[] { -1, 1 })
                {
                    Box("Outer column", new Vector3(i, 5, edge * 55), new Vector3(3, 10, 3), wallMaterial, transform);
                    Box("Column light", new Vector3(i, 7, edge * 53.4f), new Vector3(.25f, 5, .2f), amberMaterial, transform);
                }
            rider = CycleVisual.Create(transform, "Player cycle",
                cycleMaterial, glowMaterial, wallMaterial).transform;
            var cameraObject = new GameObject("Arena camera", typeof(Camera));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.012f, .02f, .045f);
            view.nearClipPlane = .1f;
            view.farClipPlane = 220f;
            view.fieldOfView = 60f;
            cameraObject.AddComponent<AudioListener>();
        }

        private RectTransform Panel(string label, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(label, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private Text Label(string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, int fontSize)
        {
            var rect = Panel(value, parent, anchor, pivot, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = fontSize;
            text.color = new Color(.8f, .95f, 1);
            text.text = value; text.raycastTarget = false;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button Control(string value, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Panel(value, parent, anchor, pivot, position, size);
            var background = rect.gameObject.AddComponent<Image>();
            UiSkin.Round(background);
            background.color = new Color(.035f, .15f, .2f, .96f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = new Color(.6f, 1, 1);
            colors.pressedColor = new Color(.25f, .6f, .8f);
            colors.selectedColor = new Color(.6f, 1, 1);
            button.colors = colors;
            if (action != null) button.onClick.AddListener(action);
            var text = Label(value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 25);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private void BindTurn(Button button, int direction)
        {
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => Turn(direction));
            trigger.triggers.Add(entry);
            var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
            submit.callback.AddListener(_ => Turn(direction));
            trigger.triggers.Add(submit);
        }

        private void BuildInterface()
        {
            new GameObject("UI input", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvasObject = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("Safe area", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvasObject.transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            var scrim = new GameObject("HUD contrast", typeof(RectTransform), typeof(Image));
            scrim.transform.SetParent(safe.transform, false);
            var scrimRect = (RectTransform)scrim.transform;
            scrimRect.anchorMin = new Vector2(0, 1);
            scrimRect.anchorMax = new Vector2(1, 1);
            scrimRect.pivot = new Vector2(.5f, 1);
            scrimRect.offsetMin = new Vector2(0, -340);
            scrimRect.offsetMax = Vector2.zero;
            var scrimImage = scrim.GetComponent<Image>();
            scrimImage.color = new Color(.005f, .015f, .03f, .88f);
            scrimImage.raycastTarget = false;
            var topLeft = new Vector2(0, 1);
            var topRight = Vector2.one;
            Label("GRID RUN  /  ANDROID", safe.transform, topLeft, topLeft, new Vector2(36, -26), new Vector2(650, 50), 38);
            Label("PHASE 0 - MOVEMENT PROBE", safe.transform, topLeft, topLeft, new Vector2(38, -80), new Vector2(650, 40), 24);
            status = Label("", safe.transform, topLeft, topLeft, new Vector2(38, -134), new Vector2(950, 70), 26);
            metrics = Label("Measuring frame timing...", safe.transform, topLeft, topLeft, new Vector2(38, -210), new Vector2(950, 40), 22);
            pauseLabel = Control("PAUSE", safe.transform, topRight, topRight, new Vector2(-32, -30), new Vector2(180, 85), TogglePause).GetComponentInChildren<Text>();
            Control("RESTART", safe.transform, topRight, topRight, new Vector2(-232, -30), new Vector2(180, 85), Restart);
            autoLabel = Control("AUTO DRIVE: ON", safe.transform, topRight, topRight, new Vector2(-32, -136), new Vector2(280, 85), () =>
            {
                autoDrive = !autoDrive;
                forward.Clear();
                autoLabel.text = autoDrive ? "AUTO DRIVE: ON" : "AUTO DRIVE: OFF";
            }).GetComponentInChildren<Text>();
            fpsLabel = Control("TARGET: 60 FPS", safe.transform, topRight, topRight, new Vector2(-32, -242), new Vector2(280, 85), () =>
            {
                targetFps = targetFps == 60 ? 30 : 60;
                Application.targetFrameRate = targetFps;
                frameCount = frameIndex = 0;
                fpsLabel.text = "TARGET: " + targetFps + " FPS";
            }).GetComponentInChildren<Text>();
            BindTurn(Control("LEFT", safe.transform, Vector2.zero, Vector2.zero, new Vector2(36, 36), new Vector2(220, 140), null), -1);
            BindTurn(Control("RIGHT", safe.transform, Vector2.zero, Vector2.zero, new Vector2(280, 36), new Vector2(220, 140), null), 1);
            forward = Control("HOLD FORWARD", safe.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-36, 36), new Vector2(290, 140), null).gameObject.AddComponent<HoldControl>();
            var hint = Label("A/D or arrows: turn  |  W: forward  |  Escape: pause\nSteering and boundary demo - combat arrives in Phase 1", safe.transform,
                new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 198), new Vector2(920, 80), 23);
            hint.alignment = TextAnchor.MiddleCenter;
        }

        private void Turn(int direction)
        {
            if (!paused) simulation.Turn(direction);
        }

        public void Restart()
        {
            simulation.Reset(); paused = false;
            if (forward != null) forward.Clear();
            if (pauseLabel != null) pauseLabel.text = "PAUSE";
            SnapCamera();
        }

        public void TogglePause()
        {
            paused = !paused;
            forward.Clear();
            pauseLabel.text = paused ? "RESUME" : "PAUSE";
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) TogglePause();
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) Turn(-1);
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) Turn(1);
            if (Input.GetKeyDown(KeyCode.Return) && simulation.Crashed) Restart();
            status.text = simulation.Crashed ? "BOUNDARY HIT - tap RESTART to ride again" :
                paused ? "PAUSED - tap RESUME when ready" :
                autoDrive ? "AUTO DRIVE - steer left or right" : "MANUAL DRIVE - hold FORWARD or W to move";
            float delta = Time.unscaledDeltaTime;
            frameTimes[frameIndex] = delta * 1000;
            frameIndex = (frameIndex + 1) % frameTimes.Length;
            frameCount = Mathf.Min(frameCount + 1, frameTimes.Length);
            metricTime += delta; metricFrames++;
            if (metricTime >= 1 && frameCount > 0)
            {
                System.Array.Copy(frameTimes, sortedTimes, frameCount);
                System.Array.Sort(sortedTimes, 0, frameCount);
                float p95 = sortedTimes[Mathf.Clamp(Mathf.CeilToInt(frameCount * .95f) - 1, 0, frameCount - 1)];
                metrics.text = $"{metricFrames / metricTime:F0} FPS  |  p95 frame: {p95:F1} ms  |  {Screen.width} x {Screen.height}";
                metricFrames = 0; metricTime = 0;
            }
        }

        private void FixedUpdate()
        {
            if (paused) return;
            bool moving = autoDrive || forward.Held || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            simulation.Step(Time.fixedDeltaTime, moving);
        }

        private void LateUpdate()
        {
            rider.position = new Vector3(simulation.X, 0, simulation.Z);
            rider.rotation = Quaternion.Euler(0, simulation.Heading * 90, 0);
            Vector3 target = rider.position + new Vector3(0, 19, -18);
            view.transform.position = Vector3.Lerp(view.transform.position, target, 1 - Mathf.Exp(-8 * Time.deltaTime));
            view.transform.rotation = Quaternion.Euler(47, 0, 0);
        }

        private void SnapCamera()
        {
            if (view == null) return;
            view.transform.position = new Vector3(simulation.X, 19, simulation.Z - 18);
            view.transform.rotation = Quaternion.Euler(47, 0, 0);
        }

        private void Suspend()
        {
            paused = true;
            if (forward != null) forward.Clear();
            if (pauseLabel != null) pauseLabel.text = "RESUME";
        }
        private void OnApplicationPause(bool background) { if (background) Suspend(); }
        private void OnApplicationFocus(bool focused) { if (!focused && !ignoreHostFocusForVerification) Suspend(); }
        private void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
