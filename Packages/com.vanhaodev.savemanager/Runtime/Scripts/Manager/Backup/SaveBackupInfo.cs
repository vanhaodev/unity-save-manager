using System;

namespace vanhaodev.savemanager
{
	/// <summary>One backup of the save (see Save.ListBackups / Save.RestoreBackup).</summary>
	public sealed class SaveBackupInfo
	{
		/// <summary>Folder of the backup.</summary>
		public string Path { get; }

		/// <summary>Schema version of the save when it was backed up.</summary>
		public int SchemaVersion { get; }

		/// <summary>When the backup was made (local time).</summary>
		public DateTime CreatedAt { get; }

		internal SaveBackupInfo(string path, int schemaVersion, DateTime createdAt)
		{
			Path = path;
			SchemaVersion = schemaVersion;
			CreatedAt = createdAt;
		}

		public override string ToString() => $"Backup schema {SchemaVersion} ({CreatedAt:yyyy-MM-dd HH:mm:ss})";
	}
}
