using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Try a game's migrations on a copy of a save (snapshot, backup, a save pulled from a
	/// device) without touching the game's own save: everything runs in memory, nothing is
	/// written. Uses the same steps, order and Verify as the real migration.
	/// Not for use while the game is saving (it switches the SetKey secrets for the duration).
	/// <code>
	/// var sim = SaveMigrationSimulator.Run(new GameSaveSetup(), "SaveSnapshots/schema-3");
	/// Assert.AreEqual(SaveMigrationResult.Migrated, sim.Result, sim.ToString());
	/// </code>
	/// </summary>
	public static class SaveMigrationSimulator
	{
		/// <summary>Simulate migrating the save in <paramref name="saveFolder"/> (a folder holding manifest.dat).</summary>
		public static SaveMigrationSimulation Run(ISaveSetup setup, string saveFolder)
		{
			if (setup == null)
				throw new ArgumentNullException(nameof(setup));

			var config = new SaveConfig();
			setup.Configure(config);
			var sim = new SaveMigrationSimulation(saveFolder, config.SchemaVersion ?? Save.SchemaVersion);

			// IoLock: a background save of the running game must not sign with the swapped secrets.
			lock (SaveFileStore.IoLock)
			using (SaveEncryption.UseTemporarily(config.Secret, config.PreviousSecrets))
			{
				try
				{
					Simulate(sim, config);
				}
				catch (Exception e)
				{
					sim.Result = SaveMigrationResult.Failed;
					sim.Error = e;
				}
			}

			return sim;
		}

		/// <summary>
		/// Copy the game's current save (consistent, as last saved) into <paramref name="folder"/>,
		/// replacing it. Play Mode / running game only, after Save.Setup. Returns its schema version,
		/// or null if there is nothing saved yet.
		/// </summary>
		public static int? CaptureCurrentSave(string folder) => Save.CaptureSnapshot(folder);

		private static void Simulate(SaveMigrationSimulation sim, SaveConfig config)
		{
			var files = SaveBackupStore.ReadFolderVerified(sim.SourceFolder, out var saveVersion);
			sim.SaveVersion = saveVersion;
			var game = sim.GameVersion;

			if (files.Count == 0 || saveVersion == game)
			{
				sim.Result = SaveMigrationResult.UpToDate;
				return;
			}

			if (saveVersion > game)
			{
				sim.Result = SaveMigrationResult.NewerSave;
				return;
			}

			var byFrom = SaveMigrationChain.Validate(SaveMigrationChain.Collect(config.MigrationSets), game);
			var path = SaveMigrationChain.PathFrom(byFrom, saveVersion, game);
			if (path == null)
			{
				sim.Result = SaveMigrationResult.TooOld;
				sim.Error = new InvalidOperationException($"No migration starts at version {saveVersion} (oldest supported {SaveMigrationChain.OldestSupported(byFrom, game)}).");
				return;
			}

			var context = new SaveMigrationContext(saveVersion, game, files.Keys, key => files.TryGetValue(key, out var b) ? b : null);
			var run = new SaveMigrationRun(context, path, config.VerifyCheck);
			if (!RunSteps(sim, run))
				return;

			sim.VerifyPassed = config.VerifyCheck != null;
			var changes = context.BuildFileChanges();
			RecordChanges(sim, files, changes);
			sim.ExportFiles = BuildExport(files, changes, game);
			sim.Result = SaveMigrationResult.Migrated;
		}

		// Step by step, so each one is timed and the failing one is named. False on failure.
		private static bool RunSteps(SaveMigrationSimulation sim, SaveMigrationRun run)
		{
			var steps = run.Execute().GetEnumerator();
			var timer = new Stopwatch();
			while (true)
			{
				timer.Restart();
				try
				{
					if (!steps.MoveNext())
						return true; // last MoveNext ran Verify
				}
				catch (Exception e)
				{
					sim.Result = SaveMigrationResult.Failed;
					sim.Error = e;
					if (run.Current != null)
						sim.StepList.Add(Report(run.Current, timer, e));
					return false;
				}

				sim.StepList.Add(Report(run.Current, timer, null));
			}
		}

		private static SaveMigrationSimulation.StepReport Report(SaveMigrationStep step, Stopwatch timer, Exception error)
		{
			return new SaveMigrationSimulation.StepReport
			{
				From = step.From, To = step.To, Owner = step.Owner,
				Milliseconds = timer.Elapsed.TotalMilliseconds, Error = error
			};
		}

		private static void RecordChanges(SaveMigrationSimulation sim, Dictionary<string, byte[]> before, Dictionary<string, byte[]> changes)
		{
			foreach (var kv in changes)
			{
				var had = before.TryGetValue(kv.Key, out var old);
				sim.ChangeList.Add(new SaveMigrationSimulation.KeyChange
				{
					Key = kv.Key,
					Kind = kv.Value == null ? SaveMigrationSimulation.KeyChangeKind.Removed
						: had ? SaveMigrationSimulation.KeyChangeKind.Changed : SaveMigrationSimulation.KeyChangeKind.Added,
					SizeBefore = had ? old.Length : 0,
					SizeAfter = kv.Value?.Length ?? 0
				});
			}

			sim.ChangeList.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
		}

		// The migrated save as files (generation 1), signed with the game's key while it is in use.
		private static Dictionary<string, byte[]> BuildExport(Dictionary<string, byte[]> before, Dictionary<string, byte[]> changes, int game)
		{
			var merged = new Dictionary<string, byte[]>(before);
			foreach (var kv in changes)
			{
				if (kv.Value == null) merged.Remove(kv.Key);
				else merged[kv.Key] = kv.Value;
			}

			var result = new Dictionary<string, byte[]>();
			var entries = new Dictionary<string, SaveManifest.Entry>();
			foreach (var kv in merged)
			{
				result[Path.GetFileName(SaveFileStore.DataPath(kv.Key, 1))] = kv.Value;
				entries[kv.Key] = new SaveManifest.Entry(1, SaveFileStore.Hash(kv.Value));
			}

			result[SaveFileStore.ManifestFileName] = new SaveManifest(1, entries, game).Serialize();
			return result;
		}
	}
}
