using System;
using System.Collections.Generic;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// All save settings of a game in one place. The game calls Save.Setup(new MySetup()) once
	/// at start; Editor tools (migration simulation) use the same class.
	/// <code>
	/// public sealed class GameSaveSetup : ISaveSetup
	/// {
	///     public void Configure(SaveConfig c)
	///     {
	///         c.SetKey("secret-v2", "secret-v1");
	///         c.SchemaVersion = 5;
	///         c.AddMigrations&lt;MigrationsV1&gt;();
	///         c.Verify(ctx => { if (!ctx.Exists("player")) throw new Exception("player missing"); });
	///     }
	/// }
	/// </code>
	/// </summary>
	public interface ISaveSetup
	{
		void Configure(SaveConfig config);
	}

	/// <summary>Filled by <see cref="ISaveSetup.Configure"/>.</summary>
	public sealed class SaveConfig
	{
		internal string Secret;
		internal string[] PreviousSecrets;
		internal readonly List<Func<ISaveMigrationSet>> MigrationSets = new();
		internal Action<SaveMigrationContext> VerifyCheck;

		/// <summary>
		/// Same as Save.SchemaVersion. Raise by 1 whenever saved data changes shape.
		/// Left unset, Save.SchemaVersion keeps its value.
		/// </summary>
		public int? SchemaVersion { get; set; }

		/// <summary>Same as SaveEncryption.SetKey: current secret first, then older ones.</summary>
		public void SetKey(string secret, params string[] previousSecrets)
		{
			if (string.IsNullOrEmpty(secret))
				throw new ArgumentException("Secret must not be empty.", nameof(secret));
			Secret = secret;
			PreviousSecrets = previousSecrets;
		}

		/// <summary>Add a group of migration steps. Created only when a save needs migrating.</summary>
		public void AddMigrations<T>() where T : ISaveMigrationSet, new()
		{
			MigrationSets.Add(() => new T());
		}

		/// <summary>
		/// Check run after the last step, before anything is saved. Throw to reject the result:
		/// the migration then fails and the save stays as it was. Several Verify calls add up.
		/// </summary>
		public void Verify(Action<SaveMigrationContext> check)
		{
			VerifyCheck += check ?? throw new ArgumentNullException(nameof(check));
		}
	}
}
