using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using vanhaodev.encryptionmanager;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// Keys for save encryption (AES) and save signing.<br/>
	/// Saves are always signed, and AES-encrypted by default.<br/>
	/// - SetKey(secret) called: your own keys.<br/>
	/// - Not called: the built-in <see cref="DefaultKey"/>. Saves can be copied between devices, and anyone
	///   who reads this library's source can decrypt / re-sign them. Your choice, your risk.
	/// </summary>
	public static class SaveEncryption
	{
		internal sealed class KeySet
		{
			public AesEncryption Aes;
			public byte[] SigningKey;
		}

		/// <summary>
		/// The secret used when SetKey is never called. Pass it as a previous secret when
		/// adding SetKey to a game already released without it, so players keep their saves.
		/// </summary>
		public const string DefaultKey = "vanhaodev.savemanager/default-secret";

		// Fixed, library-specific salt: the same secret always gives the same keys
		// (saves stay loadable), but keys differ from any other use of that secret.
		private static readonly byte[] KeySalt = Encoding.UTF8.GetBytes("vanhaodev.savemanager/keys/v1");
		private const int KeyIterations = 10000;

		private static KeySet _keys;
		private static List<KeySet> _previousKeys = new();

		// Lazily falls back to the default key. A race only derives the same keys twice.
		internal static KeySet CurrentKeys => _keys ??= Derive(DefaultKey);

		/// <summary>Current keys first, then previous ones (only used to read and migrate old saves).</summary>
		internal static IEnumerable<KeySet> AcceptedKeys
		{
			get
			{
				yield return CurrentKeys;
				foreach (var keys in _previousKeys)
					yield return keys;
			}
		}

		/// <summary>
		/// Set the secret used for AES encryption AND for signing saves (detects edited files).<br/>
		/// Any string works (account id hash, pieces joined at runtime...): it is stretched into
		/// two independent strong keys. Call once at game start, BEFORE any Save call.<br/>
		/// Same secret on every device = saves can be copied between devices;
		/// per-player secret (e.g. account id) = a save only loads for its owner.<br/>
		/// <br/>
		/// previousSecrets: secrets of older game versions (use <see cref="DefaultKey"/> if an older
		/// version did not call SetKey). Saves made with them are loaded once and moved to the new secret.
		/// While a previous secret is listed, saves signed with it are still accepted — drop it after
		/// players had time to update.
		/// </summary>
		public static void SetKey(string secret, params string[] previousSecrets)
		{
			if (string.IsNullOrEmpty(secret))
				throw new ArgumentException("Secret must not be empty.", nameof(secret));

			if (SaveManifestStore.IsLoaded)
				Debug.LogWarning("[SaveManager] SetKey called after saves were loaded. Call it before any Save call, otherwise existing saves are checked with the old key.");

			var previous = new List<KeySet>();
			foreach (var old in previousSecrets ?? Array.Empty<string>())
			{
				if (!string.IsNullOrEmpty(old) && old != secret)
					previous.Add(Derive(old));
			}

			_keys = Derive(secret);
			_previousKeys = previous;
		}

		/// <summary>Kept for compatibility. Same as SetKey(secret).</summary>
		public static void SetKey(EncryptionType type, string key)
		{
			SetKey(key);
		}

		internal static void ResetKey()
		{
			_keys = null;
			_previousKeys = new List<KeySet>();
		}

		/// <summary>
		/// Use other secrets until the returned scope is disposed (migration simulation reads
		/// another save with the game's secrets, then puts the current ones back).
		/// </summary>
		internal static IDisposable UseTemporarily(string secret, string[] previousSecrets)
		{
			var scope = new KeyScope(_keys, _previousKeys);
			_keys = null;
			_previousKeys = new List<KeySet>();
			if (!string.IsNullOrEmpty(secret))
			{
				_keys = Derive(secret);
				foreach (var old in previousSecrets ?? Array.Empty<string>())
				{
					if (!string.IsNullOrEmpty(old) && old != secret)
						_previousKeys.Add(Derive(old));
				}
			}

			return scope;
		}

		private sealed class KeyScope : IDisposable
		{
			private readonly KeySet _keys;
			private readonly List<KeySet> _previous;

			internal KeyScope(KeySet keys, List<KeySet> previous)
			{
				_keys = keys;
				_previous = previous;
			}

			public void Dispose()
			{
				SaveEncryption._keys = _keys;
				SaveEncryption._previousKeys = _previous;
			}
		}

		public static byte[] Encrypt(byte[] data, EncryptionType type)
		{
			return type == EncryptionType.None ? data : CurrentKeys.Aes.Encrypt(data);
		}

		public static byte[] Decrypt(byte[] data, EncryptionType type)
		{
			return type == EncryptionType.None ? data : CurrentKeys.Aes.Decrypt(data);
		}

		/// <summary>HMAC-SHA256 with the signing key (current keys unless given).</summary>
		internal static byte[] Sign(byte[] data, KeySet keys = null)
		{
			using var hmac = new HMACSHA256((keys ?? CurrentKeys).SigningKey);
			return hmac.ComputeHash(data);
		}

		/// <summary>
		/// Move one save file (header + payload) from old keys to the current keys.
		/// Unencrypted files are returned unchanged (they are only re-signed via the manifest).
		/// </summary>
		internal static byte[] Reencrypt(byte[] fileBytes, KeySet from)
		{
			var encryption = SaveHeader.Read(new SaveReader(fileBytes));
			if (encryption == EncryptionType.None)
				return fileBytes;

			var payload = new byte[fileBytes.Length - SaveHeader.HEADER_SIZE];
			Array.Copy(fileBytes, SaveHeader.HEADER_SIZE, payload, 0, payload.Length);
			var reencrypted = CurrentKeys.Aes.Encrypt(from.Aes.Decrypt(payload));

			var result = new byte[SaveHeader.HEADER_SIZE + reencrypted.Length];
			Array.Copy(fileBytes, 0, result, 0, SaveHeader.HEADER_SIZE);
			Array.Copy(reencrypted, 0, result, SaveHeader.HEADER_SIZE, reencrypted.Length);
			return result;
		}

		/// <summary>Constant-time comparison, so timing does not leak how many bytes matched.</summary>
		internal static bool SignatureEquals(byte[] a, byte[] b)
		{
			if (a == null || b == null || a.Length != b.Length)
				return false;

			var diff = 0;
			for (int i = 0; i < a.Length; i++)
				diff |= a[i] ^ b[i];

			return diff == 0;
		}

		// Own AES instance (not EncryptionManager's shared slot), so the game's
		// other uses of EncryptionManager are never overwritten by save keys.
		private static KeySet Derive(string secret)
		{
			using var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(secret), KeySalt, KeyIterations, HashAlgorithmName.SHA256);
			return new KeySet
			{
				Aes = new AesEncryption(kdf.GetBytes(32)),
				SigningKey = kdf.GetBytes(32)
			};
		}
	}
}
