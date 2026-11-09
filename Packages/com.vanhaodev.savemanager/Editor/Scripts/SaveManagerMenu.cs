using System.IO;
using UnityEditor;
using UnityEngine;

namespace vanhaodev.savemanager.Editor
{
	public static class SaveManagerMenu
	{
		private static string SaveFolder => Path.Combine(Application.persistentDataPath, "SaveData");

		[MenuItem("Tools/Save Manager/Explore Save Files")]
		private static void ExploreSaveFiles()
		{
			if (!Directory.Exists(SaveFolder))
				Directory.CreateDirectory(SaveFolder);

			EditorUtility.RevealInFinder(SaveFolder);
		}

		[MenuItem("Tools/Save Manager/Delete All Save Data")]
		private static void DeleteAllSaveData()
		{
			// The running game keeps the save index in memory and would write it back.
			if (EditorApplication.isPlaying)
			{
				EditorUtility.DisplayDialog("Delete Save Data", "Exit Play Mode before deleting save data.", "OK");
				return;
			}

			// Whole folder: save index, data files and their backups belong together.
			var files = Directory.Exists(SaveFolder) ? Directory.GetFiles(SaveFolder, "*", SearchOption.AllDirectories) : new string[0];

			if (files.Length == 0)
			{
				EditorUtility.DisplayDialog("Delete Save Data", "No save files found.", "OK");
				return;
			}

			var confirm = EditorUtility.DisplayDialog(
				"Delete Save Data",
				$"Delete {files.Length} save file(s)?\n\nThis cannot be undone.",
				"Delete",
				"Cancel"
			);

			if (!confirm) return;

			// Includes quarantine/ (saves that could not be verified).
			// Includes quarantine/ (saves that could not be verified).
			foreach (var file in files)
			{
				File.Delete(file);
			}

			foreach (var dir in Directory.GetDirectories(SaveFolder))
			{
				Directory.Delete(dir, true);
			}

			EditorUtility.DisplayDialog("Delete Save Data", $"Deleted {files.Length} file(s).", "OK");
		}
	}
}
