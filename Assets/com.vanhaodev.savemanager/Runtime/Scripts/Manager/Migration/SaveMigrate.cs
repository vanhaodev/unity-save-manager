using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Save migration: brings a save of an older Save.SchemaVersion up to the game's version by
	/// running the registered steps in order, in memory, then saving once. Any failure leaves
	/// the save on disk untouched. An up-to-date save costs one number comparison.
	/// </summary>
	public static partial class Save
	{
		private static readonly List<Func<ISaveMigrationSet>> MigrationSets = new();
		private static Action<SaveMigrationContext> _verify;
		private static volatile bool _needsMigration;
		private static volatile bool _migrationRunning;
		private static SaveMigrationResult _migrationResult;

		// Bumped on every reset: an async run from before (Editor, play mode left without a
		// domain reload) must not commit into or block the new session.
		private static int _session;

		/// <summary>
		/// Apply all save settings at game start, before any Save call. In the Editor the
		/// migration chain is checked here too, so a missing or duplicate step fails at Play.
		/// </summary>
		public static void Setup(ISaveSetup setup)
		{
			if (setup == null)
				throw new ArgumentNullException(nameof(setup));
			if (SaveManifestStore.IsLoaded)
				throw new InvalidOperationException("Call Save.Setup at game start, before any Save call.");

			var config = new SaveConfig();
			setup.Configure(config);

			if (config.Secret != null)
				SaveEncryption.SetKey(config.Secret, config.PreviousSecrets ?? Array.Empty<string>());
			if (config.SchemaVersion.HasValue)
				SchemaVersion = config.SchemaVersion.Value;
			if (config.BackupsToKeep.HasValue)
				BackupsToKeep = config.BackupsToKeep.Value;
			MigrationSets.Clear();
			MigrationSets.AddRange(config.MigrationSets);
			_verify = config.VerifyCheck;

			if (Application.isEditor)
				SaveMigrationChain.Validate(SaveMigrationChain.Collect(MigrationSets), _schemaVersion);
		}

		/// <summary>Migrate the save now if needed; the game waits until done (loading screen).</summary>
		public static SaveMigrationResult Migrate()
		{
			ThrowIfMigrating();
			LoadIfNeeded();
			if (!_needsMigration)
				return _migrationResult;

			var run = PrepareMigration(out var early);
			if (run == null)
				return early;

			_migrationRunning = true;
			try
			{
				RaiseMigrationStarted(run);
				foreach (var progress in run.Execute())
					RaiseStepCompleted(run, progress, null, false);
				return FinishMigration(run);
			}
			catch (Exception e)
			{
				return FailMigration(run.Context.SaveVersion, run.Current, e);
			}
			finally
			{
				_migrationRunning = false;
			}
		}

		/// <summary>
		/// Migrate without freezing the game. Completes at once when the save is up to date.
		/// Other Save calls throw until it completes. progress (0..1) is called on the main thread.
		/// </summary>
		public static Task<SaveMigrationResult> MigrateAsync(Action<float> progress = null, SaveMigrationThread thread = SaveMigrationThread.MainThread)
		{
			ThrowIfMigrating();
			LoadIfNeeded();
			if (!_needsMigration)
				return Task.FromResult(_migrationResult);

			var run = PrepareMigration(out var early);
			if (run == null)
				return Task.FromResult(early);

			if (SaveQueue.RunOnMainThread)
				thread = SaveMigrationThread.MainThread; // WebGL: no worker threads

			_migrationRunning = true; // set now, so Save calls right after this one are rejected
			return RunMigrationAsync(run, progress, thread);
		}

		private static async Task<SaveMigrationResult> RunMigrationAsync(SaveMigrationRun run, Action<float> progress, SaveMigrationThread thread)
		{
			try
			{
				RaiseMigrationStarted(run);
				if (thread == SaveMigrationThread.Background)
				{
					await Task.Run(() =>
					{
						foreach (var p in run.Execute())
							RaiseStepCompleted(run, p, progress, true);
					});
				}
				else
				{
					foreach (var p in run.Execute())
					{
						RaiseStepCompleted(run, p, progress, false);
						await NextFrame();
					}
				}

				if (run.Session != _session)
					return SaveMigrationResult.Failed; // stale: the save session was reset meanwhile
				return FinishMigration(run);
			}
			catch (Exception e)
			{
				if (run.Session != _session)
					return SaveMigrationResult.Failed;
				return FailMigration(run.Context.SaveVersion, run.Current, e);
			}
			finally
			{
				if (run.Session == _session)
					_migrationRunning = false;
			}
		}

		// Any public Save call: the game forgot Migrate, so run it now (sync).
		private static void EnsureMigrated()
		{
			if (!_needsMigration)
				return;

			ThrowIfMigrating();
			Debug.LogWarning("[SaveManager] Save.Migrate() / MigrateAsync() was not called at game start: migrating now, the game waits until done.");
			Migrate();
		}

		private static void ThrowIfMigrating()
		{
			if (_migrationRunning)
				throw new InvalidOperationException("The save is being migrated: await Save.MigrateAsync() before other Save calls (migration steps use ctx, not Save).");
		}

		// Null when nothing has to run (too old / bad setup); early then holds the result.
		private static SaveMigrationRun PrepareMigration(out SaveMigrationResult early)
		{
			early = default;
			var saveVersion = SaveManifestStore.SchemaVersion;

			Dictionary<int, SaveMigrationStep> byFrom;
			try
			{
				byFrom = SaveMigrationChain.Validate(SaveMigrationChain.Collect(MigrationSets), _schemaVersion);
			}
			catch (Exception e)
			{
				early = FailMigration(saveVersion, null, e);
				return null;
			}

			var path = SaveMigrationChain.PathFrom(byFrom, saveVersion, _schemaVersion);
			if (path == null)
			{
				var oldest = SaveMigrationChain.OldestSupported(byFrom, _schemaVersion);
				_needsMigration = false;
				_migrationResult = early = SaveMigrationResult.TooOld;
				BlockWrites($"the save is too old to migrate (save schema {saveVersion}, oldest supported {oldest})");
				InvokeEach(SaveTooOld, h => h(new SaveVersionEvent(saveVersion, _schemaVersion, oldest)));
				return null;
			}

			List<string> keys;
			lock (SaveFileStore.IoLock)
			{
				keys = SaveManifestStore.KeysSnapshot();
			}

			var context = new SaveMigrationContext(saveVersion, _schemaVersion, keys, ReadSavedFile);
			return new SaveMigrationRun(context, path, _verify) { Session = _session };
		}

		private static byte[] ReadSavedFile(string key)
		{
			lock (SaveFileStore.IoLock)
			{
				return SaveManifestStore.ReadVerified(key);
			}
		}

		// One commit for the whole migration: either every change and the new version, or nothing.
		private static SaveMigrationResult FinishMigration(SaveMigrationRun run)
		{
			var files = run.Context.BuildFileChanges();
			lock (SaveFileStore.IoLock)
			{
				// Backup first: a migration with a logic bug can be undone. No backup, no migration.
				SaveBackupStore.Create(_backupsToKeep);
				SaveManifestStore.Commit(files, _schemaVersion);
			}

			_needsMigration = false;
			_migrationResult = SaveMigrationResult.Migrated;

			var from = run.Context.SaveVersion;
			var to = run.Context.GameVersion;
			Debug.Log($"[SaveManager] Save migrated from schema {from} to {to} ({run.Path.Count} steps, {files.Count} keys changed).");
			Dispatch(() => InvokeEach(MigrationCompleted, h => h(from, to)));
			return SaveMigrationResult.Migrated;
		}

		private static SaveMigrationResult FailMigration(int saveVersion, SaveMigrationStep step, Exception error)
		{
			_needsMigration = false;
			_migrationResult = SaveMigrationResult.Failed;

			var failure = new SaveMigrationFailure(saveVersion, _schemaVersion, step?.From, step?.To, error);
			Debug.LogError($"[SaveManager] {failure} (the save is unchanged).\n{error}");
			BlockWrites("migrating the save failed, it is unchanged on disk");
			Dispatch(() => InvokeEach(MigrationFailed, h => h(failure)));
			return SaveMigrationResult.Failed;
		}

		private static void ResetMigration()
		{
			_session++;
			MigrationSets.Clear();
			_verify = null;
			_needsMigration = false;
			_migrationRunning = false;
			_migrationResult = SaveMigrationResult.UpToDate;
			MigrationStarted = null;
			MigrationStepCompleted = null;
			MigrationCompleted = null;
			MigrationFailed = null;
			SaveTooOld = null;
		}
	}
}
