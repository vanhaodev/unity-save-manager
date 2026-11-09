using System;

namespace vanhaodev.savemanager
{
	/// <summary>Outcome of Save.Migrate / Save.MigrateAsync.</summary>
	public enum SaveMigrationResult
	{
		/// <summary>The save already had the game's version (or is new). Nothing was done.</summary>
		UpToDate,

		/// <summary>The save was migrated and saved.</summary>
		Migrated,

		/// <summary>The save was made by a newer game version (see Save.NewerSaveDetected). Saving is blocked.</summary>
		NewerSave,

		/// <summary>No migration starts at the save's version (see Save.SaveTooOld). Saving is blocked.</summary>
		TooOld,

		/// <summary>A step or Verify threw (see Save.MigrationFailed). The save is unchanged, saving is blocked.</summary>
		Failed
	}

	/// <summary>Where Save.MigrateAsync runs the migration steps.</summary>
	public enum SaveMigrationThread
	{
		/// <summary>On the main thread, one step per frame. Steps may use Unity APIs. Default.</summary>
		MainThread,

		/// <summary>On a background thread: smoother, but steps must not use Unity APIs. WebGL uses MainThread.</summary>
		Background
	}

	/// <summary>Details passed to Save.MigrationFailed.</summary>
	public sealed class SaveMigrationFailure
	{
		/// <summary>Schema version of the save (unchanged on disk).</summary>
		public int SaveVersion { get; }

		/// <summary>Save.SchemaVersion of the game.</summary>
		public int GameVersion { get; }

		/// <summary>Start version of the step that failed; null if the setup or Verify failed.</summary>
		public int? StepFrom { get; }

		/// <summary>End version of the step that failed; null if the setup or Verify failed.</summary>
		public int? StepTo { get; }

		public Exception Error { get; }

		internal SaveMigrationFailure(int saveVersion, int gameVersion, int? stepFrom, int? stepTo, Exception error)
		{
			SaveVersion = saveVersion;
			GameVersion = gameVersion;
			StepFrom = stepFrom;
			StepTo = stepTo;
			Error = error;
		}

		/// <summary>Let the game save anyway (same as Save.AllowWrite()).</summary>
		public void AllowWrite() => Save.AllowWrite();

		public override string ToString()
		{
			var where = StepFrom.HasValue ? $"step {StepFrom} -> {StepTo}" : "setup / Verify";
			return $"Save migration {SaveVersion} -> {GameVersion} failed at {where}: {Error?.Message}";
		}
	}
}
