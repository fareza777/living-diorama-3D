using System;
using System.IO;
using UnityEngine;

namespace LivingDiorama.Save
{
    /// <summary>
    /// Local JSON persistence. No backend, no account, no network -- the player's
    /// diorama lives on their device and nowhere else.
    ///
    /// Writes go to a temp file first and are only then swapped into place, with the
    /// previous save kept as a backup. A phone killed mid-write loses at most the last
    /// autosave, never the whole collection.
    /// </summary>
    public static class SaveService
    {
        const string FileName = "diorama.save.json";
        const string TempName = "diorama.save.tmp";
        const string BackupName = "diorama.save.bak";

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        static string TempPath => Path.Combine(Application.persistentDataPath, TempName);
        static string BackupPath => Path.Combine(Application.persistentDataPath, BackupName);

        public static bool Exists => File.Exists(SavePath);

        /// <summary>Load the save, falling back to the backup, then to a fresh game.</summary>
        public static SaveData Load()
        {
            SaveData data = TryRead(SavePath);
            if (data != null) return Migrate(data);

            data = TryRead(BackupPath);
            if (data != null)
            {
                Debug.LogWarning("[SaveService] primary save unreadable; recovered from backup");
                return Migrate(data);
            }

            return null;
        }

        static SaveData TryRead(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null || data.version <= 0)
                {
                    Debug.LogError($"[SaveService] '{path}' parsed to an invalid save");
                    return null;
                }
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveService] failed to read '{path}': {e.Message}");
                return null;
            }
        }

        public static bool Save(SaveData data)
        {
            if (data == null) return false;

            data.version = SaveData.CurrentVersion;
            data.lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            try
            {
                string json = JsonUtility.ToJson(data, false);
                File.WriteAllText(TempPath, json);

                if (File.Exists(SavePath))
                {
                    // Replace keeps a backup atomically where the platform supports it.
                    File.Replace(TempPath, SavePath, BackupPath, true);
                }
                else
                {
                    File.Move(TempPath, SavePath);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveService] failed to write save: {e.Message}");

                // Last resort: a plain overwrite is better than losing the session entirely.
                try
                {
                    File.WriteAllText(SavePath, JsonUtility.ToJson(data, false));
                    return true;
                }
                catch (Exception inner)
                {
                    Debug.LogError($"[SaveService] fallback write also failed: {inner.Message}");
                    return false;
                }
            }
        }

        public static void Delete()
        {
            foreach (string path in new[] { SavePath, TempPath, BackupPath })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[SaveService] failed to delete '{path}': {e.Message}");
                }
            }
        }

        /// <summary>Bring an older save up to the current shape. Nothing to do yet, but the
        /// seam exists so the first content update does not orphan anyone's collection.</summary>
        static SaveData Migrate(SaveData data)
        {
            if (data.version > SaveData.CurrentVersion)
            {
                Debug.LogWarning($"[SaveService] save is from a newer build (v{data.version}); " +
                                 "loading anyway, some fields may be ignored");
                return data;
            }

            // while (data.version < SaveData.CurrentVersion) { ...upgrade step... }
            data.version = SaveData.CurrentVersion;
            return data;
        }
    }
}
