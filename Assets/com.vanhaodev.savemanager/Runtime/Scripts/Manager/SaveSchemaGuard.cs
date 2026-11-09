using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Game data version (schema) of the save, and the write block used when the save
	/// must not be changed by this game (e.g. made by a newer game version).
	/// </summary>
	public static partial class Save
	{
		private static int _schemaVersion;
		private static volatile bool _writeBlocked;
		private static string _writeBlockReason;

		// True once the save is loaded AND its version checked (block / migration decided).
		// Save calls wait on this, not on "loaded", so none slips in between.
		private static volatile bool _schemaChecked;
		private static readonly object LoadLock = new();

		/// <summary>
		/// Version of the game's save data. Raise it by 1 whenever saved data changes shape.
		/// Not the app version: a patch that does not change saved data keeps the same number.
		/// Set once at game start, before any Save call (like SetKey). Default 0.
		/// </summary>
		public static int SchemaVersion
		{
			get => _schemaVersion;
			set
			{
				if (value < 0)
					throw new ArgumentOutOfRangeException(nameof(value), "SchemaVersion must not be negative.");
				if (SaveManifestStore.IsLoaded && value != _schemaVersion)
					throw new InvalidOperationException("Set Save.SchemaVersion at game start, before any Save call.");
				_schemaVersion = value;
			}
		}

		/// <summary>
		/// The save was made by a newer game version (its schema is higher than SchemaVersion).
		/// Saving is blocked so this older game cannot overwrite data it does not understand;
		/// loading still works (keys it cannot read return Corrupt).
		/// Call e.AllowWrite() to let the game save anyway.<br/>
		/// Raised right away (not on the next frame), during the first Save call.
		/// </summary>
		public static event Action<SaveVersionEvent> NewerSaveDetected;

		/// <summary>
		/// An older build (Save Manager 1.1.0 or older) saved over this save since a newer one did,
		/// so some keys may be in an older layout than the save version says. Saving is not blocked.
		/// Raised right away, during the first Save call.
		/// </summary>
		public static event Action<SaveVersionEvent> SaveTouchedByOlderVersion;

		/// <summary>
		/// True while saving is blocked: save made by a newer game (NewerSaveDetected), too old
		/// to migrate (SaveTooOld) or migration failed (MigrationFailed). Loading still works.
		/// </summary>
		public static bool IsWriteBlocked => _writeBlocked;

		/// <summary>Schema version of the save on disk (loads the save if needed, does not migrate).</summary>
		public static int SaveSchemaVersion
		{
			get
			{
				LoadIfNeeded();
				return SaveManifestStore.SchemaVersion;
			}
		}

		/// <summary>
		/// Allow saving although it is blocked (your call: keys this game writes may lose data
		/// a newer version added, or mix layouts). The save is backed up first; if that fails,
		/// saving stays blocked and this throws. Does nothing when saving is not blocked.
		/// </summary>
		public static void AllowWrite()
		{
			if (!_writeBlocked)
				return;

			lock (SaveFileStore.IoLock)
			{
				SaveBackupStore.Create(_backupsToKeep);
				SaveManifestStore.MoveToCurrentKeyIfNeeded();
			}

			_writeBlocked = false;
			Debug.LogWarning("[SaveManager] Saving allowed by the game although the save was blocked (backup made).");
		}

		private static void ResetSchema()
		{
			_schemaVersion = 0;
			_writeBlocked = false;
			_writeBlockReason = null;
			_schemaChecked = false;
			NewerSaveDetected = null;
			SaveTouchedByOlderVersion = null;
		}

		// Right after the save is loaded (main thread, outside IoLock). Only compares numbers
		// when the save is up to date.
		private static void CheckSchema()
		{
			var saveVersion = SaveManifestStore.SchemaVersion;
			var touched = SaveManifestStore.TouchedByOlderBuild;
			var newer = saveVersion > _schemaVersion;

			// All state first: a hook handler below may already call Save, and must see the
			// write block / pending migration.
			if (newer)
			{
				_migrationResult = SaveMigrationResult.NewerSave;
				BlockWrites($"the save was made by a newer game version (save schema {saveVersion} > game {_schemaVersion})");
			}
			else
			{
				lock (SaveFileStore.IoLock)
				{
					SaveManifestStore.MoveToCurrentKeyIfNeeded();
				}

				// Run by Migrate / MigrateAsync, or by the first Save call if the game did not.
				_needsMigration = saveVersion < _schemaVersion;
			}

			_schemaChecked = true;

			if (touched)
			{
				Debug.LogWarning($"[SaveManager] An older game build saved over this save (save schema {saveVersion}).");
				InvokeEach(SaveTouchedByOlderVersion, h => h(new SaveVersionEvent(saveVersion, _schemaVersion)));
			}

			// Inline so a handler's AllowWrite() takes effect before the caller's first write.
			if (newer)
				InvokeEach(NewerSaveDetected, h => h(new SaveVersionEvent(saveVersion, _schemaVersion)));
		}

		private static void BlockWrites(string reason)
		{
			_writeBlocked = true;
			_writeBlockReason = reason;
			Debug.LogWarning($"[SaveManager] Saving is blocked: {reason}.");
		}

		private static void ThrowIfWriteBlocked()
		{
			if (_writeBlocked)
				throw WriteBlockedError();
		}

		private static InvalidOperationException WriteBlockedError()
		{
			return new InvalidOperationException($"Saving is blocked: {_writeBlockReason}. Call Save.AllowWrite() to save anyway.");
		}

		// SetAsync never throws for this: the Task fails and SaveFailed is raised, like a disk error.
		private static Task BlockedWriteTask(string key)
		{
			var error = WriteBlockedError();
			RaiseSaveFailed(new List<string> { key }, error);
			return Task.FromException(error);
		}
	}
}
