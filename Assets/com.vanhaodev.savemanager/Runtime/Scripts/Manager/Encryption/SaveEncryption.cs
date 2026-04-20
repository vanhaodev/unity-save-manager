using System.Collections.Generic;

namespace vanhaodev.savemanager
{
	public static class SaveEncryption
	{
		private static Dictionary<EncryptionType, IEncryptionProvider> _providers = new();
		private static Dictionary<EncryptionType, byte[]> _keys = new();

		public static void SetKey(EncryptionType type, string key)
		{
			_keys[type] = System.Text.Encoding.UTF8.GetBytes(key);
			_providers.Remove(type);
		}

		public static byte[] Encrypt(byte[] data, EncryptionType type)
		{
			if (type == EncryptionType.None)
				return data;

			return GetProvider(type).Encrypt(data);
		}

		public static byte[] Decrypt(byte[] data, EncryptionType type)
		{
			if (type == EncryptionType.None)
				return data;

			return GetProvider(type).Decrypt(data);
		}

		private static IEncryptionProvider GetProvider(EncryptionType type)
		{
			if (_providers.TryGetValue(type, out var provider))
				return provider;

			provider = CreateProvider(type);
			_providers[type] = provider;
			return provider;
		}

		private static IEncryptionProvider CreateProvider(EncryptionType type)
		{
			_keys.TryGetValue(type, out var key);

			return type switch
			{
				EncryptionType.AES => new AesEncryptionProvider(key),
				_ => null
			};
		}
	}
}
