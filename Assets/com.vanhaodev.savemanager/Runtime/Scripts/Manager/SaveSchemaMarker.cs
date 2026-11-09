using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace vanhaodev.savemanager
{
	/// <summary>
	/// schema.dat: the schema version this library last committed, signed like the manifest.<br/>
	/// Library 1.1.0 and older neither read nor delete it, but they drop the schema number
	/// at the end of the manifest when they save. So "manifest without schema + marker present"
	/// means an older build rewrote the save, and the marker still tells its real version.<br/>
	/// Only read when the manifest has no schema number (never on the normal load path).
	/// Callers hold <see cref="SaveFileStore.IoLock"/>.
	/// </summary>
	internal static class SaveSchemaMarker
	{
		private const string MAGIC = "SSCH";
		private const byte VERSION = 1;
		private const int HASH_SIZE = 32;
		private const int BODY_SIZE = 4 + 1 + 4; // MAGIC + VERSION + schema

		/// <summary>Schema in the marker, or null if missing / broken / signed with an unknown key.</summary>
		internal static int? TryRead()
		{
			try
			{
				var path = SaveFileStore.SchemaMarkerPath;
				if (!File.Exists(path))
					return null;

				var bytes = File.ReadAllBytes(path);
				if (bytes.Length != BODY_SIZE + HASH_SIZE)
					return null;

				var body = new byte[BODY_SIZE];
				var stored = new byte[HASH_SIZE];
				Buffer.BlockCopy(bytes, 0, body, 0, BODY_SIZE);
				Buffer.BlockCopy(bytes, BODY_SIZE, stored, 0, HASH_SIZE);

				if (!IsSigned(body, stored))
					return null;

				var r = new SaveReader(body);
				if (Encoding.ASCII.GetString(r.ReadBytes(MAGIC.Length)) != MAGIC || r.ReadByte() != VERSION)
					return null;

				return r.ReadInt();
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>Best effort: a missing marker only weakens old-build detection, never breaks saving.</summary>
		internal static bool TryWrite(int schemaVersion)
		{
			try
			{
				var w = new SaveWriter();
				w.AddBytes(Encoding.ASCII.GetBytes(MAGIC));
				w.AddByte(VERSION);
				w.AddInt(schemaVersion);

				var body = w.ToArray();
				var result = new byte[BODY_SIZE + HASH_SIZE];
				Buffer.BlockCopy(body, 0, result, 0, BODY_SIZE);
				Buffer.BlockCopy(SaveEncryption.Sign(body), 0, result, BODY_SIZE, HASH_SIZE);

				// Plain replace (no .bak kept): a crash in between only leaves the marker missing.
				var path = SaveFileStore.SchemaMarkerPath;
				var tmp = SaveFileStore.TmpPath(path);
				SaveFileStore.WriteDurable(tmp, result);
				if (File.Exists(path))
					File.Delete(path);
				File.Move(tmp, path);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[SaveManager] Could not write {Path.GetFileName(SaveFileStore.SchemaMarkerPath)}: {e.Message}");
				return false;
			}
		}

		// Any SetKey secret (current or previous) is fine: the marker may predate a key change.
		private static bool IsSigned(byte[] body, byte[] stored)
		{
			foreach (var keys in SaveEncryption.AcceptedKeys)
			{
				if (SaveEncryption.SignatureEquals(SaveEncryption.Sign(body, keys), stored))
					return true;
			}

			return false;
		}
	}
}
