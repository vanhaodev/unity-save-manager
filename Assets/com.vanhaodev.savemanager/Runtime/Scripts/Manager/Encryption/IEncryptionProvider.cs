namespace vanhaodev.savemanager
{
	public interface IEncryptionProvider
	{
		byte[] Encrypt(byte[] data);
		byte[] Decrypt(byte[] data);
	}
}
