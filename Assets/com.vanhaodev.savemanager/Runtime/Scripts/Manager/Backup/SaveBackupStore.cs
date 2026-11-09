using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Full copies of the save in SaveData/backups/&lt;time&gt;-schema-&lt;N&gt;/ (manifest.dat + data files),
	/// made before a migration and before the game writes over a save it was blocked from.
	/// The folder is never read by normal loading. Callers hold <see cref="SaveFileStore.IoLock"/>.
	/// </summary>
	internal static class SaveBackupStore
	{
		private const string TimeFormat = "yyyyMMdd-HHmmss-fff";
		private const string SchemaPart = "-schema-";
		private const string TmpSuffix = ".tmp";

		internal static string Folder => Path.Combine(SaveFileStore.Folder, "backups");

		/// <summary>
		/// Copy the save in use. Throws if it cannot be copied completely (nothing half-made is kept).
		/// Returns null when there is nothing to back up. Keeps the newest <paramref name="keep"/> backups.
		/// </summary>
		internal static SaveBackupInfo Create(int keep)
		{
			var (manifest, dataFiles, schema) = SaveManifestStore.Snapshot();
			if (dataFiles.Count == 0)
				return null;

			var created = DateTime.UtcNow;
			var name = created.ToString(TimeFormat, CultureInfo.InvariantCulture) + SchemaPart + schema;
			var target = Path.Combine(Folder, name);
			for (var i = 1; Directory.Exists(target); i++)
				target = Path.Combine(Folder, name + "-" + i);

			// Built under a .tmp name and renamed at the end: a crash never leaves a half backup.
			var tmp = target + TmpSuffix;
			try
			{
				Directory.CreateDirectory(tmp);
				foreach (var file in dataFiles)
					SaveFileStore.WriteDurable(Path.Combine(tmp, Path.GetFileName(file)), File.ReadAllBytes(file));
				SaveFileStore.WriteDurable(Path.Combine(tmp, SaveFileStore.ManifestFileName), manifest);
				Directory.Move(tmp, target);
			}
			catch
			{
				TryDeleteFolder(tmp);
				throw;
			}

			DeleteOldest(keep);
			return new SaveBackupInfo(target, schema, created.ToLocalTime());
		}

		/// <summary>Complete backups, newest first.</summary>
		internal static List<SaveBackupInfo> List()
		{
			var result = new List<SaveBackupInfo>();
			if (!Directory.Exists(Folder))
				return result;

			foreach (var dir in Directory.GetDirectories(Folder))
			{
				var name = Path.GetFileName(dir);
				if (name.EndsWith(TmpSuffix, StringComparison.Ordinal))
				{
					TryDeleteFolder(dir); // left by a crash while backing up
					continue;
				}

				var split = name.IndexOf(SchemaPart, StringComparison.Ordinal);
				if (split < 0 ||
				    !DateTime.TryParseExact(name.Substring(0, split), TimeFormat, CultureInfo.InvariantCulture,
					    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created))
					continue;

				var schemaText = name.Substring(split + SchemaPart.Length);
				var dash = schemaText.IndexOf('-');
				if (dash >= 0)
					schemaText = schemaText.Substring(0, dash);
				if (int.TryParse(schemaText, NumberStyles.None, CultureInfo.InvariantCulture, out var schema))
					result.Add(new SaveBackupInfo(dir, schema, created.ToLocalTime()));
			}

			// Folder names start with the time, so name order is time order.
			result.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b.Path), Path.GetFileName(a.Path)));
			return result;
		}

		/// <summary>
		/// Load and check a backup like a normal save (signature + every fingerprint).
		/// Returns key -> file bytes moved to the current SetKey secret. Throws InvalidDataException.
		/// </summary>
		internal static Dictionary<string, byte[]> ReadVerified(SaveBackupInfo backup, out int schema)
		{
			var manifestPath = Path.Combine(backup.Path, SaveFileStore.ManifestFileName);
			if (!File.Exists(manifestPath))
				throw new InvalidDataException($"Backup '{backup.Path}' has no {SaveFileStore.ManifestFileName}.");

			var manifest = SaveManifest.Deserialize(File.ReadAllBytes(manifestPath), out var signedWith);
			schema = manifest.SchemaVersion ?? 0;

			var files = new Dictionary<string, byte[]>();
			foreach (var kv in manifest.Entries)
			{
				var path = Path.Combine(backup.Path, Path.GetFileName(SaveFileStore.DataPath(kv.Key, kv.Value.Generation)));
				if (!File.Exists(path))
					throw new InvalidDataException($"Backup is missing the data of '{kv.Key}'.");

				var bytes = File.ReadAllBytes(path);
				if (!SaveFileStore.HashEquals(SaveFileStore.Hash(bytes), kv.Value.Hash))
					throw new InvalidDataException($"Backup data of '{kv.Key}' failed integrity check.");

				files[kv.Key] = signedWith == SaveEncryption.CurrentKeys ? bytes : SaveEncryption.Reencrypt(bytes, signedWith);
			}

			return files;
		}

		private static void DeleteOldest(int keep)
		{
			var all = List();
			for (var i = Math.Max(keep, 1); i < all.Count; i++)
				TryDeleteFolder(all[i].Path);
		}

		private static void TryDeleteFolder(string path)
		{
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, true);
			}
			catch (Exception)
			{
				// Best effort: retried on the next backup.
			}
		}
	}
}
