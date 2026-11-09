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
				SaveBackupStore.Create(_backupsToKeep);

				var changes = new Dictionary<string, byte[]>(files);
				foreach (var key in SaveManifestStore.KeysSnapshot())
				{
					if (!changes.ContainsKey(key))
						changes[key] = null;
				}

				SaveManifestStore.Commit(changes, schema);
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
			}

			ClearVersionState();
			Debug.Log("[SaveManager] Started a fresh save (previous one is in backups).");
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
