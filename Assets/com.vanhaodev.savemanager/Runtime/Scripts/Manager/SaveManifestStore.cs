using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
			var canCleanUp = true;
			SaveEncryption.KeySet loadedWith = null;

			var path = SaveFileStore.ManifestPath;
			var bakPath = SaveFileStore.BakPath(path);

			var main = TryLoadVerified(path, problems, out var mainKeys);
			if (main != null)
			{
				_current = main;
				loadedWith = mainKeys;
				_backup = TryParse(bakPath);
				_mainFileIsGood = true;
			}
			else
			{
				var backup = TryLoadVerified(bakPath, problems, out var backupKeys);
				if (backup != null)
				{
					if (File.Exists(path))
						problems.Add("Save data failed integrity check, restored previous save.");

					_current = backup;
					loadedWith = backupKeys;
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

					// No signature verifies at all (wrong / missing SetKey secret, or edited):
					// never delete — move everything aside so it can be recovered, start empty.
					if (mainParsed == null && bakParsed == null && (File.Exists(path) || File.Exists(bakPath)))
						canCleanUp = Quarantine(problems);

					_current = mainParsed ?? bakParsed ?? SaveManifest.Empty;
					_backup = mainParsed != null ? bakParsed : null;
					_mainFileIsGood = mainParsed != null || !File.Exists(path);
				}
			}

			// Unverifiable files that could not be moved aside must not be deleted either.
			if (canCleanUp)
				DeleteOrphans();
			_loaded = true;

			// Loaded with a previous SetKey secret: move it to the current one right away,
			// so later versions no longer need the old secret.
			if (loadedWith != null && loadedWith != SaveEncryption.CurrentKeys)
				MigrateToCurrentKey(loadedWith);
		}

		/// <summary>
		/// A commit split into steps: data files first (one per WriteNext, old save untouched),
		/// then FinishCommit swaps the manifest (the commit point). Steps may run on different
		/// frames; a crash in between leaves the previous complete save.
		/// </summary>
		internal sealed class CommitJob
		{
			internal long Generation;
			internal Dictionary<string, SaveManifest.Entry> Entries;
			internal Queue<KeyValuePair<string, byte[]>> Remaining;
			internal readonly List<string> Written = new();
		}

		/// <summary>Start a commit. changes: key -> bytes, or key -> null to remove the key.</summary>
		internal static CommitJob BeginCommit(IReadOnlyDictionary<string, byte[]> changes)
		{
			if (!_loaded)
				throw new System.InvalidOperationException("Save manifest is not loaded.");

			return new CommitJob
			{
				Generation = _current.Generation + 1,
				Entries = _current.CopyEntries(),
				Remaining = new Queue<KeyValuePair<string, byte[]>>(changes)
			};
		}

		/// <summary>Write the next data file of the job. False when nothing is left.</summary>
		internal static bool WriteNext(CommitJob job)
		{
			if (job.Remaining.Count == 0)
				return false;

			var kv = job.Remaining.Dequeue();
			if (kv.Value == null)
			{
				job.Entries.Remove(kv.Key);
				return true;
			}

			var dataPath = SaveFileStore.DataPath(kv.Key, job.Generation);
			SaveFileStore.WriteDurable(dataPath, kv.Value);
			job.Written.Add(dataPath);
			job.Entries[kv.Key] = new SaveManifest.Entry(job.Generation, SaveFileStore.Hash(kv.Value));
			return true;
		}

		/// <summary>Swap the manifest: from here on the new save is the current one.</summary>
		internal static void FinishCommit(CommitJob job)
		{
			// Commits are serialized; another commit in between would make this one stale.
			if (job.Generation != _current.Generation + 1)
				throw new System.InvalidOperationException("Save commit is out of date.");

			var next = new SaveManifest(job.Generation, job.Entries);

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

		/// <summary>Give up a job: remove its new files, the previous save stays current.</summary>
		internal static void AbortCommit(CommitJob job)
		{
			foreach (var file in job.Written)
				SaveFileStore.TryDelete(file);
		}

		/// <summary>
		/// Write one complete save in one go. On failure nothing changes on disk
		/// (new files are removed) and the exception propagates.
		/// </summary>
		internal static void Commit(IReadOnlyDictionary<string, byte[]> changes)
		{
			var job = BeginCommit(changes);
			try
			{
				while (WriteNext(job)) { }
				FinishCommit(job);
			}
			catch
			{
				AbortCommit(job);
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

		private static SaveManifest TryParse(string path) => TryParse(path, out _);

		private static SaveManifest TryParse(string path, out SaveEncryption.KeySet signedWith)
		{
			signedWith = null;
			try
			{
				return File.Exists(path) ? SaveManifest.Deserialize(File.ReadAllBytes(path), out signedWith) : null;
			}
			catch (System.Exception)
			{
				return null;
			}
		}

		// Manifest is valid only if it parses AND every file it points to matches its fingerprint.
		private static SaveManifest TryLoadVerified(string path, List<string> problems, out SaveEncryption.KeySet signedWith)
		{
			signedWith = null;
			if (!File.Exists(path))
				return null;

			var manifest = TryParse(path, out signedWith);
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

		private static void MigrateToCurrentKey(SaveEncryption.KeySet from)
		{
			try
			{
				Commit(SaveKeyMigration.BuildChanges(_current, from));
				Debug.Log("[SaveManager] Save moved from a previous SetKey secret to the current one.");
			}
			catch (System.Exception e)
			{
				// Save stays readable with the previous secret; retried on next launch.
				Debug.LogWarning($"[SaveManager] Could not move save to the current secret, will retry next launch.\n{e}");
			}
		}

		// Move manifests + data files into quarantine/{timestamp}/ (the folder is never read again).
		private static bool Quarantine(List<string> problems)
		{
			var target = Path.Combine(SaveFileStore.Folder, "quarantine", System.DateTime.Now.ToString("yyyyMMdd-HHmmss"));

			try
			{
				Directory.CreateDirectory(target);
				foreach (var file in Directory.GetFiles(SaveFileStore.Folder))
					File.Move(file, Path.Combine(target, Path.GetFileName(file)));

				problems.Add($"Save could not be verified (different SetKey secret, or files edited). Moved to {target}, starting with an empty save.");
				return true;
			}
			catch (System.Exception e)
			{
				problems.Add($"Save could not be verified and could not be moved to quarantine: {e.Message}");
				return false;
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
