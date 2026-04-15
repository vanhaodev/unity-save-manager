namespace vanhaodev.savemanager
{
	public static class SaveHeader
	{
		public const string MAGIC = "SAVE";
		public const byte VERSION = 1;

		public static void Write(SaveWriter w)
		{
			var magicBytes = System.Text.Encoding.ASCII.GetBytes(MAGIC);
			w.AddBytes(magicBytes);

			w.AddByte(VERSION);
			w.AddByte(0); // flags (future)
		}

		public static void Read(SaveReader r)
		{
			var magic = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));

			if (magic != MAGIC)
				throw new System.Exception("Invalid save file");

			byte version = r.ReadByte();
			byte flags = r.ReadByte();

			// future: handle version / flags
		}
	}
}