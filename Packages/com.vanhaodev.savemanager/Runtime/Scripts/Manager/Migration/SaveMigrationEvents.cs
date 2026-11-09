using System;
using System.Threading.Tasks;

namespace vanhaodev.savemanager
{
	/// <summary>Migration hooks. Delivered on the main thread like the other save hooks (SaveTooOld right away).</summary>
	public static partial class Save
	{
		/// <summary>(saveVersion, gameVersion): migration started.</summary>
		public static event Action<int, int> MigrationStarted;

		/// <summary>(stepFrom, stepTo, progress 0..1): one step finished (in memory, not saved yet).</summary>
		public static event Action<int, int, float> MigrationStepCompleted;

		/// <summary>(fromVersion, toVersion): the migrated save is on disk.</summary>
		public static event Action<int, int> MigrationCompleted;

		/// <summary>A step, Verify or the setup failed. The save is unchanged and saving is blocked (e.AllowWrite() to save anyway).</summary>
		public static event Action<SaveMigrationFailure> MigrationFailed;

		/// <summary>
		/// No migration starts at the save's version (older than every step). Saving is blocked;
		/// the game decides (start over, show a message...). Raised right away.
		/// </summary>
		public static event Action<SaveVersionEvent> SaveTooOld;

		private static void RaiseMigrationStarted(SaveMigrationRun run)
		{
			var from = run.Context.SaveVersion;
			var to = run.Context.GameVersion;
			Dispatch(() => InvokeEach(MigrationStarted, h => h(from, to)));
		}

		// offThread: called from a worker, so the progress callback must go to the main thread.
		private static void RaiseStepCompleted(SaveMigrationRun run, float progress, Action<float> callback, bool offThread)
		{
			var from = run.Current.From;
			var to = run.Current.To;
			Dispatch(() => InvokeEach(MigrationStepCompleted, h => h(from, to, progress)));

			if (callback == null)
				return;
			if (offThread)
				Dispatch(() => InvokeEach(callback, h => h(progress)));
			else
				InvokeEach(callback, h => h(progress));
		}

		// Resumes on a later frame (immediately outside Unity).
		private static Task NextFrame()
		{
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			Dispatch(() => tcs.TrySetResult(true));
			return tcs.Task;
		}
	}
}
