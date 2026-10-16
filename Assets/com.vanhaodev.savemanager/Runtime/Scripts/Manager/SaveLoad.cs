using System;
using System.IO;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace vanhaodev.savemanager
{
    /// <summary>Loading: Get (throws when broken) and TryGet (returns a status).</summary>
    public static partial class Save
    {
        /// <summary>
        /// Load data. Return default if the key was never saved.
        /// Throws InvalidDataException if the save is broken or was edited.
        /// Data still waiting in the background queue is not visible until written.
        /// </summary>
        public static T Get<T>(string key) where T : ISaveable, new()
        {
            return GetOrThrow<T>(key, null);
        }

        /// <summary>
        /// Load data that MUST use the given encryption. A file stored with another type
        /// is rejected with InvalidDataException. Recommended for any encrypted save.
        /// </summary>
        public static T Get<T>(string key, EncryptionType required) where T : ISaveable, new()
        {
            return GetOrThrow<T>(key, required);
        }

        /// <summary>
        /// Load without exceptions: tells "never saved" (NotFound) apart from "save exists but
        /// is unusable" (Corrupt / WrongEncryption). data is default unless the result is Ok.
        /// <code>
        /// switch (Save.TryGet("player", out PlayerData player))
        /// {
        ///     case SaveLoadStatus.Ok: break;
        ///     case SaveLoadStatus.NotFound: player = new PlayerData(); break;
        ///     default: ShowSaveProblem(); break;
        /// }
        /// </code>
        /// </summary>
        public static SaveLoadStatus TryGet<T>(string key, out T data) where T : ISaveable, new()
        {
            return Load(key, null, out data, out _);
        }

        /// <summary>TryGet that also requires the given encryption (WrongEncryption otherwise).</summary>
        public static SaveLoadStatus TryGet<T>(string key, EncryptionType required, out T data) where T : ISaveable, new()
        {
            return Load(key, required, out data, out _);
        }

        private static T GetOrThrow<T>(string key, EncryptionType? required) where T : ISaveable, new()
        {
            var status = Load<T>(key, required, out var data, out var error);
            if (status == SaveLoadStatus.Ok || status == SaveLoadStatus.NotFound)
                return data;

            ExceptionDispatchInfo.Capture(error).Throw();
            return default; // unreachable
        }

        private static SaveLoadStatus Load<T>(string key, EncryptionType? required, out T data, out Exception error)
            where T : ISaveable, new()
        {
            ValidateKey(key);
            EnsureLoaded();
            data = default;
            error = null;

            // 1. File must match its fingerprint in the (signed) save index.
            byte[] bytes;
            try
            {
                lock (SaveFileStore.IoLock)
                {
                    bytes = SaveManifestStore.ReadVerified(key);
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException)
            {
                error = e;
                Report(e.Message);
                return SaveLoadStatus.Corrupt;
            }

            if (bytes == null)
                return SaveLoadStatus.NotFound;

            // 2. The header is only a hint: the caller decides which encryption is acceptable.
            EncryptionType stored;
            try
            {
                stored = SaveHeader.Read(new SaveReader(bytes));
            }
            catch (Exception e)
            {
                error = new InvalidDataException($"Save '{key}' has an invalid header.", e);
                Report(error.Message);
                return SaveLoadStatus.Corrupt;
            }

            if (required.HasValue && stored != required.Value)
            {
                error = new InvalidDataException($"Save '{key}' is stored as {stored}, expected {required.Value}.");
                Report(error.Message);
                return SaveLoadStatus.WrongEncryption;
            }

            // 3. Decrypt + read. The file is exactly what was written, so a failure here means the
            //    data class changed or the key does not fit — not tampering (no IntegrityFailed).
            try
            {
                data = Deserialize<T>(bytes, stored);
                return SaveLoadStatus.Ok;
            }
            catch (Exception e)
            {
                error = new InvalidDataException($"Save '{key}' could not be read: {e.Message}", e);
                Debug.LogWarning($"[SaveManager] {error.Message}");
                data = default;
                return SaveLoadStatus.Corrupt;
            }
        }

        private static T Deserialize<T>(byte[] bytes, EncryptionType encryption) where T : ISaveable, new()
        {
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
