namespace vanhaodev.savemanager
{
	public static class SaveHeader
	{
		public const string MAGIC = "SAVE";
		public const byte VERSION = 1;
		public const int HEADER_SIZE = 6; // MAGIC(4) + VERSION(1) + ENCRYPTION(1)

		public static void Write(SaveWriter w, EncryptionType encryption = EncryptionType.None)
		{
			var magicBytes = System.Text.Encoding.ASCII.GetBytes(MAGIC);
			w.AddBytes(magicBytes);

			w.AddByte(VERSION);
			w.AddByte((byte)encryption);
		}

		public static EncryptionType Read(SaveReader r)
		{
			var magic = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));

			if (magic != MAGIC)
				throw new System.Exception("Invalid save file");

			byte version = r.ReadByte();
			byte encryption = r.ReadByte();

			return (EncryptionType)encryption;
		}
	}
}