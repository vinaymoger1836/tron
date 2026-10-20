using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GridRun.Editor
{
    public static class PhaseThreeBuild
    {
        public const string ScenePath = "Assets/GridRun/Scenes/PhaseThreeTransit.unity";

        [MenuItem("Grid Run/Phase 3/Prepare Solar Transit")]
        public static void Prepare()
        {
            PhaseTwoBuild.Prepare();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var game = new GameObject("Phase 3 Solar Transit").AddComponent<TransitGame>();
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
                new EditorBuildSettingsScene(PhaseTwoBuild.ScenePath, true),
                new EditorBuildSettingsScene(ScenePath, true)
            };
            PlayerSettings.productName = "Grid Run - Feature Preview";
            PlayerSettings.bundleVersion = "0.3.0";
            PlayerSettings.Android.bundleVersionCode = 4;
            PlayerSettings.Android.forceInternetPermission = true;
            AssetDatabase.SaveAssets();
            Debug.Log("PHASE3_TRANSIT_PREPARED " + ScenePath);
        }

        private static void Bind(SerializedObject target, string field, string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/GridRun/Materials/" + name + ".mat");
            if (material == null) throw new BuildFailedException("Missing material: " + name);
            target.FindProperty(field).objectReferenceValue = material;
        }

        [MenuItem("Grid Run/Phase 3/Build Desktop Verification Player")]
        public static void BuildDesktop()
        {
            Prepare();
            string output = Path.GetFullPath("Builds/Verification/GridRunTransit.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath, PhaseOneBuild.ScenePath, PhaseTwoBuild.ScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Transit desktop build failed: " + report.summary.result);
            Debug.Log("PHASE3_DESKTOP_READY " + output);
        }

        [MenuItem("Grid Run/Phase 3/Build Development APK")]
        public static void BuildAndroid()
        {
            Prepare();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Install Android Build Support for Unity " + PhaseZeroBuild.EditorVersion);
            PlayerSettings.Android.useCustomKeystore = false;
            string output = Path.GetFullPath("Builds/Android/grid-run-phase3-preview.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhaseOneBuild.ScenePath, PhaseTwoBuild.ScenePath, ScenePath },
                locationPathName = output, target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Transit Android build failed: " + report.summary.result);
            Debug.Log("PHASE3_APK_READY " + output + " (" + report.summary.totalSize + " bytes)");
        }
    }
}
