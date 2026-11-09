using System;
using System.Collections.Generic;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// One migration of one save: the steps to run, in order, on an in-memory context, then
	/// Verify. Writing is left to the caller, so the same run serves the live save and Editor
	/// simulations on a copy. Migration set instances live only as long as this object.
	/// </summary>
	internal sealed class SaveMigrationRun
	{
		internal readonly SaveMigrationContext Context;
		internal readonly List<SaveMigrationStep> Path;
		private readonly Action<SaveMigrationContext> _verify;

		/// <summary>Save session the run belongs to; a reset (leaving play mode) makes it stale.</summary>
		internal int Session;

		/// <summary>Step running now, null before the first and during Verify.</summary>
		internal SaveMigrationStep Current { get; private set; }

		internal SaveMigrationRun(SaveMigrationContext context, List<SaveMigrationStep> path, Action<SaveMigrationContext> verify)
		{
			Context = context;
			Path = path;
			_verify = verify;
		}

		/// <summary>
		/// Runs the steps one by one, yielding progress (0..1] after each, then Verify.
		/// The caller decides the pace (all at once, one per frame, or on a worker thread).
		/// An exception stops the run; <see cref="Current"/> tells which step threw.
		/// </summary>
		internal IEnumerable<float> Execute()
		{
			for (int i = 0; i < Path.Count; i++)
			{
				Current = Path[i];
				Context.FromVersion = Current.From;
				Context.ToVersion = Current.To;
				Current.Run(Context);
				yield return (i + 1f) / Path.Count;
			}

			Current = null;
			Context.FromVersion = Context.GameVersion;
			Context.ToVersion = Context.GameVersion;
			_verify?.Invoke(Context);
		}

		/// <summary>Run everything at once (sync).</summary>
		internal void ExecuteAll()
		{
			foreach (var _ in Execute()) { }
		}
	}
}
