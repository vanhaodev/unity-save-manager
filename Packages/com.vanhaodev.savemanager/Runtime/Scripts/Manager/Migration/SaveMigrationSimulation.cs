using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace vanhaodev.savemanager
{
	/// <summary>Result of <see cref="SaveMigrationSimulator.Run"/>: what a migration would do to one save.</summary>
	public sealed class SaveMigrationSimulation
	{
		public enum KeyChangeKind { Added, Removed, Changed }

		/// <summary>One step that ran (Error null = it worked).</summary>
		public sealed class StepReport
		{
			public int From { get; internal set; }
			public int To { get; internal set; }
			public string Owner { get; internal set; }
			public double Milliseconds { get; internal set; }
			public Exception Error { get; internal set; }
		}

		/// <summary>One key the migration would write or delete. Sizes are file sizes in bytes.</summary>
		public sealed class KeyChange
		{
			public string Key { get; internal set; }
			public KeyChangeKind Kind { get; internal set; }
			public int SizeBefore { get; internal set; }
			public int SizeAfter { get; internal set; }
		}

		internal readonly List<StepReport> StepList = new();
		internal readonly List<KeyChange> ChangeList = new();

		// Migrated save ready to write (file name -> bytes), built while the game's keys were in use.
		internal Dictionary<string, byte[]> ExportFiles;

		public string SourceFolder { get; }
		public int SaveVersion { get; internal set; }
		public int GameVersion { get; }
		public SaveMigrationResult Result { get; internal set; }

		/// <summary>Why it failed (bad folder, chain, step, Verify); null otherwise.</summary>
		public Exception Error { get; internal set; }

		/// <summary>True if the setup has a Verify check and it passed.</summary>
		public bool VerifyPassed { get; internal set; }

		public IReadOnlyList<StepReport> Steps => StepList;
		public IReadOnlyList<KeyChange> Changes => ChangeList;

		internal SaveMigrationSimulation(string sourceFolder, int gameVersion)
		{
			SourceFolder = sourceFolder;
			GameVersion = gameVersion;
		}

		/// <summary>
		/// Write the migrated save into an empty folder laid out like SaveData (open it with
		/// Explore, or copy it into SaveData to play it). Only after Result == Migrated.
		/// </summary>
		public void ExportTo(string folder)
		{
			if (ExportFiles == null)
				throw new InvalidOperationException("Nothing to export: the simulation did not migrate the save.");
			if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length > 0)
				throw new IOException($"Export folder '{folder}' is not empty.");

			Directory.CreateDirectory(folder);
			foreach (var kv in ExportFiles)
				File.WriteAllBytes(Path.Combine(folder, kv.Key), kv.Value);
		}

		public override string ToString()
		{
			var sb = new StringBuilder();
			sb.Append($"{Path.GetFileName(SourceFolder)}: save {SaveVersion} -> game {GameVersion}: {Result}");
			if (Error != null && StepList.TrueForAll(s => s.Error == null))
				sb.Append($" ({Error.Message})");

			foreach (var s in StepList)
				sb.Append($"\n  {(s.Error == null ? "OK  " : "FAIL")} {s.From} -> {s.To} ({s.Owner}) {s.Milliseconds:0.#} ms{(s.Error == null ? "" : ": " + s.Error.Message)}");
			foreach (var c in ChangeList)
				sb.Append($"\n  {c.Kind,-7} {c.Key} ({c.SizeBefore} -> {c.SizeAfter} bytes)");
			if (VerifyPassed)
				sb.Append("\n  Verify passed");
			return sb.ToString();
		}
	}
}
