using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        private static readonly IReadOnlyDictionary<string, PendingWrite> NoItems = new Dictionary<string, PendingWrite>();

        // Static state survives play mode when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            // Under IoLock so a still-running worker cannot commit between the two resets.
            lock (SaveFileStore.IoLock)
            {
                SaveManifestStore.Reset();
                SaveQueue.Reset();
            }
            SaveTransaction.Reset();
            SaveEncryption.ResetKey();
            ResetEvents();
            ResetSchema();
            ResetMigration();
            ResetBackup();
        }

        /// <summary>
        /// Save data immediately (sync).
        /// Also writes everything still pending, in the same save.
        /// Safe but may block a little.
        /// </summary>
        public static void Set(string key, ISaveable data, EncryptionType encryption = EncryptionType.AES)
        {
            ValidateKey(key);
            EnsureLoaded();
            ThrowIfWriteBlocked();
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
        ///
        /// Returns a Task you may await (optional): completes when this data (or newer data
        /// for the same key) is on disk, fails if writing fails, is canceled if the transaction
        /// it belongs to is discarded. After await, code continues on the main thread.
        /// <code>
        /// await Save.SetAsync("player", player); // e.g. before confirming a purchase
        /// </code>
        /// </summary>
        public static Task SetAsync(string key, ISaveable data, EncryptionType encryption = EncryptionType.AES, bool forceSync = false)
        {
            ValidateKey(key);
            EnsureLoaded();
            if (IsWriteBlocked)
                return BlockedWriteTask(key);

            var bytes = Serialize(data, encryption);

            if (forceSync)
            {
                // Save now (safe)
                WriteNow(key, bytes);
                return Task.CompletedTask;
            }

            var waiter = PendingWrite.NewWaiter();
            var write = new PendingWrite(bytes);
            write.Waiters.Add(waiter);

            if (!SaveTransaction.TryAdd(key, write))
            {
                // Save later (background)
                SaveQueue.Enqueue(new Dictionary<string, PendingWrite> { [key] = write });
            }

            return waiter.Task;
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
        /// Write everything pending in the background and complete when it is on disk
        /// (also waits for a save already in progress). Fails if writing fails.
        /// <code>
        /// Save.SetAsync("player", player);
        /// Save.SetAsync("inventory", inventory);
        /// await Save.FlushAsync();
        /// </code>
        /// </summary>
        public static Task FlushAsync()
        {
            EnsureLoaded();
            return SaveQueue.FlushAsync();
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
            ThrowIfWriteBlocked();
            WriteNow(key, null);
        }

        // Sync write of one key (null = delete). Drops the key from an open transaction,
        // otherwise the transaction's older value would overwrite this one when it closes.
        private static void WriteNow(string key, byte[] bytes)
        {
            // Awaiters of the dropped transaction value complete with this (newer) write.
            var write = SaveTransaction.Remove(key) ?? new PendingWrite(bytes);
            write.Data = bytes;
            SaveQueue.CommitNow(new Dictionary<string, PendingWrite> { [key] = write });
        }

        // Every public Save call: load once, then make sure the save is migrated (one bool
        // check when it already is).
        private static void EnsureLoaded()
        {
            LoadIfNeeded();
            EnsureMigrated();
        }

        // First call loads the manifest on the calling (main) thread, so integrity
        // problems are reported there and the worker never has to load.
        // Done when the version check finished too, so no call can write in between. A hook
        // raised by the check that calls Save again (same thread) passes straight through.
        private static void LoadIfNeeded()
        {
            if (_schemaChecked)
                return;

            lock (LoadLock)
            {
                if (_schemaChecked)
                    return;

                CaptureMainThread();
                var problems = new List<string>();
                lock (SaveFileStore.IoLock)
                {
                    SaveManifestStore.EnsureLoaded(problems, _schemaVersion);
                }

                foreach (var problem in problems)
                    Report(problem);

                CheckSchema();
            }
        }

        private static void Report(string message)
        {
            Debug.LogWarning($"[SaveManager] {message}");
            RaiseIntegrityFailed(message);
        }

        // Key becomes part of a file name.
        internal static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Save key must not be empty.", nameof(key));
            if (key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Save key '{key}' contains characters not allowed in file names.", nameof(key));
        }

        private static byte[] Serialize(ISaveable data, EncryptionType encryption)
        {
            var dataWriter = new SaveWriter();
            data.WriteSave(dataWriter);
            return BuildFile(dataWriter.ToArray(), encryption);
        }

        /// <summary>File bytes: header (not encrypted) + data (encrypted).</summary>
        internal static byte[] BuildFile(byte[] plainData, EncryptionType encryption)
        {
            var finalWriter = new SaveWriter();
            SaveHeader.Write(finalWriter, encryption);
            finalWriter.AddBytes(SaveEncryption.Encrypt(plainData, encryption));
            return finalWriter.ToArray();
        }

        /// <summary>Plain data of a file (header removed, decrypted with the current key).</summary>
        internal static byte[] ReadPlainData(byte[] fileBytes, EncryptionType encryption)
        {
            var dataBytes = new byte[fileBytes.Length - SaveHeader.HEADER_SIZE];
            Array.Copy(fileBytes, SaveHeader.HEADER_SIZE, dataBytes, 0, dataBytes.Length);
            return SaveEncryption.Decrypt(dataBytes, encryption);
        }

    }
}
