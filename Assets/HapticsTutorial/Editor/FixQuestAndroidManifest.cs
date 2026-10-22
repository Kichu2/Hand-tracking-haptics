using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    class FixQuestAndroidManifest : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 99999;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            Fix(Path.Combine(path, "src", "main", "AndroidManifest.xml"));
            Fix(Path.GetFullPath(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml")));
        }

        static void Fix(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                return;

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("android", "http://schemas.android.com/apk/res/android");
            var androidNs = "http://schemas.android.com/apk/res/android";

            var activities = doc.SelectNodes("//activity");
            if (activities == null)
                return;

            foreach (XmlElement activity in activities)
            {
                var name = activity.GetAttribute("name", androidNs);
                if (string.IsNullOrEmpty(name))
                    activity.SetAttribute("name", androidNs, "com.unity3d.player.UnityPlayerGameActivity");

                name = activity.GetAttribute("name", androidNs);
                if (name.Contains("UnityPlayerGameActivity") || name.Contains("UnityPlayerActivity"))
                {
                    activity.SetAttribute("enabled", androidNs, "true");
                    activity.SetAttribute("exported", androidNs, "true");
                }
            }

            doc.Save(manifestPath);
            Debug.Log($"[Haptics Tutorial] Patched Android manifest launch activity: {manifestPath}");
        }
    }
}
