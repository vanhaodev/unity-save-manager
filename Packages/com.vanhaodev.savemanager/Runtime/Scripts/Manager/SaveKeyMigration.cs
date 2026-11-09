using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Moves a save made with a previous secret (SetKey previousSecrets) to the current secret:
	/// AES files are decrypted with the old key and encrypted with the new one, then the whole
	/// set is committed, so the new manifest is signed with the current key.
	/// After this, the previous secret is no longer needed to load the save.
	/// </summary>
	internal static class SaveKeyMigration
	{
		/// <summary>Re-encrypted bytes for every AES file of the manifest (unencrypted files need no change).</summary>
		internal static Dictionary<string, byte[]> BuildChanges(SaveManifest manifest, SaveEncryption.KeySet signedWith)
		{
			var changes = new Dictionary<string, byte[]>();

			foreach (var kv in manifest.Entries)
			{
				var bytes = File.ReadAllBytes(SaveFileStore.DataPath(kv.Key, kv.Value.Generation));
				var moved = TryReencrypt(bytes, signedWith);

				if (moved == null)
					Debug.LogWarning($"[SaveManager] Save '{kv.Key}' could not be decrypted with any SetKey secret; left unchanged.");
				else if (!ReferenceEquals(moved, bytes))
					changes[kv.Key] = moved;
			}

			return changes;
		}

		// Normally encrypted with the key that signed the manifest; other accepted keys are tried
		// so one odd file (e.g. SetKey called mid-session) never blocks the whole migration.
		private static byte[] TryReencrypt(byte[] bytes, SaveEncryption.KeySet signedWith)
		{
			var candidates = new List<SaveEncryption.KeySet> { signedWith };
			foreach (var keys in SaveEncryption.AcceptedKeys)
			{
				if (keys != signedWith)
					candidates.Add(keys);
			}

			foreach (var keys in candidates)
			{
				try
				{
					return SaveEncryption.Reencrypt(bytes, keys);
				}
				catch (System.Security.Cryptography.CryptographicException)
				{
					// Wrong key for this file, try the next one.
				}
			}

			return null;
		}
	}
}
