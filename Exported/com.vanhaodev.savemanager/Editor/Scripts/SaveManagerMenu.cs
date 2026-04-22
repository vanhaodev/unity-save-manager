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
			if (!Directory.Exists(SaveFolder))
			{
				EditorUtility.DisplayDialog("Delete Save Data", "No save files found.", "OK");
				return;
			}

			var files = Directory.GetFiles(SaveFolder, "*.dat");

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

			foreach (var file in files)
			{
				File.Delete(file);
			}

			EditorUtility.DisplayDialog("Delete Save Data", $"Deleted {files.Length} file(s).", "OK");
		}
	}
}
