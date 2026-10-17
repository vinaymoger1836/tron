using System;
using System.IO;
using GridRun;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridRun.Editor
{
    public static class PhaseZeroBuild
    {
        public const string EditorVersion = "6000.3.26f1";
        public const string ScenePath = "Assets/GridRun/Scenes/PhaseZero.unity";
        private const string SettingsPath = "Assets/GridRun/Settings";
        private const string MaterialsPath = "Assets/GridRun/Materials";

        [MenuItem("Grid Run/Phase 0/Prepare Project")]
        public static void Prepare()
        {
            if (Application.unityVersion != EditorVersion)
                throw new BuildFailedException("Use the pinned Unity Editor " + EditorVersion + ".");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Phase 0 preparation canceled; no build will run.");
            Directory.CreateDirectory(SettingsPath);
            Directory.CreateDirectory(MaterialsPath);
            Directory.CreateDirectory("Assets/GridRun/Scenes");
            AssetDatabase.Refresh();

            string pipelinePath = SettingsPath + "/MobilePipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null)
            {
                var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, SettingsPath + "/MobileRenderer.asset");
                pipeline = UniversalRenderPipelineAsset.Create(data);
                pipeline.name = "Mobile Pipeline";
                pipeline.renderScale = .85f;
                pipeline.msaaSampleCount = 2;
                pipeline.supportsHDR = false;
                pipeline.supportsCameraDepthTexture = false;
                pipeline.supportsCameraOpaqueTexture = false;
                pipeline.shadowDistance = 0;
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            // Clear per-quality overrides so every existing quality level uses the same probe baseline.
            int previousQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(previousQuality, false);
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / 120f;
            Time.maximumDeltaTime = .1f;
            EditorSettings.serializationMode = SerializationMode.ForceText;

            PlayerSettings.companyName = "Grid Run Development";
            PlayerSettings.productName = "Grid Run - Phase 0";
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.gridrun.prototype");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.renderOutsideSafeArea = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            EditorUserBuildSettings.buildAppBundle = false;

            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var probe = new GameObject("Phase 0 Arena").AddComponent<ArenaProbe>();
                var serialized = new SerializedObject(probe);
                SetMaterial(serialized, "floorMaterial", "Floor", new Color(.018f, .035f, .06f));
                SetMaterial(serialized, "gridMaterial", "Grid", new Color(.025f, .2f, .26f));
                SetMaterial(serialized, "wallMaterial", "Wall", new Color(.025f, .055f, .09f));
                SetMaterial(serialized, "cycleMaterial", "Cycle", new Color(.12f, .3f, .4f));
                SetMaterial(serialized, "glowMaterial", "Cyan", new Color(.12f, .95f, 1));
                SetMaterial(serialized, "amberMaterial", "Amber", new Color(1, .4f, .08f));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Phase 0 project prepared: URP, landscape, ARM64 IL2CPP, Android API 29 minimum / 36 target.");
        }

        private static void SetMaterial(SerializedObject target, string field, string label, Color color)
        {
            string path = MaterialsPath + "/" + label + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new BuildFailedException("URP Unlit shader is unavailable. Resolve the pinned packages first.");
                material = new Material(shader) { name = label };
                material.SetColor("_BaseColor", color);
                AssetDatabase.CreateAsset(material, path);
            }
            target.FindProperty(field).objectReferenceValue = material;
        }

        [MenuItem("Grid Run/Phase 0/Build Desktop Verification Player")]
        public static void BuildVerificationPlayer()
        {
            Prepare();
            string output = Path.GetFullPath("Builds/Verification/GridRunProbe.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Verification player failed: " + report.summary.result);
            Debug.Log("PHASE0_VERIFICATION_READY " + output);
        }

        [MenuItem("Grid Run/Phase 0/Build Development APK")]
        public static void BuildAndroid()
        {
            Prepare();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support, SDK/NDK and OpenJDK for " + EditorVersion + ".");
            // Phase 0 uses a development/debug signature only; never read a production keystore here.
            PlayerSettings.Android.useCustomKeystore = false;
            string output = Path.GetFullPath("Builds/Android/grid-run-phase0.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Android build failed: " + report.summary.result);
            Debug.Log("PHASE0_APK_READY " + output + " (" + report.summary.totalSize + " bytes)");
        }
    }
}
