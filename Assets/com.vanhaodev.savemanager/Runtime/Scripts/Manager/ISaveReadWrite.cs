namespace vanhaodev.savemanager
{
	public interface ISaveReadWrite
	{
		void Write(SaveWriter w);
		void Read(SaveReader r);
	}
}