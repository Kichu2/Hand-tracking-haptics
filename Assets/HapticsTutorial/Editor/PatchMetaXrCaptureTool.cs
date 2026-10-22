using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    /// <summary>
    /// Meta XR SDK 205 CaptureTool references AndroidExternalToolsSettings, which only exists
    /// when Android Build Support is installed. Re-apply this patch if a package restore brings the error back.
    /// </summary>
    [InitializeOnLoad]
    static class PatchMetaXrCaptureTool
    {
        static PatchMetaXrCaptureTool()
        {
            EditorApplication.delayCall += ApplyIfNeeded;
        }

        [MenuItem("Haptics Tutorial/Patch Meta XR CaptureTool")]
        public static void ApplyIfNeeded()
        {
            PatchCaptureTool();
            PatchOvrConfigDialogs();
            PatchAdbSingleton();
        }

        static void PatchCaptureTool()
        {
            foreach (var file in FindMetaSdkFiles("CaptureTool.cs"))
            {
                if (!PatchCaptureToolFile(file))
                    continue;
                Debug.Log($"[Haptics Tutorial] Patched Meta XR CaptureTool:\n{file}");
                AssetDatabase.Refresh();
                return;
            }
        }

        static void PatchOvrConfigDialogs()
        {
            foreach (var file in FindMetaSdkFiles("OVRConfig.cs"))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("ShouldPromptForMissingAndroidTools"))
                    continue;

                text = text.Replace(
                    "if (throwError)",
                    "if (throwError && ShouldPromptForMissingAndroidTools())");

                const string helper = @"
    static bool ShouldPromptForMissingAndroidTools()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return false;

        try
        {
            var playback = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
            return !string.IsNullOrEmpty(playback) && Directory.Exists(playback);
        }
        catch
        {
            return false;
        }
    }
";
                var insertAt = text.LastIndexOf("    }\r\n}", System.StringComparison.Ordinal);
                if (insertAt < 0)
                    insertAt = text.LastIndexOf("    }\n}", System.StringComparison.Ordinal);
                if (insertAt < 0)
                    continue;

                text = text.Insert(insertAt, helper);
                File.WriteAllText(file, text);
                Debug.Log($"[Haptics Tutorial] Patched OVRConfig so Play Mode does not require an Android SDK:\n{file}");
                AssetDatabase.Refresh();
                return;
            }
        }

        static void PatchAdbSingleton()
        {
            foreach (var file in FindMetaSdkFiles("UnityADBToolSingleton.cs"))
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("GetAndroidSDKPathLocation()"))
                    continue;

                text = text.Replace(
                    "OVRConfig.GetAndroidSDKPathLocation()",
                    "OVRConfig.GetAndroidSDKPathLocation(false)");
                File.WriteAllText(file, text);
                Debug.Log($"[Haptics Tutorial] Patched UnityADBToolSingleton:\n{file}");
                AssetDatabase.Refresh();
                return;
            }
        }

        static IEnumerable<string> FindMetaSdkFiles(string fileName)
        {
            var roots = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache"),
                Path.Combine(Directory.GetCurrentDirectory(), "Packages")
            };

            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (var file in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                {
                    if (file.IndexOf("com.meta.xr.sdk.core", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        yield return file;
                }
            }
        }

        static bool PatchCaptureToolFile(string path)
        {
            var text = File.ReadAllText(path);
            if (!text.Contains("AndroidExternalToolsSettings"))
                return false;

            text = text.Replace(
                "using Unity.Profiling;\r\nusing UnityEngine;\r\n#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX\r\nusing UnityEditor.Android;\r\n#endif",
                "using System.Reflection;\r\nusing Unity.Profiling;\r\nusing UnityEditor;\r\nusing UnityEngine;");
            text = text.Replace(
                "using Unity.Profiling;\nusing UnityEngine;\n#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX\nusing UnityEditor.Android;\n#endif",
                "using System.Reflection;\nusing Unity.Profiling;\nusing UnityEditor;\nusing UnityEngine;");

            const string helper = @"        static string GetAndroidSdkRootPath()
        {
            try
            {
                var type = Type.GetType(""UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions"");
                var path = type?.GetProperty(""sdkRootPath"", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
            catch
            {
            }

            var sdk = EditorPrefs.GetString(""AndroidSdkRoot"");
            if (!string.IsNullOrEmpty(sdk) && Directory.Exists(sdk))
                return sdk;

            return Environment.GetEnvironmentVariable(""ANDROID_SDK_ROOT"") ?? """";
        }

";

            if (!text.Contains("GetAndroidSdkRootPath()"))
            {
                text = text.Replace(
                    "    static class CaptureTool\n    {\n        static private OVRADBTool adbTool =",
                    "    static class CaptureTool\n    {\n" + helper + "        static private OVRADBTool adbTool =");
                text = text.Replace(
                    "    static class CaptureTool\r\n    {\r\n        static private OVRADBTool adbTool =",
                    "    static class CaptureTool\r\n    {\r\n" + helper.Replace("\n", "\r\n") + "        static private OVRADBTool adbTool =");
            }

            text = text.Replace("AndroidExternalToolsSettings.sdkRootPath", "GetAndroidSdkRootPath()");
            File.WriteAllText(path, text);
            return true;
        }
    }
}
