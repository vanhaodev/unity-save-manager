using System;
using System.Collections.Generic;
using System.Linq;

namespace vanhaodev.savemanager
{
	/// <summary>Builds and checks the chain of migration steps up to Save.SchemaVersion.</summary>
	internal static class SaveMigrationChain
	{
		/// <summary>Create every migration set and collect its steps.</summary>
		internal static List<SaveMigrationStep> Collect(IEnumerable<Func<ISaveMigrationSet>> sets)
		{
			var steps = new List<SaveMigrationStep>();
			foreach (var create in sets)
			{
				var set = create();
				var m = new SaveMigrations(set.GetType().Name);
				set.Register(m);
				steps.AddRange(m.Steps);
			}

			return steps;
		}

		/// <summary>
		/// Steps by start version. Throws ArgumentException when the chain is wrong: a step past
		/// the game version, two steps from the same version, a gap, or a step never reached.
		/// </summary>
		internal static Dictionary<int, SaveMigrationStep> Validate(List<SaveMigrationStep> steps, int gameVersion)
		{
			var byFrom = new Dictionary<int, SaveMigrationStep>();
			foreach (var step in steps)
			{
				if (step.To > gameVersion)
					throw new ArgumentException($"Migration {step} goes past Save.SchemaVersion {gameVersion}.");
				if (byFrom.TryGetValue(step.From, out var other))
					throw new ArgumentException($"Two migrations start at version {step.From}: {other} and {step}.");
				byFrom[step.From] = step;
			}

			if (byFrom.Count == 0)
				return byFrom;

			// Walk from the oldest step: every version on the way must have a step.
			var reached = new HashSet<int>();
			var version = byFrom.Keys.Min();
			while (version < gameVersion)
			{
				if (!byFrom.TryGetValue(version, out var step))
					throw new ArgumentException($"No migration from version {version}: the steps must reach Save.SchemaVersion {gameVersion} without gaps.");
				reached.Add(step.From);
				version = step.To;
			}

			foreach (var step in steps)
			{
				if (!reached.Contains(step.From))
					throw new ArgumentException($"Migration {step} is never used: a longer step jumps over version {step.From}.");
			}

			return byFrom;
		}

		/// <summary>Steps to run for a save of saveVersion, or null if no step starts there (too old).</summary>
		internal static List<SaveMigrationStep> PathFrom(Dictionary<int, SaveMigrationStep> byFrom, int saveVersion, int gameVersion)
		{
			var path = new List<SaveMigrationStep>();
			var version = saveVersion;
			while (version < gameVersion)
			{
				if (!byFrom.TryGetValue(version, out var step))
					return null;
				path.Add(step);
				version = step.To;
			}

			return path;
		}

		/// <summary>Oldest save version that can still be migrated.</summary>
		internal static int OldestSupported(Dictionary<int, SaveMigrationStep> byFrom, int gameVersion)
		{
			return byFrom.Count == 0 ? gameVersion : byFrom.Keys.Min();
		}
	}
}
