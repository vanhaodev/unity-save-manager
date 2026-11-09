namespace vanhaodev.savemanager
{
	/// <summary>
	/// A group of migration steps, e.g. one class per big game version.
	/// Registered by type (<see cref="SaveConfig.AddMigrations{T}"/>): the library creates it
	/// only when a save has to be migrated, and drops it right after.
	/// <code>
	/// public sealed class MigrationsV2 : ISaveMigrationSet
	/// {
	///     public void Register(SaveMigrations m)
	///     {
	///         m.Add(from: 3, AddAvatar);   // 3 -> 4
	///     }
	///
	///     void AddAvatar(SaveMigrationContext ctx)
	///     {
	///         var old = ctx.Get&lt;PlayerV3&gt;("player");
	///         ctx.Set("player", new Player { Name = old.Name, Avatar = "default" });
	///     }
	/// }
	/// </code>
	/// </summary>
	public interface ISaveMigrationSet
	{
		void Register(SaveMigrations m);
	}
}
