using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace vanhaodev.savemanager.Tests
{
	/// <summary>
	/// Every test runs on its own temp save folder (never the real save) with hooks delivered
	/// inline, and "relaunches" the game by resetting the library's static state.
	/// </summary>
	public abstract class SaveTestBase
	{
		private string _root;
		private SynchronizationContext _context;

		protected static readonly Action<SaveConfig> Full = c =>
		{
			c.AddMigrations<MigrationsV1>();
			c.AddMigrations<MigrationsV2>();
		};

		protected string Folder => Path.Combine(_root, "SaveData");

		[SetUp]
		public void SetUpFolder()
		{
			_context = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext(null); // hooks run inline
			_root = Path.Combine(Path.GetTempPath(), "savemanager-tests-" + Guid.NewGuid().ToString("N"));
			SaveFileStore.UseFolder(Path.Combine(_root, "SaveData"));
			Save.ResetStatics();
		}

		[TearDown]
		public void TearDownFolder()
		{
			Save.ResetStatics();
			SaveFileStore.UseFolder(null);
			SynchronizationContext.SetSynchronizationContext(_context);
			if (Directory.Exists(_root))
				Directory.Delete(_root, true);
		}

		protected static void Relaunch() => Save.ResetStatics();

		protected static void Setup(int? version, Action<SaveConfig> extra = null)
		{
			Relaunch();
			Save.Setup(new TestSetup { Version = version, Extra = extra });
		}

		/// <summary>A save made by an older game: schema 0, player V0 + coins + settings.</summary>
		protected static void MakeV0Save()
		{
			Setup(0);
			Save.Set("player", new PlayerV0 { Name = "hao", Image = "cat.png" });
			Save.Set("coins", new Coins { Value = 250 });
			Save.Set("settings", new Coins { Value = 7 });
			Relaunch();
		}

		/// <summary>Rewrite manifest.dat like library 1.1.0 would (no schema number at the end).</summary>
		protected void StripSchemaFromManifest(bool keepMarker)
		{
			var path = Path.Combine(Folder, SaveFileStore.ManifestFileName);
			using (SaveEncryption.UseTemporarily("secret-test", null))
			{
				var m = SaveManifest.Deserialize(File.ReadAllBytes(path), out _);
				File.WriteAllBytes(path, new SaveManifest(m.Generation + 1, m.CopyEntries(), null).Serialize());
			}

			if (!keepMarker)
				File.Delete(SaveFileStore.SchemaMarkerPath);
			Relaunch();
		}

		/// <summary>
		/// A failed migration logs an error on purpose; Unity's test runner fails a test on an
		/// unexpected error log, so tests that make a migration fail declare it first.
		/// </summary>
		protected static void ExpectMigrationErrorLog()
		{
#if UNITY_5_3_OR_NEWER
			UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
				new System.Text.RegularExpressions.Regex(@"\[SaveManager\] Save migration .* failed"));
#endif
		}

		/// <summary>Fingerprint of every file in the save folder (not backups).</summary>
		protected string Hashes()
		{
			var parts = new List<string>();
			foreach (var f in Directory.GetFiles(Folder))
				parts.Add(Path.GetFileName(f) + ":" + BitConverter.ToString(SaveFileStore.Hash(File.ReadAllBytes(f))));
			parts.Sort(StringComparer.Ordinal);
			return string.Join("|", parts);
		}
	}
}
