namespace vanhaodev.savemanager
{
	public class AesEncryptionProvider : IEncryptionProvider
	{
		private readonly byte[] _key;

		public AesEncryptionProvider(byte[] key = null)
		{
			_key = key;
		}

		public byte[] Encrypt(byte[] data)
		{
			// TODO: Implement real AES encryption with _key
			return data;
		}

		public byte[] Decrypt(byte[] data)
		{
			// TODO: Implement real AES decryption with _key
			return data;
		}
	}
}
