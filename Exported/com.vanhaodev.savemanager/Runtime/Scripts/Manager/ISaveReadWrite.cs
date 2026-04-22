namespace vanhaodev.savemanager
{
	public interface ISaveable
	{
		void WriteSave(SaveWriter w);
		void ReadSave(SaveReader r);
	}
}