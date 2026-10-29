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

            EnsurePermission(doc, androidNs, "horizonos.permission.HEADSET_CAMERA");
            EnsurePermission(doc, androidNs, "android.permission.CAMERA");

            var activities = doc.SelectNodes("//activity");
            if (activities != null)
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

        static void EnsurePermission(XmlDocument doc, string androidNs, string permission)
        {
            var nodes = doc.SelectNodes("/manifest/uses-permission");
            if (nodes != null)
            {
                foreach (XmlElement node in nodes)
                {
                    if (node.GetAttribute("name", androidNs) == permission)
                        return;
                }
            }

            var element = doc.CreateElement("uses-permission");
            element.SetAttribute("name", androidNs, permission);
            doc.DocumentElement.InsertBefore(element, doc.DocumentElement.FirstChild);
        }
    }
}
