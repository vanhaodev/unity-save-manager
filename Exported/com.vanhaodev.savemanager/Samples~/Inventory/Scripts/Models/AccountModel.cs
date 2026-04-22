namespace vanhaodev.savemanager.Samples.Inventory
{
	public class AccountModel : ISaveable
	{
		public string Password { get; set; }
		public void WriteSave(SaveWriter w)
		{
			w.AddString(Password);
		}

		public void ReadSave(SaveReader r)
		{
			Password = r.ReadString();
		}
	}
}