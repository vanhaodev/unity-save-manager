using System;
using System.Collections.Generic;

namespace vanhaodev.savemanager
{
	/// <summary>One migration step: turns a save of version From into version To.</summary>
	internal sealed class SaveMigrationStep
	{
		internal readonly int From;
		internal readonly int To;
		internal readonly Action<SaveMigrationContext> Run;
		internal readonly string Owner;

		internal SaveMigrationStep(int from, int to, Action<SaveMigrationContext> run, string owner)
		{
			From = from;
			To = to;
			Run = run;
			Owner = owner;
		}

		public override string ToString() => $"{From} -> {To} ({Owner})";
	}

	/// <summary>Collects the steps of an <see cref="ISaveMigrationSet"/>.</summary>
	public sealed class SaveMigrations
	{
		internal readonly List<SaveMigrationStep> Steps = new();
		private readonly string _owner;

		internal SaveMigrations(string owner)
		{
			_owner = owner;
		}

		/// <summary>Step from version <paramref name="from"/> to <paramref name="from"/> + 1.</summary>
		public SaveMigrations Add(int from, Action<SaveMigrationContext> step)
		{
			return Add(from, from + 1, step);
		}

		/// <summary>
		/// Step that jumps from <paramref name="from"/> straight to <paramref name="to"/>,
		/// e.g. to replace many old small steps with one.
		/// </summary>
		public SaveMigrations Add(int from, int to, Action<SaveMigrationContext> step)
		{
			if (step == null)
				throw new ArgumentNullException(nameof(step));
			if (from < 0)
				throw new ArgumentOutOfRangeException(nameof(from), "Migration version must not be negative.");
			if (to <= from)
				throw new ArgumentOutOfRangeException(nameof(to), $"Migration must go to a higher version ({from} -> {to}).");

			Steps.Add(new SaveMigrationStep(from, to, step, _owner));
			return this;
		}
	}
}
