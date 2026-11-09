using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace vanhaodev.savemanager.Editor
{
	/// <summary>
	/// Editor only: while playing, every save is copied to &lt;project&gt;/SaveSnapshots/schema-&lt;N&gt;/
	/// (one folder per schema version, newest save wins). Commit the folder to keep a real save
	/// of every version, so Simulate Migration can test new migrations on old saves.
	/// </summary>
	[InitializeOnLoad]
	internal static class SaveSnapshotRecorder
	{
		private const string EnabledPref = "vanhaodev.savemanager.AutoSnapshot";
		private const string ToggleMenu = "Tools/Save Manager/Snapshots/Auto Snapshot In Play Mode";
		private static bool _scheduled;

		internal static string SnapshotsFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SaveSnapshots");

		private static bool Enabled
		{
			get => EditorPrefs.GetBool(EnabledPref, true);
			set => EditorPrefs.SetBool(EnabledPref, value);
		}

		static SaveSnapshotRecorder()
		{
			EditorApplication.playModeStateChanged += state =>
			{
				// After the library reset its static state for this play session.
				if (state == PlayModeStateChange.EnteredPlayMode)
					Save.Saved += OnSaved;
			};
		}

		/// <summary>Snapshot folders, oldest schema first.</summary>
		internal static List<string> List()
		{
			var result = new List<string>();
			if (Directory.Exists(SnapshotsFolder))
			{
				// Only "schema-<N>": skips leftovers of an interrupted copy (schema-3.new, .tmp).
				foreach (var dir in Directory.GetDirectories(SnapshotsFolder, "schema-*"))
				{
					if (SchemaOf(dir) >= 0)
						result.Add(dir);
				}
			}

			result.Sort((a, b) => SchemaOf(a).CompareTo(SchemaOf(b)));
			return result;
		}

		private static int SchemaOf(string folder)
		{
			var text = Path.GetFileName(folder).Substring("schema-".Length);
			return int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;
		}

		private static void OnSaved(IReadOnlyList<string> keys)
		{
			if (!Enabled || _scheduled)
				return;

			// Several saves in one frame -> one copy.
			_scheduled = true;
			EditorApplication.delayCall += () =>
			{
				_scheduled = false;
				Capture(false);
			};
		}

		private static void Capture(bool verbose)
		{
			if (!EditorApplication.isPlaying)
				return;

			try
			{
				var folder = Path.Combine(SnapshotsFolder, "schema-" + Save.SaveSchemaVersion);
				var schema = SaveMigrationSimulator.CaptureCurrentSave(folder);
				if (verbose)
					Debug.Log(schema == null ? "[SaveManager] Nothing saved yet, no snapshot." : $"[SaveManager] Snapshot saved: {folder}");
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[SaveManager] Could not take a save snapshot: {e.Message}");
			}
		}

		[MenuItem(ToggleMenu, priority = 100)]
		private static void Toggle() => Enabled = !Enabled;

		[MenuItem(ToggleMenu, true)]
		private static bool ToggleValidate()
		{
			Menu.SetChecked(ToggleMenu, Enabled);
			return true;
		}

		[MenuItem("Tools/Save Manager/Snapshots/Take Snapshot Now", priority = 101)]
		private static void TakeNow() => Capture(true);

		[MenuItem("Tools/Save Manager/Snapshots/Take Snapshot Now", true)]
		private static bool TakeNowValidate() => EditorApplication.isPlaying;

		[MenuItem("Tools/Save Manager/Snapshots/Open Snapshots Folder", priority = 102)]
		private static void OpenFolder()
		{
			Directory.CreateDirectory(SnapshotsFolder);
			EditorUtility.RevealInFinder(SnapshotsFolder);
		}
	}
}
