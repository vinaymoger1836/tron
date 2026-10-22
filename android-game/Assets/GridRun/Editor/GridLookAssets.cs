using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridRun.Editor
{
    // Generates the shared Grid art kit. Values live in code so the look is reproducible;
    // rerunning any Prepare command reapplies them.
    public static class GridLookAssets
    {
        private const string KitPath = "Assets/Resources/GridLook";
        private const string MaterialsPath = "Assets/GridRun/Materials";
        private const string PostProcessDataPath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        public static readonly Color Cyan = new Color(.22f, 2.4f, 3.4f);
        public static readonly Color Amber = new Color(3.6f, 1.15f, .14f);

        [MenuItem("Grid Run/Look/Apply Grid Look")]
        public static void Apply()
        {
            Directory.CreateDirectory(KitPath);
            AssetDatabase.Refresh();
            Shader lit = Require("Universal Render Pipeline/Lit"), unlit = Require("Universal Render Pipeline/Unlit");
            Shader floor = Require("GridRun/GridFloor"), wall = Require("GridRun/LightWall"), sky = Require("GridRun/SkyDome");

            // Existing scene-bound materials keep their GUIDs; only their shader and values change.
            Lit(MaterialsPath + "/Cycle.mat", lit, new Color(.012f, .014f, .018f), .92f, .86f);
            Lit(MaterialsPath + "/Wall.mat", lit, new Color(.02f, .026f, .034f), .75f, .72f);
            Glow(MaterialsPath + "/Cyan.mat", unlit, Cyan);
            Glow(MaterialsPath + "/Amber.mat", unlit, Amber);
            var grid = Ensure(MaterialsPath + "/Floor.mat", floor);
            grid.SetColor("_BaseColor", new Color(.006f, .011f, .018f));
            grid.SetColor("_LineColor", new Color(.10f, .95f, 1.35f));
            grid.SetFloat("_LineWidth", .025f);
            grid.SetFloat("_MajorEvery", 4);
            grid.SetFloat("_Reflectivity", .62f);
            EditorUtility.SetDirty(grid);

            Lit(KitPath + "/Shell.mat", lit, new Color(.012f, .014f, .018f), .92f, .86f);
            Lit(KitPath + "/ShellSolar.mat", lit, new Color(.07f, .035f, .012f), .9f, .82f);
            Lit(KitPath + "/Suit.mat", lit, new Color(.022f, .025f, .03f), .35f, .58f);
            Lit(KitPath + "/Tire.mat", lit, new Color(.014f, .015f, .017f), .1f, .52f);
            Lit(KitPath + "/Structure.mat", lit, new Color(.018f, .024f, .032f), .72f, .66f);
            Glow(KitPath + "/Glow.mat", unlit, Cyan);
            Glow(KitPath + "/GlowSoft.mat", unlit, new Color(.05f, .55f, .8f));
            Glow(KitPath + "/GlowAmber.mat", unlit, Amber);
            Wall(KitPath + "/TrailCyan.mat", wall, Cyan, .42f, .07f);
            Wall(KitPath + "/TrailAmber.mat", wall, Amber, .42f, .07f);
            Wall(KitPath + "/Boundary.mat", wall, new Color(.5f, 1.4f, 2.2f), .16f, .09f);
            Ensure(KitPath + "/Sky.mat", sky);

            ConfigurePipeline();
            KeepFogVariants();
            BuildVolume();
            AssetDatabase.SaveAssets();
            Debug.Log("GRID_LOOK_APPLIED");
        }

        private static Shader Require(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new BuildFailedException("Shader unavailable: " + name + ". Resolve packages and reimport.");
            return shader;
        }

        private static Material Ensure(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader) material.shader = shader;
            return material;
        }

        private static void Lit(string path, Shader shader, Color color, float metallic, float smoothness)
        {
            var material = Ensure(path, shader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SpecularHighlights", 1);
            material.SetFloat("_EnvironmentReflections", 1);
            EditorUtility.SetDirty(material);
        }

        private static void Glow(string path, Shader shader, Color color)
        {
            var material = Ensure(path, shader);
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
        }

        private static void Wall(string path, Shader shader, Color color, float fill, float edge)
        {
            var material = Ensure(path, shader);
            material.SetColor("_Color", color);
            material.SetFloat("_FillAlpha", fill);
            material.SetFloat("_EdgeWidth", edge);
            EditorUtility.SetDirty(material);
        }

        private static void ConfigurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/GridRun/Settings/MobilePipeline.asset");
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/GridRun/Settings/MobileRenderer.asset");
            var postData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
            if (pipeline == null || renderer == null || postData == null)
                throw new BuildFailedException("Mobile pipeline, renderer, or URP post-process data is missing.");
            // HDR uses the compact 32-bit buffer on mobile; bloom needs values above 1.
            pipeline.supportsHDR = true;
            renderer.postProcessData = postData;
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(renderer);
        }

        // Fog is enabled at runtime, so automatic stripping (which inspects scenes) would remove it.
        private static void KeepFogVariants()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings.Length == 0) return;
            var serialized = new SerializedObject(settings[0]);
            serialized.FindProperty("m_FogStripping").intValue = 1;
            serialized.FindProperty("m_FogKeepLinear").boolValue = false;
            serialized.FindProperty("m_FogKeepExp").boolValue = false;
            serialized.FindProperty("m_FogKeepExp2").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildVolume()
        {
            string path = KitPath + "/GridLookVolume.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(.95f);
            bloom.intensity.Override(1.15f);
            bloom.scatter.Override(.68f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.35f);
            color.contrast.Override(12);
            color.saturation.Override(8);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.26f);
            vignette.smoothness.Override(.45f);
            foreach (var component in profile.components)
            {
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            EditorUtility.SetDirty(profile);
        }
    }
}
