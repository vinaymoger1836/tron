using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GridRun
{
    // Player-visible catalog preview. There is intentionally no purchase or entitlement path.
    public sealed class CosmeticShowroom : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial, gridMaterial, wallMaterial, cycleMaterial, glowMaterial, amberMaterial;
        private readonly List<Renderer> trailParts = new List<Renderer>();
        private readonly Dictionary<CosmeticCatalog.Category, Button> categoryButtons =
            new Dictionary<CosmeticCatalog.Category, Button>();
        private CycleVisual previewCycle;
        private Camera view;
        private Font font;
        private Text heading, description, availability, position;
        private CosmeticCatalog.Category category;
        private int selected;
        public CosmeticCatalog.Entry CurrentEntry { get; private set; }
        public string DisplayedBodyTheme { get; private set; }
        public string DisplayedTrailTheme { get; private set; }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            if (GraphicsSettings.currentRenderPipeline == null || floorMaterial == null)
            {
                Debug.LogError("Phase 4 cosmetic showroom scene is not prepared.");
                enabled = false;
                return;
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildWorld();
            BuildUI();
            Select(CosmeticCatalog.Category.Cycle);
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
            GridLook.Floor(transform, Vector3.zero, new Vector2(70, 70), 2, floorMaterial);
            GridLook.Stadium(transform, 20);
            GridLook.Skyline(transform, 90, 150, 60, 31);
            var display = new GameObject("Cosmetic display").transform;
            display.SetParent(transform, false);
            display.localRotation = Quaternion.Euler(0, 65, 0);
            var ring = GridLook.MeshObject("Display ring", display, GridMesh.Annulus(4.1f, 4.22f, 96, true), glowMaterial);
            ring.transform.localPosition = new Vector3(0, .01f, 2);
            ring.transform.localRotation = Quaternion.Euler(0, 0, 90);
            previewCycle = CycleVisual.Create(display, "Preview cycle", cycleMaterial, glowMaterial);
            previewCycle.transform.localPosition = new Vector3(0, 0, 2);
            previewCycle.transform.localScale = Vector3.one * 2.35f;
            AddTrail(Box("Light wall", new Vector3(0, .6f, -2.9f), new Vector3(.12f, 1.2f, 7), GridLook.Trail(true), display));
            var cameraObject = new GameObject("Showroom camera", typeof(Camera), typeof(AudioListener));
            view = cameraObject.GetComponent<Camera>();
            view.tag = "MainCamera";
            view.nearClipPlane = .1f; view.farClipPlane = 200; view.fieldOfView = 54;
            view.transform.position = new Vector3(7, 5.5f, 12);
            view.transform.LookAt(new Vector3(0, -.1f, 0));
            GridLook.Stage(view);
        }

        private void AddTrail(GameObject item) => trailParts.Add(item.GetComponent<Renderer>());

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
            Label(name + " label", value, rect, Vector2.zero, Vector2.zero, Vector2.zero, size, 26, TextAnchor.MiddleCenter);
            return button;
        }

        private void BuildUI()
        {
            new GameObject("Showroom input", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvas = new GameObject("Showroom UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var safe = new GameObject("Safe area", typeof(RectTransform), typeof(SafeArea));
            safe.transform.SetParent(canvas.transform, false);
            var safeRect = (RectTransform)safe.transform;
            safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
            safeRect.offsetMin = safeRect.offsetMax = Vector2.zero;
            Label("Showroom title", "GRID RUN  /  STYLE PREVIEW", safe.transform,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(35, -16), new Vector2(1200, 65), 40);
            Label("Preview notice", "CATALOG V1  /  PREVIEW ONLY  /  PURCHASES UNAVAILABLE", safe.transform,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(37, -75), new Vector2(1350, 50), 24);
            Button("Back to Arena", "MAIN MENU", safe.transform, Vector2.one, Vector2.one,
                new Vector2(-30, -28), new Vector2(280, 85), ReturnToArena);
            categoryButtons[CosmeticCatalog.Category.Cycle] = Button("Cycle Skins", "CYCLE SKINS", safe.transform,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(-330, -155), new Vector2(295, 75),
                () => Select(CosmeticCatalog.Category.Cycle));
            categoryButtons[CosmeticCatalog.Category.Trail] = Button("Trail Styles", "TRAIL STYLES", safe.transform,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -155), new Vector2(295, 75),
                () => Select(CosmeticCatalog.Category.Trail));
            categoryButtons[CosmeticCatalog.Category.Bundle] = Button("Sets", "SETS", safe.transform,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(330, -155), new Vector2(295, 75),
                () => Select(CosmeticCatalog.Category.Bundle));
            var panel = Rect("Preview details", safe.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 25), new Vector2(1420, 330));
            var panelImage = panel.gameObject.AddComponent<Image>();
            UiSkin.Panel(panelImage);
            heading = Label("Item name", "", panel, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -12), new Vector2(1290, 70), 44, TextAnchor.MiddleCenter);
            description = Label("Item description", "", panel, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -88), new Vector2(1290, 65), 27, TextAnchor.MiddleCenter);
            availability = Label("Item availability", "", panel, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -155), new Vector2(1320, 60), 28, TextAnchor.MiddleCenter);
            position = Label("Item position", "", panel, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(0, 26), new Vector2(300, 75), 25, TextAnchor.MiddleCenter);
            Button("Previous Style", "PREVIOUS", panel, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(-350, 26), new Vector2(350, 80), Previous);
            Button("Next Style", "NEXT", panel, new Vector2(.5f, 0), new Vector2(.5f, 0),
                new Vector2(350, 26), new Vector2(350, 80), Next);
        }

        public void Select(CosmeticCatalog.Category nextCategory)
        {
            category = nextCategory;
            selected = 0;
            ShowSelection();
        }

        public void Next()
        {
            selected = (selected + 1) % CosmeticCatalog.Count(category);
            ShowSelection();
        }

        public void Previous()
        {
            selected = (selected + CosmeticCatalog.Count(category) - 1) % CosmeticCatalog.Count(category);
            ShowSelection();
        }

        private void ShowSelection()
        {
            CurrentEntry = CosmeticCatalog.At(category, selected);
            DisplayedBodyTheme = CurrentEntry.BodyTheme;
            DisplayedTrailTheme = CurrentEntry.TrailTheme;
            bool solarBody = DisplayedBodyTheme == "solar", solarTrail = DisplayedTrailTheme == "solar";
            // A cycle skin owns the shell and its circuit lines; a trail style owns only the light wall.
            previewCycle.SetPalette(solarBody ? GridLook.Assets.ShellSolar : cycleMaterial,
                solarBody ? amberMaterial : glowMaterial);
            foreach (var part in trailParts) part.sharedMaterial = GridLook.Trail(!solarTrail);
            heading.text = CurrentEntry.Name.ToUpperInvariant();
            description.text = CurrentEntry.Description;
            availability.text = CurrentEntry.Included ? "DEFAULT LOOK  /  INCLUDED IN ARENA" :
                "CONCEPT PREVIEW  /  UNAVAILABLE TO EQUIP OR BUY";
            position.text = (selected + 1) + " / " + CosmeticCatalog.Count(category);
            foreach (var pair in categoryButtons)
                UiSkin.Button(pair.Value, pair.Value.GetComponent<Image>(), pair.Key == category);
        }

        private void Update()
        {
            if (previewCycle != null) previewCycle.transform.Rotate(0, Time.deltaTime * 8f, 0, Space.Self);
            if (Input.GetKeyDown(KeyCode.Escape)) ReturnToArena();
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Previous();
            if (Input.GetKeyDown(KeyCode.RightArrow)) Next();
            if (Input.GetKeyDown(KeyCode.Alpha1)) Select(CosmeticCatalog.Category.Cycle);
            if (Input.GetKeyDown(KeyCode.Alpha2)) Select(CosmeticCatalog.Category.Trail);
            if (Input.GetKeyDown(KeyCode.Alpha3)) Select(CosmeticCatalog.Category.Bundle);
        }

        public void ReturnToArena() => SceneManager.LoadScene("PhaseOneArena");
    }
}
