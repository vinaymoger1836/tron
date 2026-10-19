using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GridRun.Editor
{
    public static class PhaseOneBuild
    {
        public const string ScenePath = "Assets/GridRun/Scenes/PhaseOneArena.unity";

        [MenuItem("Grid Run/Phase 1/Prepare Arena")]
        public static void Prepare()
        {
            PhaseZeroBuild.Prepare();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var game = new GameObject("Phase 1 Arena").AddComponent<ArenaGame>();
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
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Grid Run - Arena Preview";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 2;
            AssetDatabase.SaveAssets();
            Debug.Log("PHASE1_ARENA_PREPARED " + ScenePath);
        }

        private static void Bind(SerializedObject target, string field, string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GridRun/Materials/" + name + ".mat");
            if (material == null) throw new BuildFailedException("Missing Phase 0 material: " + name);
            target.FindProperty(field).objectReferenceValue = material;
        }

        [MenuItem("Grid Run/Phase 1/Build Desktop Verification Player")]
        public static void BuildDesktop()
        {
            Prepare();
            string output = Path.GetFullPath("Builds/Verification/GridRunArena.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Arena desktop build failed: " + report.summary.result);
            Debug.Log("PHASE1_DESKTOP_READY " + output);
        }

        [MenuItem("Grid Run/Phase 1/Build Development APK")]
        public static void BuildAndroid()
        {
            Prepare();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support for Unity " + PhaseZeroBuild.EditorVersion);
            PlayerSettings.Android.useCustomKeystore = false;
            string output = Path.GetFullPath("Builds/Android/grid-run-phase1-preview.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output,
                target = BuildTarget.Android, options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Arena Android build failed: " + report.summary.result);
            Debug.Log("PHASE1_APK_READY " + output + " (" + report.summary.totalSize + " bytes)");
        }
    }
}
