using System.Collections.Generic;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class InventoryModel : ISaveable
	{
		public long NextItemId { get; set; }
		public List<ItemModel> Items { get; set; }

		public long GenerateId()
		{
			return NextItemId++;
		}

		public void WriteSave(SaveWriter w)
		{
			w.AddLong(NextItemId);
			w.WriteList(Items);
		}

		public void ReadSave(SaveReader r)
		{
			NextItemId = r.ReadLong();
			Items = r.ReadList<ItemModel>();
		}
	}
}
