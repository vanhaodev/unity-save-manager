using System.Collections.Generic;

namespace vanhaodev.savemanager.Samples.Inventory
{
	public class ItemModel : ISaveable
	{
		public long Id { get; set; }
		public string Name { get; set; }
		public void WriteSave(SaveWriter w)
		{
			w.AddLong(Id);
			w.AddString(Name);
		}

		public void ReadSave(SaveReader r)
		{
			Id = r.ReadLong();
			Name = r.ReadString();
		}
	}
}