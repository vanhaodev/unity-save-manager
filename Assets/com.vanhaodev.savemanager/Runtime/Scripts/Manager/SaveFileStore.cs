using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Low-level file operations for the save folder.<br/>
	/// Data files are never overwritten: each commit writes "{key}.{generation}.sav",
	/// and the manifest decides which generation of each key is current.<br/>
	/// Every caller must hold <see cref="IoLock"/> while touching save files.
	/// </summary>
	internal static class SaveFileStore
	{
		internal const string DataExtension = ".sav";
		internal const string ManifestFileName = "manifest.dat";

		internal static readonly object IoLock = new();

		// Must be first touched on the main thread (Application API).
		internal static readonly string Folder = Path.Combine(Application.persistentDataPath, "SaveData");

		internal static string ManifestPath => Path.Combine(Folder, ManifestFileName);
		internal static string TmpPath(string path) => path + ".tmp";
		internal static string BakPath(string path) => path + ".bak";

		internal static string DataPath(string key, long generation)
		{
			return Path.Combine(Folder, $"{key}.{generation}{DataExtension}");
		}

		// "{key}.{generation}.sav" — key may itself contain dots, generation is the last number.
		private static readonly Regex DataFileName = new(@"^(.+)\.(\d+)\.sav$", RegexOptions.CultureInvariant);

		/// <summary>All data files in the folder as (key, generation, path).</summary>
		internal static IEnumerable<(string key, long generation, string path)> ListDataFiles()
		{
			if (!Directory.Exists(Folder))
				yield break;

			foreach (var path in Directory.GetFiles(Folder, "*" + DataExtension))
			{
				var match = DataFileName.Match(Path.GetFileName(path));
				if (match.Success && long.TryParse(match.Groups[2].Value, out var generation))
					yield return (match.Groups[1].Value, generation, path);
			}
		}

		internal static void EnsureFolder()
		{
			if (!Directory.Exists(Folder))
				Directory.CreateDirectory(Folder);
		}

		/// <summary>
		/// Crash-safe replace for a small file (the manifest): at every moment either
		/// path or bak holds a complete copy. bak keeps the previous version.
		/// </summary>
		internal static void WriteAtomic(string path, byte[] bytes)
		{
			var tmp = TmpPath(path);
			var bak = BakPath(path);

			WriteDurable(tmp, bytes);

			if (File.Exists(path))
			{
				if (File.Exists(bak))
					File.Delete(bak);
				File.Move(path, bak);
			}

			File.Move(tmp, path);
		}

		// Flush(true) forces data to the device before anything points at it,
		// otherwise power loss can leave a 0-byte / garbage file behind a valid name.
		internal static void WriteDurable(string path, byte[] bytes)
		{
			using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
			fs.Write(bytes, 0, bytes.Length);
			fs.Flush(true);
		}

		internal static byte[] Hash(byte[] bytes)
		{
			using var sha = SHA256.Create();
			return sha.ComputeHash(bytes);
		}

		internal static bool HashEquals(byte[] a, byte[] b)
		{
			if (a == null || b == null || a.Length != b.Length)
				return false;

			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
					return false;
			}

			return true;
		}

		internal static void TryDelete(string path)
		{
			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch (System.Exception)
			{
				// Best effort: a stale file is harmless, the next load cleans it up.
			}
		}
	}
}
