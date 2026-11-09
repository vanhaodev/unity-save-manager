using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class InventoryUI : MonoBehaviour
	{
		[SerializeField] private Button _saveButton;
		[SerializeField] private Button _loadButton;
		[SerializeField] private Button _generateButton;
		[SerializeField] private Transform _container;
		[SerializeField] private ItemUI _itemPrefab;
		
		private InventoryManager _manager;

#if !UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX)
		private bool _showSuccessDialog;
		private string _dialogTitle;
		private string _dialogMessage;
#endif

		private void Awake()
		{
			_manager = GetComponent<InventoryManager>();
		}

		private void Start()
		{
			_saveButton.onClick.AddListener(OnSaveClick);
			_loadButton.onClick.AddListener(OnLoadClick);
			_generateButton.onClick.AddListener(OnGenerateClick);

			_manager.Load();
			RefreshUI();
		}

		private void OnSaveClick()
		{
			_manager.SaveData();
			var count = _manager.Data?.Items?.Count ?? 0;
			ShowDialog("Save Complete", $"Saved {count} item(s) to disk.");
		}

		private void OnLoadClick()
		{
			_manager.Load();
			RefreshUI();
			var count = _manager.Data?.Items?.Count ?? 0;
			ShowDialog("Load Complete", $"Loaded {count} item(s) from disk.");
		}

		private void OnGenerateClick()
		{
			var item = _manager.GenerateItem();
			RefreshUI();
			ShowDialog("Item Created", $"ID: {item.Id}\nName: {item.Name}");
		}

		private void ShowDialog(string title, string message)
		{
#if UNITY_EDITOR
			EditorUtility.DisplayDialog(title, message, "OK");
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
			_dialogTitle = title;
			_dialogMessage = message;
			_showSuccessDialog = true;
#endif
		}

#if !UNITY_EDITOR && (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX)
		private void OnGUI()
		{
			if (!_showSuccessDialog) return;

			var windowWidth = 280f;
			var windowHeight = 120f;
			var windowRect = new Rect(
				(Screen.width - windowWidth) / 2f,
				(Screen.height - windowHeight) / 2f,
				windowWidth,
				windowHeight
			);

			GUI.ModalWindow(0, windowRect, DrawDialogWindow, _dialogTitle);
		}

		private void DrawDialogWindow(int windowId)
		{
			GUILayout.Space(10);
			GUILayout.Label(_dialogMessage, GUILayout.ExpandWidth(true));
			GUILayout.FlexibleSpace();

			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("OK", GUILayout.Width(80), GUILayout.Height(30)))
			{
				_showSuccessDialog = false;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(10);
		}
#endif

		private void RefreshUI()
		{
			foreach (Transform child in _container)
			{
				Destroy(child.gameObject);
			}

			if (_manager.Data?.Items == null) return;

			foreach (var item in _manager.Data.Items)
			{
				var itemUI = Instantiate(_itemPrefab, _container);
				itemUI.Setup(item, OnDeleteItem);
			}
		}

		private void OnDeleteItem(long id)
		{
			var item = _manager.GetItem(id);
			if (item != null && _manager.RemoveItem(id))
			{
				RefreshUI();
				ShowDialog("Item Deleted", $"ID: {item.Id}\nName: {item.Name}");
			}
		}

		private void OnDestroy()
		{
			_saveButton.onClick.RemoveListener(OnSaveClick);
			_loadButton.onClick.RemoveListener(OnLoadClick);
			_generateButton.onClick.RemoveListener(OnGenerateClick);
		}
	}
}
