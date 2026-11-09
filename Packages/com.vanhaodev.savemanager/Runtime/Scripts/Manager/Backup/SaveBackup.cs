using System;
using System.Collections.Generic;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Backups: the library copies the whole save to SaveData/backups/ right before a migration
	/// is saved, before AllowWrite() lets the game write over a blocked save, and before
	/// RestoreBackup / StartFresh replace it. A bug in a migration step can then be undone.
	/// </summary>
	public static partial class Save
	{
		private const int DefaultBackupsToKeep = 3;
		private static int _backupsToKeep = DefaultBackupsToKeep;

		/// <summary>How many backups are kept (newest first, older ones are deleted). Default 3, at least 1.</summary>
		public static int BackupsToKeep
		{
			get => _backupsToKeep;
			set
			{
				if (value < 1)
					throw new ArgumentOutOfRangeException(nameof(value), "Keep at least 1 backup.");
				_backupsToKeep = value;
			}
		}

		/// <summary>Backups of this save, newest first.</summary>
		public static IReadOnlyList<SaveBackupInfo> ListBackups()
		{
			LoadIfNeeded();
			lock (SaveFileStore.IoLock)
			{
				return SaveBackupStore.List();
			}
		}

		/// <summary>
		/// Replace the save with a backup (e.g. after shipping a fixed migration). The backup is
		/// checked first (throws InvalidDataException if broken, nothing changes); the current
		/// save is backed up too. The restored save is then checked like a fresh load: older
		/// than the game → migrated by the next Migrate / Save call, newer → blocked.
		/// </summary>
		public static void RestoreBackup(SaveBackupInfo backup)
		{
			if (backup == null)
				throw new ArgumentNullException(nameof(backup));

			PrepareReplace();
			int schema;
			lock (SaveFileStore.IoLock)
			{
				var files = SaveBackupStore.ReadVerified(backup, out schema);
				SaveBackupStore.Create(_backupsToKeep, backup.Path); // never rotate away the backup being restored

				var changes = new Dictionary<string, byte[]>(files);
				foreach (var key in SaveManifestStore.KeysSnapshot())
				{
					if (!changes.ContainsKey(key))
						changes[key] = null;
				}

				SaveManifestStore.Commit(changes, schema);
				SaveManifestStore.ClearPendingKeyMove(); // restored files were moved to the current key
			}

			Debug.Log($"[SaveManager] Restored {backup}.");

			// Decide again (block / migrate) for the restored version, raising the hooks again.
			ClearVersionState();
			_schemaChecked = false;
			LoadIfNeeded();
		}

		/// <summary>
		/// Start over with an empty save at the game's version (e.g. on SaveTooOld). The current
		/// save is backed up first, never just deleted. Unblocks saving.
		/// </summary>
		public static void StartFresh()
		{
			PrepareReplace();
			lock (SaveFileStore.IoLock)
			{
				SaveBackupStore.Create(_backupsToKeep);

				var changes = new Dictionary<string, byte[]>();
				foreach (var key in SaveManifestStore.KeysSnapshot())
					changes[key] = null;

				SaveManifestStore.Commit(changes, _schemaVersion);
				SaveManifestStore.ClearPendingKeyMove(); // no data left on the old key
			}

			ClearVersionState();
			Debug.Log("[SaveManager] Started a fresh save (previous one is in backups).");
		}

		// Copy of the save as last committed, replacing folder. Null when the game has not loaded
		// its save yet (loading here, maybe before Save.Setup, could check it with the wrong key
		// and quarantine it) or nothing is saved.
		internal static int? CaptureSnapshot(string folder)
		{
			if (string.IsNullOrEmpty(folder))
				throw new ArgumentException("Snapshot folder must not be empty.", nameof(folder));
			if (!_schemaChecked)
				return null;

			// The folder is replaced: never let it be (or contain, or sit inside) the save folder.
			var target = System.IO.Path.GetFullPath(folder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
			var saves = System.IO.Path.GetFullPath(SaveFileStore.Folder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
			if (target.StartsWith(saves, StringComparison.OrdinalIgnoreCase) || saves.StartsWith(target, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("Snapshot folder must be outside the save folder.", nameof(folder));

			lock (SaveFileStore.IoLock)
			{
				if (SaveManifestStore.KeysSnapshot().Count == 0)
					return null;

				var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(folder));
				if (!string.IsNullOrEmpty(parent))
					System.IO.Directory.CreateDirectory(parent);

				var fresh = folder + ".new";
				if (System.IO.Directory.Exists(fresh))
					System.IO.Directory.Delete(fresh, true);
				SaveBackupStore.CopyCurrentSaveTo(fresh);
				if (System.IO.Directory.Exists(folder))
					System.IO.Directory.Delete(folder, true);
				System.IO.Directory.Move(fresh, folder);
				return SaveManifestStore.SchemaVersion;
			}
		}

		// Pending background writes go into the current save first, so its backup has them.
		private static void PrepareReplace()
		{
			ThrowIfMigrating();
			LoadIfNeeded();
			if (!_writeBlocked)
				SaveQueue.CommitNow(NoItems);
		}

		private static void ClearVersionState()
		{
			_writeBlocked = false;
			_writeBlockReason = null;
			_needsMigration = false;
			_migrationResult = SaveMigrationResult.UpToDate;
		}

		private static void ResetBackup()
		{
			_backupsToKeep = DefaultBackupsToKeep;
		}
	}
}
