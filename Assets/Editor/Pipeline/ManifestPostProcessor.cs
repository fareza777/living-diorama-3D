using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Strips the INTERNET permission from the generated Android manifest.
    ///
    /// The game is genuinely offline: no backend, no accounts, no telemetry, and the only
    /// network code in the repository is the asset pipelines, which run on a desktop and
    /// never ship. Unity adds INTERNET anyway, and a permission a game does not need is
    /// worth removing on its own terms -- it is the difference between saying the game is
    /// offline and the install screen agreeing.
    ///
    /// The exception is an AdMob build, which obviously does need the network, so the
    /// permission is left alone whenever LD_ADMOB is defined.
    /// </summary>
    public sealed class ManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
#if LD_ADMOB
            Debug.Log("[ManifestPostProcessor] LD_ADMOB is on; leaving INTERNET in place");
#else
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[ManifestPostProcessor] no manifest at {manifestPath}");
                return;
            }

            var document = new XmlDocument();
            document.Load(manifestPath);

            XmlNodeList permissions = document.SelectNodes("/manifest/uses-permission");
            if (permissions == null) return;

            int removed = 0;
            for (int i = permissions.Count - 1; i >= 0; i--)
            {
                if (permissions[i] is not XmlElement element) continue;

                string name = element.GetAttribute("android:name");
                if (name != "android.permission.INTERNET") continue;

                element.ParentNode?.RemoveChild(element);
                removed++;
            }

            if (removed == 0) return;

            document.Save(manifestPath);
            Debug.Log($"[ManifestPostProcessor] removed {removed} INTERNET permission " +
                      "declaration(s); this build cannot reach the network");
#endif
        }
    }
}
