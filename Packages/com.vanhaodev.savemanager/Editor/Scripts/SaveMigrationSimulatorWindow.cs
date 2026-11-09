using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace vanhaodev.savemanager.Editor
{
	/// <summary>
	/// Tools > Save Manager > Simulate Migration: runs the game's ISaveSetup migrations on copies
	/// of old saves (snapshots, backups, any folder) in memory and shows each step, the keys
	/// changed and the Verify result. Never writes to any save.
	/// </summary>
	internal sealed class SaveMigrationSimulatorWindow : EditorWindow
	{
		private enum Source { AllSnapshots, CurrentSave, Backups, Folder }

		private Type[] _setups = Array.Empty<Type>();
		private int _setupIndex;
		private Source _source;
		private string _folder = "";
		private Vector2 _scroll;
		private readonly List<SaveMigrationSimulation> _results = new();

		private static string SaveFolder => Path.Combine(Application.persistentDataPath, "SaveData");

		[MenuItem("Tools/Save Manager/Simulate Migration", priority = 50)]
		private static void Open() => GetWindow<SaveMigrationSimulatorWindow>("Simulate Migration");

		private void OnEnable() => FindSetups();

		private void FindSetups()
		{
			_setups = TypeCache.GetTypesDerivedFrom<ISaveSetup>()
				.Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
				.OrderBy(t => t.FullName)
				.ToArray();
			_setupIndex = Mathf.Clamp(_setupIndex, 0, Math.Max(0, _setups.Length - 1));
		}

		private void OnGUI()
		{
			if (_setups.Length == 0)
			{
				EditorGUILayout.HelpBox("No ISaveSetup class found. Put the game's save settings (secret, SchemaVersion, migrations) in a class implementing ISaveSetup.", MessageType.Info);
				if (GUILayout.Button("Refresh")) FindSetups();
				return;
			}

			_setupIndex = EditorGUILayout.Popup("Setup", _setupIndex, _setups.Select(t => t.FullName).ToArray());
			_source = (Source)EditorGUILayout.EnumPopup("Saves to test", _source);
			if (_source == Source.Folder)
			{
				EditorGUILayout.BeginHorizontal();
				_folder = EditorGUILayout.TextField("Save folder", _folder);
				if (GUILayout.Button("...", GUILayout.Width(30)))
				{
					var picked = EditorUtility.OpenFolderPanel("Folder with manifest.dat", _folder, "");
					if (!string.IsNullOrEmpty(picked)) _folder = picked;
				}
				EditorGUILayout.EndHorizontal();
			}

			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				EditorGUILayout.HelpBox("Exit Play Mode to simulate (it uses the game's secrets temporarily).", MessageType.Warning);
				return;
			}

			if (GUILayout.Button("Run", GUILayout.Height(28)))
				Run();

			DrawResults();
		}

		private void Run()
		{
			_results.Clear();
			var folders = Folders();
			if (folders.Count == 0)
			{
				ShowNotification(new GUIContent(_source == Source.AllSnapshots
					? "No snapshots yet: play the game once (Auto Snapshot) to record one."
					: "No save found there."));
				return;
			}

			var setup = (ISaveSetup)Activator.CreateInstance(_setups[_setupIndex]);
			foreach (var folder in folders)
				_results.Add(SaveMigrationSimulator.Run(setup, folder));
		}

		private List<string> Folders()
		{
			switch (_source)
			{
				case Source.AllSnapshots:
					return SaveSnapshotRecorder.List();
				case Source.CurrentSave:
					return HasSave(SaveFolder) ? new List<string> { SaveFolder } : new List<string>();
				case Source.Backups:
					var backups = Path.Combine(SaveFolder, "backups");
					return Directory.Exists(backups) ? Directory.GetDirectories(backups).Where(HasSave).OrderBy(d => d).ToList() : new List<string>();
				default:
					return HasSave(_folder) ? new List<string> { _folder } : new List<string>();
			}
		}

		private static bool HasSave(string folder) =>
			!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "manifest.dat"));

		private void DrawResults()
		{
			if (_results.Count == 0)
				return;

			var failed = _results.Count(r => r.Result == SaveMigrationResult.Failed || r.Result == SaveMigrationResult.TooOld);
			EditorGUILayout.HelpBox(failed == 0 ? $"All {_results.Count} saves OK." : $"{failed} of {_results.Count} saves FAILED.",
				failed == 0 ? MessageType.Info : MessageType.Error);

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			foreach (var result in _results)
			{
				var text = result.ToString();
				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				EditorGUILayout.SelectableLabel(text, EditorStyles.wordWrappedLabel,
					GUILayout.Height(EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(text), position.width - 40)));
				if (result.Error != null && result.Steps.All(s => s.Error == null))
					EditorGUILayout.LabelField(result.Error.GetType().Name, EditorStyles.miniLabel);
				if (result.Result == SaveMigrationResult.Migrated && GUILayout.Button("Export migrated save...", GUILayout.Width(180)))
					Export(result);
				EditorGUILayout.EndVertical();
			}
			EditorGUILayout.EndScrollView();
		}

		private static void Export(SaveMigrationSimulation result)
		{
			var folder = EditorUtility.SaveFolderPanel("Export migrated save (empty folder)", "", "migrated-" + Path.GetFileName(result.SourceFolder));
			if (string.IsNullOrEmpty(folder))
				return;

			try
			{
				result.ExportTo(folder);
				EditorUtility.RevealInFinder(folder);
			}
			catch (Exception e)
			{
				EditorUtility.DisplayDialog("Export failed", e.Message, "OK");
			}
		}
	}
}
