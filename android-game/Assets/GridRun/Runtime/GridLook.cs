using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridRun
{
    // Shared presentation for every scene: materials, lighting, fog, post-processing,
    // reflective floor, and arena surroundings. Gameplay rules never read from this class.
    public static class GridLook
    {
        public const int FloorLayer = 4; // Built-in "Water" layer: reflective surfaces hidden from the mirror view.
        private const string QualityKey = "gridrun.graphics.v1";

        public sealed class Kit
        {
            public Material Shell, ShellSolar, Suit, Tire, Structure, Glow, GlowSoft, GlowAmber,
                TrailCyan, TrailAmber, Boundary, Sky;
            public VolumeProfile Volume;
        }

        private static Kit kit;
        private static Cubemap environment;
        private static Mesh quad, sphere;
        private static readonly Dictionary<string, Dictionary<int, Mesh>> setCache = new Dictionary<string, Dictionary<int, Mesh>>();
        internal static readonly List<CycleVisual> Emitters = new List<CycleVisual>();

        public static Kit Assets => kit ?? (kit = Load());

        private static Kit Load()
        {
            Material M(string name)
            {
                var material = Resources.Load<Material>("GridLook/" + name);
                if (material == null) Debug.LogError("Grid look material missing: " + name + ". Run a Grid Run Prepare command.");
                return material;
            }
            return new Kit
            {
                Shell = M("Shell"), ShellSolar = M("ShellSolar"), Suit = M("Suit"), Tire = M("Tire"),
                Structure = M("Structure"), Glow = M("Glow"), GlowSoft = M("GlowSoft"), GlowAmber = M("GlowAmber"),
                TrailCyan = M("TrailCyan"), TrailAmber = M("TrailAmber"), Boundary = M("Boundary"), Sky = M("Sky"),
                Volume = Resources.Load<VolumeProfile>("GridLook/GridLookVolume")
            };
        }

        // High: HDR, bloom/tonemapping, and planar floor reflections. Low keeps the same art without them.
        public static bool HighQuality
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "--gridrun-graphics");
                if (index >= 0 && index + 1 < args.Length) return args[index + 1] != "low";
                try { return PlayerPrefs.GetInt(QualityKey, 1) == 1; }
                catch (Exception) { return true; }
            }
            set
            {
                try { PlayerPrefs.SetInt(QualityKey, value ? 1 : 0); PlayerPrefs.Save(); }
                catch (Exception error) { Debug.LogWarning("Graphics preference could not be saved: " + error.GetType().Name); }
            }
        }

        public static Material Trail(bool cyan) => cyan ? Assets.TrailCyan : Assets.TrailAmber;

        // Configures lighting, fog, sky, post-processing, and reflections around one gameplay camera.
        public static void Stage(Camera camera, float floorHeight = 0)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0068f;
            RenderSettings.fogColor = new Color(.006f, .02f, .03f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.07f, .11f, .15f);
            RenderSettings.ambientEquatorColor = new Color(.03f, .06f, .08f);
            RenderSettings.ambientGroundColor = new Color(.008f, .01f, .014f);
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(.03f, .05f, .065f));
            ambient.AddDirectionalLight(Vector3.up, new Color(.05f, .08f, .1f), 1);
            RenderSettings.ambientProbe = ambient;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = EnvironmentMap();
            RenderSettings.reflectionIntensity = 1;

            var probe = new GameObject("Grid environment probe").AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = EnvironmentMap();
            probe.size = Vector3.one * 2000;
            probe.importance = 1;

            var key = new GameObject("Grid key light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(.62f, .8f, 1f);
            key.intensity = .9f;
            key.shadows = LightShadows.None;
            key.transform.rotation = Quaternion.Euler(34, -38, 0);
            RenderSettings.sun = key;
            var rim = new GameObject("Grid rim light").AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(.2f, .7f, 1f);
            rim.intensity = .65f;
            rim.shadows = LightShadows.None;
            rim.transform.rotation = Quaternion.Euler(16, 150, 0);

            if (Assets.Volume != null)
            {
                var volume = new GameObject("Grid post volume").AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10;
                volume.sharedProfile = Assets.Volume;
            }

            var sky = new GameObject("Grid sky dome", typeof(MeshFilter), typeof(MeshRenderer));
            sky.transform.SetParent(camera.transform, false);
            sky.transform.localScale = Vector3.one * camera.farClipPlane * 1.8f;
            sky.GetComponent<MeshFilter>().sharedMesh = SphereMesh();
            var skyRenderer = sky.GetComponent<MeshRenderer>();
            skyRenderer.sharedMaterial = Assets.Sky;
            skyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            skyRenderer.receiveShadows = false;

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.004f, .008f, .014f);
            var driver = camera.gameObject.AddComponent<GridLookDriver>();
            driver.FloorHeight = floorHeight;
            ApplyQuality(camera);
        }

        public static void ApplyQuality(Camera camera)
        {
            bool high = HighQuality;
            camera.allowHDR = high;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = high;
            data.antialiasing = AntialiasingMode.None;
            var reflection = camera.GetComponent<PlanarReflection>();
            if (reflection == null && high)
            {
                reflection = camera.gameObject.AddComponent<PlanarReflection>();
                reflection.Height = camera.GetComponent<GridLookDriver>()?.FloorHeight ?? 0;
            }
            if (reflection != null) reflection.enabled = high;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, int layer = 0)
        {
            var obj = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            obj.layer = layer;
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return obj;
        }

        // Reflective grid floor. The grid is drawn procedurally, so one quad covers the arena.
        public static GameObject Floor(Transform parent, Vector3 center, Vector2 size, float gridSize, Material floor)
        {
            var obj = MeshObject("Grid floor", parent, QuadMesh(), floor, FloorLayer);
            obj.transform.localPosition = center;
            obj.transform.localScale = new Vector3(size.x, 1, size.y);
            var block = new MaterialPropertyBlock();
            block.SetFloat("_GridSize", gridSize);
            obj.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            return obj;
        }

        private static void Place(Transform parent, string name, Dictionary<int, Mesh> meshes)
        {
            Material[] materials = { Assets.Structure, Assets.Glow, Assets.GlowSoft, Assets.GlowAmber };
            foreach (var pair in meshes) MeshObject(name + " " + pair.Key, parent, pair.Value, materials[pair.Key]);
        }

        private static Dictionary<int, Mesh> Cached(string key, Action<GridMesh.Batch> build)
        {
            if (setCache.TryGetValue(key, out var meshes)) return meshes;
            var batch = new GridMesh.Batch();
            build(batch);
            return setCache[key] = batch.Build(key);
        }

        // Stepped spectator tiers, a tall back wall, and light columns surrounding a square arena.
        public static void Stadium(Transform parent, float inner)
        {
            Place(parent, "Stadium", Cached("stadium " + inner, batch =>
            {
                const int tiers = 6;
                const float depth = 3.4f;
                for (int side = 0; side < 4; side++)
                {
                    var rotation = Quaternion.Euler(0, side * 90, 0);
                    void Box(int slot, Vector3 center, Vector3 size) =>
                        batch.Add(slot, GridMesh.Box(Vector3.zero, size), rotation * center, rotation);
                    float baseZ = inner + 1.5f;
                    float baseHalf = baseZ + 1.2f;
                    Box(0, new Vector3(0, .5f, baseZ + .6f), new Vector3(baseHalf * 2, 1f, 1.2f));
                    Box(1, new Vector3(0, 1.03f, baseZ + .05f), new Vector3(baseHalf * 2, .06f, .1f));
                    for (int t = 0; t < tiers; t++)
                    {
                        float z0 = inner + 3 + t * depth, height = 1.8f + t * 2.3f, half = z0 + depth;
                        Box(0, new Vector3(0, height * .5f, z0 + depth * .5f), new Vector3(half * 2, height, depth));
                        Box(t % 2 == 0 ? 2 : 1, new Vector3(0, height + .03f, z0 + .06f), new Vector3(half * 2, .06f, .12f));
                    }
                    float wallZ = inner + 3 + tiers * depth;
                    float wallHalf = wallZ + 2;
                    Box(0, new Vector3(0, 13, wallZ + 1), new Vector3(wallHalf * 2, 26, 2));
                    Box(1, new Vector3(0, 19, wallZ - .02f), new Vector3(wallHalf * 2, .25f, .08f));
                    Box(2, new Vector3(0, 22.5f, wallZ - .02f), new Vector3(wallHalf * 2, .1f, .08f));
                    for (float x = -wallHalf + 8; x < wallHalf - 4; x += 14)
                    {
                        Box(0, new Vector3(x, 14, wallZ - .6f), new Vector3(1.6f, 28, 1.2f));
                        Box(2, new Vector3(x, 14, wallZ - 1.23f), new Vector3(.18f, 24, .06f));
                    }
                }
            }));
        }

        // Distant towers that give the horizon depth; fog keeps them atmospheric.
        public static void Skyline(Transform parent, float minRadius, float maxRadius, int count, int seed)
        {
            Place(parent, "Skyline", Cached("skyline " + minRadius + " " + maxRadius + " " + count + " " + seed, batch =>
            {
                var random = new System.Random(seed);
                float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
                for (int i = 0; i < count; i++)
                {
                    float angle = R(0, Mathf.PI * 2);
                    float radius = R(minRadius, maxRadius);
                    var position = new Vector3(Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                    var rotation = Quaternion.LookRotation(-position.normalized);
                    float width = R(5, 13), depth = R(5, 11), height = R(14, 72);
                    void Box(int slot, Vector3 center, Vector3 size) =>
                        batch.Add(slot, GridMesh.Box(Vector3.zero, size), position + rotation * center, rotation);
                    Box(0, new Vector3(0, height * .5f, 0), new Vector3(width, height, depth));
                    int accent = random.NextDouble() < .16 ? 3 : 1;
                    int strips = random.Next(1, 4);
                    for (int s = 0; s < strips; s++)
                    {
                        float x = (s + 1f) / (strips + 1) * width - width * .5f;
                        Box(s == 0 ? accent : 2, new Vector3(x, height * .5f, depth * .5f + .05f), new Vector3(.22f, height * .92f, .08f));
                    }
                    Box(accent, new Vector3(0, height + .05f, 0), new Vector3(width + .1f, .12f, depth + .1f));
                    if (random.NextDouble() < .5)
                        Box(2, new Vector3(0, height * R(.35f, .8f), depth * .5f + .05f), new Vector3(width, .14f, .08f));
                }
            }));
        }

        private static Mesh QuadMesh()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Grid floor quad" };
            quad.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            quad.RecalculateBounds();
            return quad;
        }

        private static Mesh SphereMesh()
        {
            if (sphere != null) return sphere;
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(primitive);
            return sphere;
        }

        // Procedural HDR environment for glossy reflections: horizon glow, light panels, soft overhead light.
        private static Cubemap EnvironmentMap()
        {
            if (environment != null) return environment;
            const int size = 64;
            var format = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
            environment = new Cubemap(size, format, true) { name = "Grid environment", filterMode = FilterMode.Trilinear };
            var pixels = new Color[size * size];
            for (int face = 0; face < 6; face++)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                        Vector3 direction;
                        switch (face)
                        {
                            case 0: direction = new Vector3(1, -v, -u); break;
                            case 1: direction = new Vector3(-1, -v, u); break;
                            case 2: direction = new Vector3(u, 1, v); break;
                            case 3: direction = new Vector3(u, -1, -v); break;
                            case 4: direction = new Vector3(u, -v, 1); break;
                            default: direction = new Vector3(-u, -v, -1); break;
                        }
                        pixels[y * size + x] = EnvironmentColor(direction.normalized);
                    }
                environment.SetPixels(pixels, (CubemapFace)face, 0);
            }
            environment.Apply(true, false);
            return environment;
        }

        private static Color EnvironmentColor(Vector3 d)
        {
            float y = d.y;
            var horizon = new Color(.03f, .10f, .14f);
            Color color = y >= 0
                ? Color.Lerp(horizon, new Color(.006f, .012f, .02f), Mathf.Pow(y, .5f))
                : Color.Lerp(horizon, new Color(.004f, .008f, .012f), Mathf.Clamp01(-y * 5));
            color += new Color(.10f, .55f, .75f) * Mathf.Exp(-Mathf.Abs(y) * 18f) * 1.1f;
            float azimuth = Mathf.Atan2(d.x, d.z);
            float panel = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.93f, .985f, Mathf.Cos(azimuth * 8)));
            float band = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .1f, y)) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, .38f, y));
            color += new Color(.35f, 1.2f, 1.6f) * panel * band;
            color += new Color(.25f, .32f, .38f) * Mathf.Pow(Mathf.Max(0, y), 6) * 1.6f;
            color.a = 1;
            return color;
        }
    }

    // Per-camera helper: feeds cycle light pools to the floor shader each frame.
    public sealed class GridLookDriver : MonoBehaviour
    {
        public float FloorHeight;
        private readonly Vector4[] positions = new Vector4[4];
        private readonly Vector4[] colors = new Vector4[4];
        private static readonly int GlowPos = Shader.PropertyToID("_GridGlowPos");
        private static readonly int GlowColor = Shader.PropertyToID("_GridGlowColor");

        private void LateUpdate()
        {
            int n = 0;
            foreach (var emitter in GridLook.Emitters)
            {
                if (n == positions.Length) break;
                if (emitter == null || !emitter.isActiveAndEnabled) continue;
                Vector3 p = emitter.transform.position;
                float height = Mathf.Max(0, p.y - FloorHeight);
                positions[n] = new Vector4(p.x, p.y, p.z, Mathf.Clamp01(1 - height * .22f));
                colors[n] = emitter.GlowColor;
                n++;
            }
            for (; n < positions.Length; n++) { positions[n] = Vector4.zero; colors[n] = Vector4.zero; }
            Shader.SetGlobalVectorArray(GlowPos, positions);
            Shader.SetGlobalVectorArray(GlowColor, colors);
        }

        private void OnDisable()
        {
            Array.Clear(positions, 0, positions.Length);
            Shader.SetGlobalVectorArray(GlowPos, positions);
        }
    }

    // Mirrors the main camera below the floor plane into a half-resolution texture sampled by GridFloor.
    public sealed class PlanarReflection : MonoBehaviour
    {
        public float Height;
        private Camera main, mirror;
        private RenderTexture target;
        private static readonly int ReflectionTex = Shader.PropertyToID("_GridReflectionTex");
        private static readonly int ReflectionOn = Shader.PropertyToID("_GridReflectionOn");

        private void OnEnable()
        {
            main = GetComponent<Camera>();
            var obj = new GameObject("Floor reflection camera");
            obj.hideFlags = HideFlags.DontSave;
            mirror = obj.AddComponent<Camera>();
            mirror.enabled = true;
            mirror.cullingMask = ~((1 << GridLook.FloorLayer) | (1 << 5));
            mirror.clearFlags = CameraClearFlags.SolidColor;
            mirror.backgroundColor = Color.black;
            mirror.allowMSAA = false;
            mirror.allowHDR = true;
            mirror.useOcclusionCulling = false;
            var data = mirror.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            RenderPipelineManager.beginContextRendering += Sync;
            Shader.SetGlobalFloat(ReflectionOn, 1);
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= Sync;
            Shader.SetGlobalFloat(ReflectionOn, 0);
            Shader.SetGlobalTexture(ReflectionTex, Texture2D.blackTexture);
            if (mirror != null) Destroy(mirror.gameObject);
            if (target != null) { target.Release(); Destroy(target); }
            mirror = null; target = null;
        }

        private void Sync(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (main == null || mirror == null) return;
            int width = Mathf.Max(64, main.pixelWidth / 2), height = Mathf.Max(64, main.pixelHeight / 2);
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) { target.Release(); Destroy(target); }
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR)
                    ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32;
                target = new RenderTexture(width, height, 16, format) { name = "Grid floor reflection" };
                target.Create();
                mirror.targetTexture = target;
                Shader.SetGlobalTexture(ReflectionTex, target);
            }
            var p = main.transform.position;
            var f = main.transform.forward;
            var u = main.transform.up;
            mirror.transform.SetPositionAndRotation(new Vector3(p.x, 2 * Height - p.y, p.z),
                Quaternion.LookRotation(new Vector3(f.x, -f.y, f.z), new Vector3(u.x, -u.y, u.z)));
            mirror.depth = main.depth - 1;
            mirror.fieldOfView = main.fieldOfView;
            mirror.aspect = main.aspect;
            mirror.nearClipPlane = main.nearClipPlane;
            mirror.farClipPlane = main.farClipPlane;
            mirror.ResetProjectionMatrix();
            // Oblique near plane clips everything below the floor out of the reflection.
            Matrix4x4 view = mirror.worldToCameraMatrix;
            Vector3 point = view.MultiplyPoint(new Vector3(0, Height + .01f, 0));
            Vector3 normal = view.MultiplyVector(Vector3.up).normalized;
            mirror.projectionMatrix = mirror.CalculateObliqueMatrix(
                new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(point, normal)));
        }
    }
}
