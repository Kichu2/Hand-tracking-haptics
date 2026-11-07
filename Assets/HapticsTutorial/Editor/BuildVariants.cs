using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    /// <summary>
    /// Builds the two comparison versions of the tutorial to the connected Quest. The no-props
    /// version gets its own app name and package, so both can be installed side by side.
    /// </summary>
    static class BuildVariants
    {
        const string PlayModeMenu = "Haptics Tutorial/Play Mode Without Props";
        const string NoPropsDefine = "HAPTICS_NO_PROPS";

        [MenuItem("Haptics Tutorial/Build and Run: With Props", priority = 100)]
        static void BuildWithProps() => Build(false);

        [MenuItem("Haptics Tutorial/Build and Run: Without Props", priority = 101)]
        static void BuildWithoutProps() => Build(true);

        [MenuItem(PlayModeMenu, priority = 120)]
        static void TogglePlayModeWithoutProps()
        {
            var noProps = !EditorPrefs.GetBool(TutorialMode.EditorNoPropsKey, false);
            EditorPrefs.SetBool(TutorialMode.EditorNoPropsKey, noProps);
            Debug.Log($"[Haptics Tutorial] Play Mode now runs the {(noProps ? "no-props" : "with-props")} version.");
        }

        [MenuItem(PlayModeMenu, true)]
        static bool TogglePlayModeWithoutPropsValidate()
        {
            Menu.SetChecked(PlayModeMenu, EditorPrefs.GetBool(TutorialMode.EditorNoPropsKey, false));
            return true;
        }

        static void Build(bool noProps)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("Haptics Tutorial", "Switch the build target to Android first (File > Build Profiles).", "OK");
                return;
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Haptics Tutorial", "No scenes are enabled in the build settings.", "OK");
                return;
            }

            var productName = PlayerSettings.productName;
            var packageName = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
            var file = noProps ? "HandTrackingHaptics_NoProps.apk" : "HandTrackingHaptics.apk";
            Directory.CreateDirectory("Builds");
            try
            {
                if (noProps)
                {
                    PlayerSettings.productName = productName + " (No Props)";
                    PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, packageName + ".noprops");
                }

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine("Builds", file),
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.AutoRunPlayer,
                    extraScriptingDefines = noProps ? new[] { NoPropsDefine } : Array.Empty<string>()
                };

                var report = BuildPipeline.BuildPlayer(options);
                var ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log(ok
                    ? $"[Haptics Tutorial] Built and installed {(noProps ? "the no-props" : "the with-props")} version: Builds/{file}"
                    : $"[Haptics Tutorial] Build failed: {report.summary.result}. See the Console for errors.");
            }
            finally
            {
                PlayerSettings.productName = productName;
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, packageName);
            }
        }
    }
}
