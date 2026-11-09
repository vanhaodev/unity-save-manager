using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace vanhaodev.savemanager.Samples.Inventory
{
	/// <summary>
	/// Example of save hooks: logs every save event to the Console.
	/// Replace the Debug.Log calls with your own UI (saving icon, popup...).
	/// You can also drag this component onto any GameObject in your own scene.
	/// </summary>
	public class SaveHookLogger : MonoBehaviour
	{
		private const string SampleSceneName = "com.vanhaodev.savemanager.sample.inventory";

		// Adds the logger automatically when the sample scene is opened (no scene setup needed).
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void AttachToSampleScene()
		{
			if (SceneManager.GetActiveScene().name != SampleSceneName) return;
			if (FindAnyObjectByType<SaveHookLogger>() != null) return;

			new GameObject(nameof(SaveHookLogger)).AddComponent<SaveHookLogger>();
		}

		private void OnEnable()
		{
			Save.SaveStarted += OnSaveStarted;
			Save.Saved += OnSaved;
			Save.SaveFailed += OnSaveFailed;
			Save.IntegrityFailed += OnIntegrityFailed;
			Save.TransactionDiscarded += OnTransactionDiscarded;
		}

		private void OnDisable()
		{
			Save.SaveStarted -= OnSaveStarted;
			Save.Saved -= OnSaved;
			Save.SaveFailed -= OnSaveFailed;
			Save.IntegrityFailed -= OnIntegrityFailed;
			Save.TransactionDiscarded -= OnTransactionDiscarded;
		}

		private void OnSaveStarted(IReadOnlyList<string> keys)
		{
			Debug.Log($"[SaveHook] Saving... {string.Join(", ", keys)}");
		}

		private void OnSaved(IReadOnlyList<string> keys)
		{
			Debug.Log($"[SaveHook] <color=green>Saved</color> {string.Join(", ", keys)}");
		}

		private void OnSaveFailed(IReadOnlyList<string> keys, Exception error)
		{
			Debug.LogError($"[SaveHook] Save failed {string.Join(", ", keys)}: {error.Message}");
		}

		private void OnIntegrityFailed(string message)
		{
			Debug.LogWarning($"[SaveHook] Save file problem: {message}");
		}

		private void OnTransactionDiscarded(IReadOnlyList<string> keys)
		{
			Debug.LogWarning($"[SaveHook] Transaction not committed, discarded: {string.Join(", ", keys)}");
		}
	}
}
