using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class ItemUI : MonoBehaviour
	{
		[SerializeField] private TMP_Text _idText;
		[SerializeField] private TMP_Text _nameText;
		[SerializeField] private Button _deleteButton;

		private long _itemId;
		private Action<long> _onDelete;

		public void Setup(ItemModel item, Action<long> onDelete)
		{
			_itemId = item.Id;
			_onDelete = onDelete;

			_idText.text = item.Id.ToString();
			_nameText.text = item.Name;

			_deleteButton.onClick.AddListener(OnDeleteClick);
		}

		private void OnDeleteClick()
		{
			_onDelete?.Invoke(_itemId);
		}

		private void OnDestroy()
		{
			_deleteButton.onClick.RemoveListener(OnDeleteClick);
		}
	}
}
