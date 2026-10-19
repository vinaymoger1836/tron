using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GridRun.Editor
{
    public static class PhaseTwoBuild
    {
        public const string ScenePath = "Assets/GridRun/Scenes/PhaseTwoOnline.unity";

        [MenuItem("Grid Run/Phase 2/Prepare Online Prototype")]
        public static void Prepare()
        {
            PhaseOneBuild.Prepare();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var game = new GameObject("Phase 2 Online Arena").AddComponent<OnlineArenaGame>();
                var serialized = new SerializedObject(game);
                Bind(serialized, "floorMaterial", "Floor");
                Bind(serialized, "gridMaterial", "Grid");
                Bind(serialized, "wallMaterial", "Wall");
                Bind(serialized, "cycleMaterial", "Cycle");
                Bind(serialized, "glowMaterial", "Cyan");
                Bind(serialized, "amberMaterial", "Amber");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] {
                new EditorBuildSettingsScene(PhaseOneBuild.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
            PlayerSettings.productName = "Grid Run - Online Preview";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 3;
            PlayerSettings.Android.forceInternetPermission = true;
            AssetDatabase.SaveAssets();
            Debug.Log("PHASE2_ONLINE_PREPARED " + ScenePath);
        }

        private static void Bind(SerializedObject target, string field, string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GridRun/Materials/" + name + ".mat");
            if (material == null) throw new BuildFailedException("Missing Phase 0 material: " + name);
            target.FindProperty(field).objectReferenceValue = material;
        }

        [MenuItem("Grid Run/Phase 2/Build Desktop Verification Player")]
        public static void BuildDesktop()
        {
            Prepare();
            string output = Path.GetFullPath("Builds/Verification/GridRunOnline.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath, PhaseOneBuild.ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Online desktop build failed: " + report.summary.result);
            Debug.Log("PHASE2_DESKTOP_READY " + output);
        }

        [MenuItem("Grid Run/Phase 2/Build Development APK")]
        public static void BuildAndroid()
        {
            Prepare();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support for Unity " + PhaseZeroBuild.EditorVersion);
            PlayerSettings.Android.useCustomKeystore = false;
            string output = Path.GetFullPath("Builds/Android/grid-run-phase2-preview.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhaseOneBuild.ScenePath, ScenePath }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Online Android build failed: " + report.summary.result);
            Debug.Log("PHASE2_APK_READY " + output + " (" + report.summary.totalSize + " bytes)");
        }
    }
}
