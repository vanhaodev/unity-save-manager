using System.Collections.Generic;
using UnityEngine;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class InventoryManager : MonoBehaviour
	{
		private const string InvFileName = "inventory";

		private static readonly string[] Prefixes =
		{
			"Ancient", "Blazing", "Cursed", "Divine", "Ethereal",
			"Frozen", "Gilded", "Haunted", "Infernal", "Jade",
			"Keen", "Lunar", "Mystic", "Nightmare", "Obsidian",
			"Phantom", "Radiant", "Shadow", "Thunder", "Unholy",
			"Venomous", "Wicked", "Crimson", "Dread", "Eclipse"
		};

		private static readonly string[] Equipment =
		{
			"Sword", "Shield", "Helm", "Armor", "Gauntlet",
			"Boots", "Cloak", "Ring", "Amulet", "Staff",
			"Bow", "Dagger", "Axe", "Mace", "Spear",
			"Orb", "Tome", "Crown", "Belt", "Bracers",
			"Greaves", "Pendant", "Robe", "Blade", "Hammer"
		};

		public InventoryModel Data { get; private set; }

		public void Load()
		{
			Data = Save.Get<InventoryModel>(InvFileName);

			if (Data == null)
			{
				Data = new InventoryModel
				{
					Items = new List<ItemModel>()
				};
			}
		}

		public void SaveData()
		{
			if (Data == null)
			{
				Data = new InventoryModel
				{
					Items = new List<ItemModel>()
				};
			}

			Save.Set(InvFileName, Data);
		}

		public ItemModel GenerateItem()
		{
			if (Data == null) Load();

			var prefix = Prefixes[Random.Range(0, Prefixes.Length)];
			var equip = Equipment[Random.Range(0, Equipment.Length)];

			var item = new ItemModel
			{
				Id = Data.GenerateId(),
				Name = $"{prefix} {equip}"
			};

			Data.Items.Add(item);
			return item;
		}

		public ItemModel GetItem(long id)
		{
			if (Data?.Items == null) return null;

			for (int i = 0; i < Data.Items.Count; i++)
			{
				if (Data.Items[i].Id == id)
				{
					return Data.Items[i];
				}
			}

			return null;
		}

		public bool RemoveItem(long id)
		{
			if (Data?.Items == null) return false;

			for (int i = 0; i < Data.Items.Count; i++)
			{
				if (Data.Items[i].Id == id)
				{
					Data.Items.RemoveAt(i);
					return true;
				}
			}

			return false;
		}
	}
}
