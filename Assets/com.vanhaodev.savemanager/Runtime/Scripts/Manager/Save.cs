using System.IO;
using UnityEngine;

namespace vanhaodev.savemanager
{
    /// <summary>
    /// Save system overview:<br/>
    /// <br/>
    /// There are 2 ways to save:<br/>
    /// <br/>
    /// 1. SetAsync (recommended):<br/>
    /// - Save in background (no lag)<br/>
    /// - Game continues normally<br/>
    /// - Save may finish later<br/>
    /// - Use for normal gameplay<br/>
    ///   (inventory, settings, etc)<br/>
    /// <br/>
    /// 2. Set (or SetAsync forceSync):<br/>
    /// - Save immediately (sync)<br/>
    /// - May freeze for short time<br/>
    /// - Data is safe on disk<br/>
    /// - Use for important moments<br/>
    ///   (exit game, checkpoint)<br/>
    /// <br/>
    /// Simple rule:<br/>
    /// - Normal save → SetAsync<br/>
    /// - Important save → Set
    /// </summary>
    public static class Save
    {
        /// <summary>
        /// Get full file path from key.
        /// File is saved in persistentDataPath.
        /// </summary>
        private static string GetPath(string key)
        {
            return Path.Combine(Application.persistentDataPath, key + ".dat");
        }

        /// <summary>
        /// Save data immediately (sync).
        /// This will write file right away.
        /// Safe but may block a little.
        /// </summary>
        public static void Set(string key, ISaveable data)
        {
            var path = GetPath(key);

            var writer = new SaveWriter();

            SaveHeader.Write(writer);
            data.WriteSave(writer);

            var bytes = writer.ToArray();

            WriteAtomic(path, bytes);
        }

        /// <summary>
        /// Save data in background (async).
        /// 
        /// forceSync = false:
        ///     Save in background, no lag, but may delay.
        /// 
        /// forceSync = true:
        ///     Save immediately like Set(), safe for exit game.
        /// </summary>
        public static void SetAsync(string key, ISaveable data, bool forceSync = false)
        {
            var path = GetPath(key);

            var writer = new SaveWriter();

            SaveHeader.Write(writer);
            data.WriteSave(writer);

            var bytes = writer.ToArray();

            if (forceSync)
            {
                // Save now (safe)
                WriteAtomic(path, bytes);
            }
            else
            {
                // Save later (background)
                SaveQueue.Enqueue(path, bytes);
            }
        }

        /// <summary>
        /// Write file using atomic method.
        /// Write to temp file first, then replace main file.
        /// This prevents file corruption if crash happens.
        /// </summary>
        private static void WriteAtomic(string path, byte[] bytes)
        {
            var tempPath = path + ".tmp";

            File.WriteAllBytes(tempPath, bytes);

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tempPath, path);
        }

        /// <summary>
        /// Load data from file.
        /// Return default if file not exists.
        /// </summary>
        public static T Get<T>(string key) where T : ISaveable, new()
        {
            var path = GetPath(key);
            var tempPath = path + ".tmp";

            // If temp file exists (crash before), remove it
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            if (!File.Exists(path))
                return default;

            var bytes = File.ReadAllBytes(path);
            var reader = new SaveReader(bytes);

            SaveHeader.Read(reader);

            var data = new T();
            data.ReadSave(reader);

            return data;
        }

        /// <summary>
        /// Check if save file exists.
        /// </summary>
        public static bool Exists(string key)
        {
            return File.Exists(GetPath(key));
        }

        /// <summary>
        /// Delete save file by key.
        /// </summary>
        public static void Delete(string key)
        {
            var path = GetPath(key);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}