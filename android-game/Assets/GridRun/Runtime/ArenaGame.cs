using GridRun.Solo;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // App entry scene: main menu, settings, and the offline game (SoloGame) for every solo mode.
    public sealed class ArenaGame : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        private Camera view;
        private SoloGame solo;
        private GameObject landing, settingsPanel, settingsClose, newGameButton;
        private Text graphicsText, autoDefaultText, lookText, soundText;
        private bool ignoreHostFocusForVerification;

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = System.Environment.GetCommandLineArgs();
            ignoreHostFocusForVerification = System.Array.IndexOf(args, "--phase1-capture-dir") >= 0 ||
                System.Array.IndexOf(args, "--look-capture-dir") >= 0 || System.Array.IndexOf(args, "--solo-capture-dir") >= 0;
#endif
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = .1f;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            {
                Debug.LogError("Arena setup is missing. Run a Grid Run Prepare command.");
                enabled = false;
                return;
            }
            var cameraObject = new GameObject("Arena camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 260f; view.fieldOfView = 60f;
            GridLook.Stage(view);
            var safe = BuildCanvas();
            solo = gameObject.AddComponent<SoloGame>();
            solo.IgnoreFocusLoss = ignoreHostFocusForVerification;
            BuildMainMenu(safe);
            solo.Init(view, safe, floorMaterial, cycleMaterial, glowMaterial, ShowLanding);
            landing.transform.SetAsLastSibling();
            ShowLanding();
        }

        public SoloGame Solo => solo;

        private RectTransform BuildCanvas()
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
            return safeRect;
        }

        // Main-menu entry: left-aligned label with a luminous marker bar.
        private Button MenuButton(string name, string value, Transform parent, float y, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var size = new Vector2(540, 78);
            var button = SoloUi.Button(name, value, parent, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(110, y), size, action, primary, 30);
            var label = SoloUi.Caption(button);
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.anchoredPosition = new Vector2(46, 0);
            label.rectTransform.sizeDelta = new Vector2(-108, 0);
            var marker = SoloUi.Bar(name + " marker", button.transform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(26, 0), new Vector2(5, 32),
                primary ? UiSkin.Accent : new Color(.18f, .55f, .68f));
            return button;
        }

        // Launch screen: title, primary actions, and settings over a slow orbit of the parked cycle.
        private void BuildMainMenu(RectTransform safe)
        {
            var root = SoloUi.Stretch("Landing", safe);
            landing = root.gameObject;
            var shade = SoloUi.Stretch("Menu shade", root);
            shade.anchorMax = new Vector2(.72f, 1);
            UiSkin.Scrim(shade.gameObject.AddComponent<Image>());

            SoloUi.Label("Menu kicker", "LIGHT CYCLE ARENA", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(114, -96), new Vector2(700, 36), 24, TextAnchor.MiddleLeft, UiSkin.Accent);
            var title = SoloUi.Label("Menu title", "GRID RUN", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -128), new Vector2(900, 128), 112, TextAnchor.MiddleLeft, new Color(.92f, .99f, 1f), true);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.gameObject.AddComponent<Outline>().effectColor = new Color(.1f, .75f, 1f, .35f);
            SoloUi.Bar("Menu rule", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(114, -262), new Vector2(420, 3), UiSkin.Accent);
            SoloUi.Label("Menu tagline", "Ride the Grid. Outlast every rival.", root, new Vector2(0, 1), new Vector2(0, 1), new Vector2(114, -280), new Vector2(800, 40), 26, TextAnchor.MiddleLeft, UiSkin.Muted);

            float y = 80;
            newGameButton = MenuButton("Start Arena", "NEW GAME", root, y, OpenModes, true).gameObject;
            if (Application.CanStreamedLevelBeLoaded("PhaseTwoOnline"))
                MenuButton("Online Room", "ONLINE GAME", root, y -= 88, () => SceneManager.LoadScene("PhaseTwoOnline"));
            if (Application.CanStreamedLevelBeLoaded("PhaseFourShowroom"))
                MenuButton("Style Preview", "STYLE PREVIEW", root, y -= 88, () => SceneManager.LoadScene("PhaseFourShowroom"));
            MenuButton("Records", "RECORDS", root, y -= 88, OpenRecords);
            MenuButton("Settings", "SETTINGS", root, y -= 88, OpenSettings);
            SoloUi.Label("Menu footer", "DUEL  ·  ARENA  ·  DAILY SIGNAL  ·  GRID SHIFT  ·  OPERATIONS  ·  SOLAR TRANSIT  ·  v" + Application.version, root,
                Vector2.zero, Vector2.zero, new Vector2(114, 30), new Vector2(1500, 34), 19, TextAnchor.MiddleLeft, UiSkin.Muted);

            var overlay = SoloUi.Dim("Settings", root, .86f);
            settingsPanel = overlay.gameObject;
            var panel = SoloUi.Panel("Settings panel", overlay, new Vector2(1080, 880));
            SoloUi.Label("Settings heading", "SETTINGS", panel, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -26), new Vector2(940, 66), 46, TextAnchor.MiddleCenter, Color.white, true);
            graphicsText = SettingRow(panel, -112, "Graphics", "GRAPHICS", "High adds floor reflections and glow. Low runs faster on older phones.", GraphicsLabel(), ToggleGraphics);
            autoDefaultText = SettingRow(panel, -228, "Auto Drive Default", "AUTO DRIVE", "Cycles move forward on their own. Turn off to hold FORWARD to move.", SoloProfile.AutoDrive ? "ON" : "OFF", ToggleAutoDefault);
            lookText = SettingRow(panel, -344, "Trail Look", "TRAIL LOOK", "Clear Portal Run, System Sweep, or Arena Collapse to unlock looks. No gameplay effect.", SoloProfile.LookLabel(SoloProfile.Look), CycleLook);
            soundText = SettingRow(panel, -460, "Sound", "SOUND", "Procedural effects and music.", SoloProfile.Sound ? "ON" : "OFF", ToggleSound);
            SoloUi.Label("Controls heading", "CONTROLS", panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -580), new Vector2(600, 38), 26, TextAnchor.MiddleLeft, null, true);
            SoloUi.Label("Controls detail", "LEFT / RIGHT turn relative to your cycle. FORWARD moves in manual drive. Hold FIRE to shoot. JET and SAIL lift you over walls. " +
                "Grid Shift: FORM changes form, the pad moves you on foot, DISC throws.\nKeyboard: A/D or arrows, W, E fire, Q jet, R sail, F form, Space disc, Esc pause.",
                panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -620), new Vector2(960, 120), 21, TextAnchor.UpperLeft, UiSkin.Muted);
            settingsClose = SoloUi.Button("Close Settings", "BACK", panel, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 26), new Vector2(360, 84), CloseSettings, true).gameObject;
            settingsPanel.SetActive(false);
        }

        private Text SettingRow(RectTransform panel, float y, string name, string title, string detail, string value, UnityEngine.Events.UnityAction action)
        {
            SoloUi.Label(name + " title", title, panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y), new Vector2(560, 38), 26, TextAnchor.MiddleLeft, null, true);
            SoloUi.Label(name + " detail", detail, panel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, y - 40), new Vector2(640, 64), 21, TextAnchor.UpperLeft, UiSkin.Muted);
            return SoloUi.Caption(SoloUi.Button(name, value, panel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, y - 8), new Vector2(300, 80), action, false, 22));
        }

        private static string GraphicsLabel() => GridLook.HighQuality ? "HIGH" : "LOW";

        private void ToggleGraphics()
        {
            GridLook.HighQuality = !GridLook.HighQuality;
            GridLook.ApplyQuality(view);
            graphicsText.text = GraphicsLabel();
        }

        private void ToggleAutoDefault()
        {
            SoloProfile.AutoDrive = !SoloProfile.AutoDrive;
            autoDefaultText.text = SoloProfile.AutoDrive ? "ON" : "OFF";
        }

        private void CycleLook()
        {
            var looks = SoloProfile.Looks;
            int index = System.Array.IndexOf(looks, SoloProfile.Look);
            for (int step = 1; step <= looks.Length; step++)
            {
                string next = looks[(index + step) % looks.Length];
                if (!SoloProfile.LookUnlocked(next)) continue;
                SoloProfile.Look = next;
                break;
            }
            lookText.text = SoloProfile.LookLabel(SoloProfile.Look);
            solo.ShowPreview();
        }

        private void ToggleSound()
        {
            SoloProfile.Sound = !SoloProfile.Sound;
            solo.SetSound(SoloProfile.Sound);
            soundText.text = SoloProfile.Sound ? "ON" : "OFF";
        }

        private void OpenSettings()
        {
            settingsPanel.SetActive(true);
            SoloUi.Select(settingsClose);
        }

        private void CloseSettings()
        {
            settingsPanel.SetActive(false);
            SoloUi.Select(newGameButton);
        }

        private void OpenModes()
        {
            landing.SetActive(false);
            solo.OpenSelect();
        }

        private void OpenRecords()
        {
            landing.SetActive(false);
            solo.OpenRecords();
        }

        private void ShowLanding()
        {
            settingsPanel.SetActive(false);
            landing.SetActive(true);
            SoloUi.Select(newGameButton);
        }

        private void Update()
        {
            if (!landing.activeSelf) return;
            if (Input.GetKeyDown(KeyCode.Escape) && settingsPanel.activeSelf) CloseSettings();
        }

        private void LateUpdate()
        {
            if (landing.activeSelf || solo.Phase == "select" || solo.Phase == "setup" || solo.Phase == "records") OrbitLanding();
            else solo.LateUpdateCamera();
        }

        // Slow showcase orbit of the parked cycle, framed to the right of the menu column.
        private void OrbitLanding()
        {
            var bikeTransform = solo.PreviewRider;
            if (bikeTransform == null) return;
            float angle = (Time.unscaledTime * 6f + 35f) * Mathf.Deg2Rad;
            Vector3 bike = bikeTransform.position;
            Vector3 position = bike + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 7.5f + Vector3.up * 2.3f;
            Vector3 look = bike + Vector3.up * .75f;
            Vector3 right = Vector3.Cross(Vector3.up, (look - position).normalized).normalized;
            view.transform.position = position;
            view.transform.LookAt(look - right * (landing.activeSelf ? 2.4f : 0));
        }

        private void OnApplicationPause(bool background) { if (background) solo?.Suspend(); }
        private void OnApplicationFocus(bool focused) { if (!focused && !ignoreHostFocusForVerification) solo?.Suspend(); }
        private void OnDestroy() { Screen.sleepTimeout = SleepTimeout.SystemSetting; }
    }
}
