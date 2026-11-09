namespace vanhaodev.savemanager
{
	/// <summary>Version info passed to version hooks (NewerSaveDetected, SaveTouchedByOlderVersion).</summary>
	public sealed class SaveVersionEvent
	{
		/// <summary>Schema version of the save on disk.</summary>
		public int SaveVersion { get; }

		/// <summary>Save.SchemaVersion of the running game.</summary>
		public int GameVersion { get; }

		internal SaveVersionEvent(int saveVersion, int gameVersion)
		{
			SaveVersion = saveVersion;
			GameVersion = gameVersion;
		}

		/// <summary>
		/// Let the game save anyway (same as Save.AllowWrite()). Data only the newer version
		/// understands may be lost in the keys this game writes.
		/// </summary>
		public void AllowWrite() => Save.AllowWrite();
	}
}
