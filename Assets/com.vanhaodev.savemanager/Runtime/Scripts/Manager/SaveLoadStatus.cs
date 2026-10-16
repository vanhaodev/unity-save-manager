namespace vanhaodev.savemanager
{
	/// <summary>Result of Save.TryGet.</summary>
	public enum SaveLoadStatus
	{
		/// <summary>Loaded.</summary>
		Ok,

		/// <summary>Never saved (or deleted): a new player, use default data.</summary>
		NotFound,

		/// <summary>
		/// A save exists but cannot be used: file broken or edited, or it no longer matches
		/// the data class. Avoid overwriting it right away if you want a chance to recover it.
		/// </summary>
		Corrupt,

		/// <summary>The file uses a different encryption than the one required.</summary>
		WrongEncryption
	}
}
