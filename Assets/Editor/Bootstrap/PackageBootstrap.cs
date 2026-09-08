using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// One-shot package installer, run from CI or the command line so the project
    /// can be reconstructed from a clean checkout without opening the editor UI:
    ///   Unity.exe -batchmode -quit -executeMethod LivingDiorama.EditorTools.PackageBootstrap.Install
    /// Versions are intentionally omitted so the Package Manager resolves whatever
    /// is compatible with the current editor.
    /// </summary>
    public static class PackageBootstrap
    {
        static readonly string[] Required =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.cloud.gltfast",
            "com.unity.test-framework",
            "com.unity.mathematics",
            "com.unity.burst",
            "com.unity.collections",
        };

        public static void Install()
        {
            var failures = new List<string>();

            foreach (var id in Required)
            {
                Debug.Log($"[PackageBootstrap] adding {id}");
                AddRequest request = Client.Add(id);
                while (!request.IsCompleted)
                {
                    System.Threading.Thread.Sleep(100);
                }

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[PackageBootstrap] installed {request.Result.packageId}");
                }
                else
                {
                    string error = request.Error != null ? request.Error.message : "unknown error";
                    Debug.LogError($"[PackageBootstrap] FAILED {id}: {error}");
                    failures.Add($"{id}: {error}");
                }
            }

            if (failures.Count > 0)
            {
                Console.Error.WriteLine("[PackageBootstrap] " + failures.Count + " package(s) failed");
                EditorApplication.Exit(1);
            }

            Debug.Log("[PackageBootstrap] all packages installed");
        }
    }
}
