using System.Collections.Generic;
using System.IO;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Owns the on-disk manifest and the data files it points to.<br/>
	/// Commit order guarantees all keys always come from the same save:<br/>
	/// 1. write new data files under a new generation (old ones untouched)<br/>
	/// 2. atomically replace the manifest (the commit point)<br/>
	/// 3. delete files no manifest refers to any more<br/>
	/// A crash before step 2 leaves the previous manifest, which still points to a complete set.<br/>
	/// All members require <see cref="SaveFileStore.IoLock"/>.
	/// </summary>
	internal static class SaveManifestStore
	{
		// manifest.dat contents, and manifest.dat.bak contents (its files are kept as fallback).
		private static SaveManifest _current;
		private static SaveManifest _backup;
		private static volatile bool _loaded;

		// False when manifest.dat on disk is broken and was not replaced. The next commit
		// must then delete it instead of moving it over the good manifest.dat.bak.
		private static bool _mainFileIsGood;

		internal static bool IsLoaded => _loaded;

		internal static void Reset()
		{
			_current = null;
			_backup = null;
			_mainFileIsGood = false;
			_loaded = false;
		}

		internal static bool Contains(string key) => _current != null && _current.TryGet(key, out _);

		/// <summary>
		/// Load the newest manifest whose files all pass the fingerprint check.
		/// Falls back to the backup manifest (previous complete save) if the main one
		/// or any of its files is broken / swapped / edited. Problems are appended to
		/// <paramref name="problems"/> so the caller can report them outside the lock.
		/// </summary>
		internal static void EnsureLoaded(List<string> problems)
		{
			if (_loaded)
				return;

			SaveFileStore.EnsureFolder();

			var path = SaveFileStore.ManifestPath;
			var bakPath = SaveFileStore.BakPath(path);

			var main = TryLoadVerified(path, problems);
			if (main != null)
			{
				_current = main;
				_backup = TryParse(bakPath);
				_mainFileIsGood = true;
			}
			else
			{
				var backup = TryLoadVerified(bakPath, problems);
				if (backup != null)
				{
					if (File.Exists(path))
						problems.Add("Save data failed integrity check, restored previous save.");

					_current = backup;
					_backup = null;
					_mainFileIsGood = RestoreMainFromBackup(path, bakPath);
				}
				else
				{
					// Nothing fully valid: keep whatever parses so intact keys still load,
					// broken keys fail in ReadVerified. Keep the backup's files too — they
					// may hold the only good copy of some keys.
					var mainParsed = TryParse(path);
					var bakParsed = TryParse(bakPath);
					_current = mainParsed ?? bakParsed ?? SaveManifest.Empty;
					_backup = mainParsed != null ? bakParsed : null;
					_mainFileIsGood = mainParsed != null || !File.Exists(path);
				}
			}

			DeleteOrphans();
			_loaded = true;
		}

		/// <summary>
		/// Write one complete save. changes: key -> bytes, or key -> null to remove the key.
		/// On failure nothing changes on disk (new files are removed) and the exception propagates.
		/// </summary>
		internal static void Commit(IReadOnlyDictionary<string, byte[]> changes)
		{
			if (!_loaded)
				throw new System.InvalidOperationException("Save manifest is not loaded.");

			var generation = _current.Generation + 1;
			var entries = _current.CopyEntries();
			var written = new List<string>();

			try
			{
				foreach (var kv in changes)
				{
					if (kv.Value == null)
					{
						entries.Remove(kv.Key);
						continue;
					}

					var dataPath = SaveFileStore.DataPath(kv.Key, generation);
					SaveFileStore.WriteDurable(dataPath, kv.Value);
					written.Add(dataPath);
					entries[kv.Key] = new SaveManifest.Entry(generation, SaveFileStore.Hash(kv.Value));
				}

				var next = new SaveManifest(generation, entries);

				// A broken manifest.dat must not be rotated into manifest.dat.bak.
				if (!_mainFileIsGood && File.Exists(SaveFileStore.ManifestPath))
					File.Delete(SaveFileStore.ManifestPath);

				SaveFileStore.WriteAtomic(SaveFileStore.ManifestPath, next.Serialize());
				_mainFileIsGood = true;

				var dropped = _backup;
				_backup = _current;
				_current = next;
				DeleteUnreferenced(dropped);
			}
			catch
			{
				foreach (var file in written)
					SaveFileStore.TryDelete(file);
				throw;
			}
		}

		/// <summary>
		/// Bytes of the current file for key, or null if the key is not saved.
		/// Throws InvalidDataException if the file does not match its fingerprint.
		/// </summary>
		internal static byte[] ReadVerified(string key)
		{
			if (!_current.TryGet(key, out var entry))
				return null;

			var dataPath = SaveFileStore.DataPath(key, entry.Generation);
			if (!File.Exists(dataPath))
				throw new InvalidDataException($"Save '{key}' is missing its data file.");

			var bytes = File.ReadAllBytes(dataPath);
			if (!SaveFileStore.HashEquals(SaveFileStore.Hash(bytes), entry.Hash))
				throw new InvalidDataException($"Save '{key}' failed integrity check.");

			return bytes;
		}

		private static SaveManifest TryParse(string path)
		{
			try
			{
				return File.Exists(path) ? SaveManifest.Deserialize(File.ReadAllBytes(path)) : null;
			}
			catch (System.Exception)
			{
				return null;
			}
		}

		// Manifest is valid only if it parses AND every file it points to matches its fingerprint.
		private static SaveManifest TryLoadVerified(string path, List<string> problems)
		{
			if (!File.Exists(path))
				return null;

			var manifest = TryParse(path);
			if (manifest == null)
			{
				problems.Add($"{Path.GetFileName(path)} is corrupt.");
				return null;
			}

			foreach (var kv in manifest.Entries)
			{
				var dataPath = SaveFileStore.DataPath(kv.Key, kv.Value.Generation);
				if (!File.Exists(dataPath) ||
				    !SaveFileStore.HashEquals(SaveFileStore.Hash(File.ReadAllBytes(dataPath)), kv.Value.Hash))
				{
					problems.Add($"Save '{kv.Key}' failed integrity check ({Path.GetFileName(path)}).");
					return null;
				}
			}

			return manifest;
		}

		// Main manifest is broken but bak is good: make main a copy of bak, keep bak as is.
		// Returns whether manifest.dat on disk is now good.
		private static bool RestoreMainFromBackup(string path, string bakPath)
		{
			try
			{
				var tmp = SaveFileStore.TmpPath(path);
				SaveFileStore.WriteDurable(tmp, File.ReadAllBytes(bakPath));
				if (File.Exists(path))
					File.Delete(path);
				File.Move(tmp, path);
				return true;
			}
			catch (System.Exception)
			{
				// Still loaded from bak in memory; the next commit removes the broken main first.
				return !File.Exists(path);
			}
		}

		// Remove data files from crashed / failed commits and older generations.
		private static void DeleteOrphans()
		{
			foreach (var (key, generation, path) in SaveFileStore.ListDataFiles())
			{
				if (!IsReferenced(key, generation))
					SaveFileStore.TryDelete(path);
			}

			SaveFileStore.TryDelete(SaveFileStore.TmpPath(SaveFileStore.ManifestPath));
		}

		private static void DeleteUnreferenced(SaveManifest dropped)
		{
			if (dropped == null)
				return;

			foreach (var kv in dropped.Entries)
			{
				if (!IsReferenced(kv.Key, kv.Value.Generation))
					SaveFileStore.TryDelete(SaveFileStore.DataPath(kv.Key, kv.Value.Generation));
			}
		}

		private static bool IsReferenced(string key, long generation)
		{
			return (_current != null && _current.References(key, generation)) ||
			       (_backup != null && _backup.References(key, generation));
		}
	}
}
