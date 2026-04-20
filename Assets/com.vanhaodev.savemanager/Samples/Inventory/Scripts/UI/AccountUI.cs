using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class AccountUI : MonoBehaviour
	{
		[SerializeField] private Button _saveButton;
		[SerializeField] private Button _loadButton;
		[SerializeField] private TMP_InputField _passwordInputField;

		private AccountManager _manager;

		private void Awake()
		{
			_manager = GetComponent<AccountManager>();
		}

		private void Start()
		{
			_saveButton.onClick.AddListener(OnSaveClick);
			_loadButton.onClick.AddListener(OnLoadClick);

			_manager.LoadData();
			RefreshUI();
		}

		private void OnSaveClick()
		{
			_manager.Account.Password = _passwordInputField.text;
			_manager.SaveData();
		}

		private void OnLoadClick()
		{
			_manager.LoadData();
			RefreshUI();
		}

		private void RefreshUI()
		{
			_passwordInputField.text = _manager.Account?.Password ?? "";
		}

		private void OnDestroy()
		{
			_saveButton.onClick.RemoveListener(OnSaveClick);
			_loadButton.onClick.RemoveListener(OnLoadClick);
		}
	}
}