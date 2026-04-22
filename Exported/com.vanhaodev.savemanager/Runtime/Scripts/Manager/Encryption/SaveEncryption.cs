using vanhaodev.encryptionmanager;

namespace vanhaodev.savemanager
{
	public static class SaveEncryption
	{
		public static void SetKey(EncryptionType type, string key)
		{
			if (type == EncryptionType.AES)
				EncryptionManager.RegisterAes(key);
		}

		public static byte[] Encrypt(byte[] data, EncryptionType type)
		{
			if (type == EncryptionType.None)
				return data;

			return EncryptionManager.Encrypt(ToEncryptType(type), data);
		}

		public static byte[] Decrypt(byte[] data, EncryptionType type)
		{
			if (type == EncryptionType.None)
				return data;

			return EncryptionManager.Decrypt(ToEncryptType(type), data);
		}

		private static EncryptType ToEncryptType(EncryptionType type) =>
			type == EncryptionType.AES ? EncryptType.Aes : EncryptType.Xor;
	}
}
