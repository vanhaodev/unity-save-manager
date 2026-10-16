using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// The "table of contents" of one complete save: which generation of each key is current
	/// and the content hash (fingerprint) of that file.<br/>
	/// Immutable: a commit builds a new manifest, so readers can hold a reference safely.
	/// </summary>
	internal sealed class SaveManifest
	{
		internal readonly struct Entry
		{
			public readonly long Generation;
			public readonly byte[] Hash;

			public Entry(long generation, byte[] hash)
			{
				Generation = generation;
				Hash = hash;
			}
		}

		private const string MAGIC = "SMAN";
		private const byte VERSION = 1;
		private const int HASH_SIZE = 32;

		internal static readonly SaveManifest Empty = new(0, new Dictionary<string, Entry>());

		internal long Generation { get; }
		private readonly Dictionary<string, Entry> _entries;

		internal SaveManifest(long generation, Dictionary<string, Entry> entries)
		{
			Generation = generation;
			_entries = entries;
		}

		internal IReadOnlyDictionary<string, Entry> Entries => _entries;

		internal bool TryGet(string key, out Entry entry) => _entries.TryGetValue(key, out entry);

		internal bool References(string key, long generation)
		{
			return _entries.TryGetValue(key, out var entry) && entry.Generation == generation;
		}

		/// <summary>Copy of the entries, for building the next manifest.</summary>
		internal Dictionary<string, Entry> CopyEntries() => new(_entries);

		/// <summary>Body + SHA-256 of the body, so a truncated/garbled manifest is detected.</summary>
		internal byte[] Serialize()
		{
			var w = new SaveWriter();
			w.AddBytes(Encoding.ASCII.GetBytes(MAGIC));
			w.AddByte(VERSION);
			w.AddLong(Generation);
			w.AddInt(_entries.Count);

			foreach (var kv in _entries)
			{
				w.AddString(kv.Key);
				w.AddLong(kv.Value.Generation);
				w.AddBytes(kv.Value.Hash);
			}

			var body = w.ToArray();
			var hash = SaveFileStore.Hash(body);

			var result = new byte[body.Length + HASH_SIZE];
			Buffer.BlockCopy(body, 0, result, 0, body.Length);
			Buffer.BlockCopy(hash, 0, result, body.Length, HASH_SIZE);
			return result;
		}

		/// <summary>Parse manifest bytes. Throws InvalidDataException if broken.</summary>
		internal static SaveManifest Deserialize(byte[] bytes)
		{
			if (bytes.Length < HASH_SIZE + MAGIC.Length)
				throw new InvalidDataException("Manifest too short");

			var bodyLength = bytes.Length - HASH_SIZE;
			var body = new byte[bodyLength];
			var storedHash = new byte[HASH_SIZE];
			Buffer.BlockCopy(bytes, 0, body, 0, bodyLength);
			Buffer.BlockCopy(bytes, bodyLength, storedHash, 0, HASH_SIZE);

			if (!SaveFileStore.HashEquals(SaveFileStore.Hash(body), storedHash))
				throw new InvalidDataException("Manifest checksum mismatch");

			try
			{
				var r = new SaveReader(body);
				if (Encoding.ASCII.GetString(r.ReadBytes(MAGIC.Length)) != MAGIC)
					throw new InvalidDataException("Invalid manifest");
				if (r.ReadByte() != VERSION)
					throw new InvalidDataException("Unsupported manifest version");

				var generation = r.ReadLong();
				var count = r.ReadInt();
				var entries = new Dictionary<string, Entry>(count);

				for (int i = 0; i < count; i++)
				{
					var key = r.ReadString();
					var entryGeneration = r.ReadLong();
					var hash = r.ReadBytes(HASH_SIZE);
					entries[key] = new Entry(entryGeneration, hash);
				}

				return new SaveManifest(generation, entries);
			}
			catch (Exception e) when (!(e is InvalidDataException))
			{
				throw new InvalidDataException("Manifest is corrupt", e);
			}
		}
	}
}
