using System;
using System.Collections.Generic;
using System.IO;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// The save as seen by migration steps. Changes stay in memory: a step sees what earlier
	/// steps changed, and nothing reaches the disk until every step and Verify succeeded.
	/// </summary>
	public sealed class SaveMigrationContext
	{
		// Plain data of a key (null = deleted) and how it is encrypted on disk.
		private readonly struct Slot
		{
			internal readonly byte[] Data;
			internal readonly EncryptionType Encryption;

			internal Slot(byte[] data, EncryptionType encryption)
			{
				Data = data;
				Encryption = encryption;
			}
		}

		private readonly HashSet<string> _savedKeys;
		private readonly Func<string, byte[]> _readFile;
		private readonly Dictionary<string, Slot> _changes = new();
		private readonly Dictionary<string, Slot> _readCache = new();

		/// <summary>Schema version of the save before migrating.</summary>
		public int SaveVersion { get; }

		/// <summary>Save.SchemaVersion of the game (where the migration ends).</summary>
		public int GameVersion { get; }

		/// <summary>Start version of the step running now (GameVersion during Verify).</summary>
		public int FromVersion { get; internal set; }

		/// <summary>End version of the step running now (GameVersion during Verify).</summary>
		public int ToVersion { get; internal set; }

		/// <param name="readFile">File bytes of a saved key (fingerprint already checked).</param>
		internal SaveMigrationContext(int saveVersion, int gameVersion, IEnumerable<string> savedKeys, Func<string, byte[]> readFile)
		{
			SaveVersion = saveVersion;
			GameVersion = gameVersion;
			_savedKeys = new HashSet<string>(savedKeys);
			_readFile = readFile;
		}

		/// <summary>True if the key exists (after the changes of earlier steps).</summary>
		public bool Exists(string key) => TryGetSlot(key, out _);

		/// <summary>All keys that exist now.</summary>
		public IReadOnlyCollection<string> Keys
		{
			get
			{
				var keys = new HashSet<string>(_savedKeys);
				foreach (var kv in _changes)
				{
					if (kv.Value.Data == null) keys.Remove(kv.Key);
					else keys.Add(kv.Key);
				}

				return keys;
			}
		}

		/// <summary>Read a key with a data class (old or current). Default if the key does not exist.</summary>
		public T Get<T>(string key) where T : ISaveable, new()
		{
			if (!TryGetSlot(key, out var slot))
				return default;

			var data = new T();
			data.ReadSave(new SaveReader(slot.Data));
			return data;
		}

		/// <summary>Read a key field by field, without keeping an old data class. Throws if missing.</summary>
		public T Read<T>(string key, Func<SaveReader, T> read)
		{
			if (read == null)
				throw new ArgumentNullException(nameof(read));
			return read(new SaveReader(Require(key).Data));
		}

		/// <summary>
		/// Write a key with a data class. Encryption: given, else what the key used, else AES.
		/// </summary>
		public void Set(string key, ISaveable data, EncryptionType? encryption = null)
		{
			if (data == null)
				throw new ArgumentNullException(nameof(data));
			Write(key, data.WriteSave, encryption);
		}

		/// <summary>Write a key field by field.</summary>
		public void Write(string key, Action<SaveWriter> write, EncryptionType? encryption = null)
		{
			Save.ValidateKey(key);
			if (write == null)
				throw new ArgumentNullException(nameof(write));

			var w = new SaveWriter();
			write(w);

			var keep = TryGetSlot(key, out var old) ? old.Encryption : EncryptionType.AES;
			_changes[key] = new Slot(w.ToArray(), encryption ?? keep);
		}

		/// <summary>Remove a key (nothing happens if it does not exist).</summary>
		public void Delete(string key)
		{
			if (Exists(key))
				_changes[key] = default;
		}

		/// <summary>Move a key's data to a new key name. Throws if from is missing or to exists.</summary>
		public void Rename(string from, string to)
		{
			Save.ValidateKey(to);
			var slot = Require(from);
			if (Exists(to))
				throw new InvalidOperationException($"Cannot rename '{from}' to '{to}': '{to}' already exists.");

			_changes[to] = slot;
			_changes[from] = default;
		}

		/// <summary>File bytes to commit: key -> bytes, or key -> null to delete. Only changed keys.</summary>
		internal Dictionary<string, byte[]> BuildFileChanges()
		{
			var files = new Dictionary<string, byte[]>();
			foreach (var kv in _changes)
			{
				if (kv.Value.Data != null)
					files[kv.Key] = Save.BuildFile(kv.Value.Data, kv.Value.Encryption);
				else if (_savedKeys.Contains(kv.Key))
					files[kv.Key] = null;
			}

			return files;
		}

		private Slot Require(string key)
		{
			if (!TryGetSlot(key, out var slot))
				throw new KeyNotFoundException($"Save key '{key}' does not exist (check with ctx.Exists).");
			return slot;
		}

		private bool TryGetSlot(string key, out Slot slot)
		{
			if (_changes.TryGetValue(key, out slot))
				return slot.Data != null;

			if (!_savedKeys.Contains(key))
				return false;

			if (!_readCache.TryGetValue(key, out slot))
			{
				slot = ReadSaved(key);
				_readCache[key] = slot;
			}

			return true;
		}

		private Slot ReadSaved(string key)
		{
			try
			{
				var file = _readFile(key) ?? throw new InvalidDataException("data file is missing");
				var encryption = SaveHeader.Read(new SaveReader(file));
				return new Slot(Save.ReadPlainData(file, encryption), encryption);
			}
			catch (Exception e)
			{
				throw new InvalidDataException($"Save '{key}' could not be read for migration: {e.Message}", e);
			}
		}
	}
}
