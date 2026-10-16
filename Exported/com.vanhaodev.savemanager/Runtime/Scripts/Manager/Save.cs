using System;
using System.Collections.Generic;
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
    /// All keys always load from the same save: a crash during saving falls back to the
    /// last complete save, never a mix of old and new files.<br/>
    /// <br/>
    /// Simple rule:<br/>
    /// - Normal save → SetAsync<br/>
    /// - Related data (quest + money) → wrap in Save.BeginTransaction()<br/>
    /// - Important save / quit / pause → Set or Flush
    /// </summary>
    public static partial class Save
    {
        private static readonly IReadOnlyDictionary<string, byte[]> NoItems = new Dictionary<string, byte[]>();

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Under IoLock so a still-running worker cannot commit between the two resets.
            lock (SaveFileStore.IoLock)
            {
                SaveManifestStore.Reset();
                SaveQueue.Reset();
            }
            SaveTransaction.Reset();
            ResetEvents();
        }

        /// <summary>
        /// Save data immediately (sync).
        /// Also writes everything still pending, in the same save.
        /// Safe but may block a little.
        /// </summary>
        public static void Set(string key, ISaveable data, EncryptionType encryption = EncryptionType.None)
        {
            ValidateKey(key);
            EnsureLoaded();
            WriteNow(key, Serialize(data, encryption));
        }

        /// <summary>
        /// Save data in background (async).
        ///
        /// forceSync = false:
        ///     Save in background, no lag, but may delay.
        ///     Inside a transaction, waits for Commit + end of block.
        ///
        /// forceSync = true:
        ///     Save immediately like Set(), safe for exit game.
        /// </summary>
        public static void SetAsync(string key, ISaveable data, EncryptionType encryption = EncryptionType.None, bool forceSync = false)
        {
            ValidateKey(key);
            EnsureLoaded();
            var bytes = Serialize(data, encryption);

            if (forceSync)
            {
                // Save now (safe)
                WriteNow(key, bytes);
            }
            else if (!SaveTransaction.TryAdd(key, bytes))
            {
                // Save later (background)
                SaveQueue.Enqueue(new Dictionary<string, byte[]> { [key] = bytes });
            }
        }

        /// <summary>
        /// Save several keys together: all of them or none.
        /// Without Commit() nothing inside is saved (a warning is logged).
        /// <code>
        /// using (var tx = Save.BeginTransaction())
        /// {
        ///     Save.SetAsync("quest", quest);
        ///     Save.SetAsync("money", money);
        ///     tx.Commit();
        /// }
        /// </code>
        /// </summary>
        public static SaveTransaction BeginTransaction()
        {
            return new SaveTransaction();
        }

        /// <summary>
        /// Write everything pending right now (sync). Use on quit / pause.
        /// Throws if writing fails; the data stays pending for the next save.
        /// </summary>
        public static void Flush()
        {
            EnsureLoaded();
            SaveQueue.CommitNow(NoItems);
        }

        /// <summary>
        /// Load data. Return default if the key was never saved.
        /// Throws InvalidDataException if the file is broken or was edited (IntegrityFailed is raised too).
        /// Data still waiting in the background queue is not visible until written.
        /// </summary>
        public static T Get<T>(string key) where T : ISaveable, new()
        {
            ValidateKey(key);
            EnsureLoaded();

            byte[] bytes;
            try
            {
                lock (SaveFileStore.IoLock)
                {
                    bytes = SaveManifestStore.ReadVerified(key);
                }
            }
            catch (InvalidDataException e)
            {
                Report(e.Message);
                throw;
            }

            return bytes == null ? default : Deserialize<T>(bytes);
        }

        /// <summary>
        /// Check if key has been saved (written to disk).
        /// </summary>
        public static bool Exists(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            return SaveManifestStore.Contains(key);
        }

        /// <summary>
        /// Delete key (sync). Also cancels a pending background save of this key.
        /// </summary>
        public static void Delete(string key)
        {
            ValidateKey(key);
            EnsureLoaded();
            WriteNow(key, null);
        }

        // Sync write of one key (null = delete). Drops the key from an open transaction,
        // otherwise the transaction's older value would overwrite this one when it closes.
        private static void WriteNow(string key, byte[] bytes)
        {
            SaveTransaction.Remove(key);
            SaveQueue.CommitNow(new Dictionary<string, byte[]> { [key] = bytes });
        }

        // First call loads the manifest on the calling (main) thread, so integrity
        // problems are reported there and the worker never has to load.
        private static void EnsureLoaded()
        {
            if (SaveManifestStore.IsLoaded)
                return;

            CaptureMainThread();
            var problems = new List<string>();
            lock (SaveFileStore.IoLock)
            {
                SaveManifestStore.EnsureLoaded(problems);
            }

            foreach (var problem in problems)
                Report(problem);
        }

        private static void Report(string message)
        {
            Debug.LogWarning($"[SaveManager] {message}");
            RaiseIntegrityFailed(message);
        }

        // Key becomes part of a file name.
        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Save key must not be empty.", nameof(key));
            if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Save key '{key}' contains characters not allowed in file names.", nameof(key));
        }

        // Header (not encrypted) + data (encrypted)
        private static byte[] Serialize(ISaveable data, EncryptionType encryption)
        {
            var headerWriter = new SaveWriter();
            SaveHeader.Write(headerWriter, encryption);

            var dataWriter = new SaveWriter();
            data.WriteSave(dataWriter);
            var dataBytes = SaveEncryption.Encrypt(dataWriter.ToArray(), encryption);

            var finalWriter = new SaveWriter();
            finalWriter.AddBytes(headerWriter.ToArray());
            finalWriter.AddBytes(dataBytes);
            return finalWriter.ToArray();
        }

        private static T Deserialize<T>(byte[] bytes) where T : ISaveable, new()
        {
            // Read header first (not encrypted)
            var headerReader = new SaveReader(bytes);
            var encryption = SaveHeader.Read(headerReader);

            // Decrypt data part (after header)
            var dataBytes = new byte[bytes.Length - SaveHeader.HEADER_SIZE];
            Array.Copy(bytes, SaveHeader.HEADER_SIZE, dataBytes, 0, dataBytes.Length);
            dataBytes = SaveEncryption.Decrypt(dataBytes, encryption);

            // Read data
            var dataReader = new SaveReader(dataBytes);
            var data = new T();
            data.ReadSave(dataReader);
            return data;
        }
    }
}
