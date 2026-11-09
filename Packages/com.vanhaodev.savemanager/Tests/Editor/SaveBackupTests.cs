using System.IO;
using System.Linq;
using NUnit.Framework;

namespace vanhaodev.savemanager.Tests
{
	public sealed class SaveBackupTests : SaveTestBase
	{
		[Test]
		public void Migration_BacksUpOldSave_RestoreBringsItBack()
		{
			MakeV0Save();
			Setup(3, Full);
			Save.Migrate();

			var backups = Save.ListBackups();
			Assert.AreEqual(1, backups.Count);
			Assert.AreEqual(0, backups[0].SchemaVersion);

			Save.RestoreBackup(backups[0]);
			Assert.AreEqual(0, Save.SaveSchemaVersion);
			Assert.AreEqual(2, Save.ListBackups().Count, "the replaced save is backed up too");

			Assert.AreEqual(SaveMigrationResult.Migrated, Save.Migrate());
			Assert.AreEqual(250, Save.Get<PlayerData>("player").Gold);
		}

		[Test]
		public void KeepsNewestThree()
		{
			MakeV0Save();
			Setup(3, Full);
			Save.Migrate();
			for (int i = 0; i < 3; i++)
			{
				var all = Save.ListBackups();
				Save.RestoreBackup(all[all.Count - 1]);
				Save.Migrate();
			}

			Assert.AreEqual(3, Save.ListBackups().Count);
		}

		[Test]
		public void EditedBackup_Refused_SaveUnchanged()
		{
			MakeV0Save();
			Setup(3, Full);
			Save.Migrate();
			var backup = Save.ListBackups()[0];
			var file = Directory.GetFiles(backup.Path, "*.sav")[0];
			var bytes = File.ReadAllBytes(file);
			bytes[bytes.Length - 1] ^= 0xFF;
			File.WriteAllBytes(file, bytes);
			var before = Hashes();

			Assert.Throws<InvalidDataException>(() => Save.RestoreBackup(backup));
			Assert.AreEqual(before, Hashes());
		}

		[Test]
		public void BackupImpossible_NoMigration()
		{
			MakeV0Save();
			File.WriteAllText(Path.Combine(Folder, "backups"), "not a folder");
			var before = Hashes();
			Setup(3, Full);

			ExpectMigrationErrorLog();
			Assert.AreEqual(SaveMigrationResult.Failed, Save.Migrate());
			Assert.AreEqual(before, Hashes());
		}

		[Test]
		public void StartFresh_OnTooOld_EmptySaveAtGameVersion()
		{
			MakeV0Save();
			Setup(3, c => c.AddMigrations<MigrationsV2>());
			Save.SaveTooOld += e => Save.StartFresh();

			Assert.AreEqual(SaveMigrationResult.TooOld, Save.Migrate());
			Assert.IsFalse(Save.IsWriteBlocked);
			Assert.IsFalse(Save.Exists("player"));
			Assert.AreEqual(3, Save.SaveSchemaVersion);
			Assert.AreEqual(0, Save.ListBackups()[0].SchemaVersion, "old save kept in backups");

			Save.Set("player", new PlayerData { Name = "fresh" });
			Setup(3, c => c.AddMigrations<MigrationsV2>());
			Assert.AreEqual(SaveMigrationResult.UpToDate, Save.Migrate());
			Assert.AreEqual("fresh", Save.Get<PlayerData>("player").Name);
		}

		[Test]
		public void AllowWrite_BacksUpFirst()
		{
			Setup(5);
			Save.Set("player", new PlayerData { Name = "new" });
			Setup(3, Full);
			Save.NewerSaveDetected += e => e.AllowWrite();
			Save.Migrate();

			Assert.IsFalse(Save.IsWriteBlocked);
			Assert.AreEqual(1, Save.ListBackups().Count);
			Assert.AreEqual(5, Save.ListBackups()[0].SchemaVersion);
		}

		[Test]
		public void ClockSetBack_NewBackupIsNotRotatedAway()
		{
			MakeV0Save();
			// Backups "from the future": the new one sorts as the oldest.
			for (int i = 1; i <= 3; i++)
				Directory.CreateDirectory(Path.Combine(Folder, "backups", $"2999010{i}-000000-000-schema-9"));
			Setup(3, Full);

			Assert.AreEqual(SaveMigrationResult.Migrated, Save.Migrate());
			var backups = Save.ListBackups();
			Assert.AreEqual(3, backups.Count);
			Assert.IsTrue(backups.Any(b => b.SchemaVersion == 0 && File.Exists(Path.Combine(b.Path, "manifest.dat"))));
		}

		[Test]
		public void DataFileLostAfterLoad_StartFreshStillWorks()
		{
			MakeV0Save();
			Setup(0);
			Assert.IsTrue(Save.Exists("coins")); // loaded
			foreach (var f in Directory.GetFiles(Folder, "coins.*.sav"))
				File.Delete(f);

			Assert.DoesNotThrow(Save.StartFresh);
			Assert.IsFalse(Save.Exists("player"));
			Assert.AreEqual(1, Save.ListBackups().Count, "backup holds what was still readable");
		}

		[Test]
		public void Snapshot_NeverLoadsTheSave_AndRefusesTheSaveFolder()
		{
			MakeV0Save();
			Setup(0);
			Assert.IsNull(SaveMigrationSimulator.CaptureCurrentSave(Path.Combine(Path.GetDirectoryName(Folder), "snap")), "not loaded yet");
			Assert.AreEqual(0, Save.SaveSchemaVersion);
			Assert.Throws<System.ArgumentException>(() => SaveMigrationSimulator.CaptureCurrentSave(Folder));
			Assert.Throws<System.ArgumentException>(() => SaveMigrationSimulator.CaptureCurrentSave(Path.GetDirectoryName(Folder)));
			Assert.Throws<System.ArgumentException>(() => SaveMigrationSimulator.CaptureCurrentSave(Path.Combine(Folder, "x")));
			Assert.IsTrue(Save.Exists("player"));
		}
	}
}
