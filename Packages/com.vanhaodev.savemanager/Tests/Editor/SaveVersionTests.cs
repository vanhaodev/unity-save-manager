using System;
using NUnit.Framework;

namespace vanhaodev.savemanager.Tests
{
	public sealed class SaveVersionTests : SaveTestBase
	{
		[Test]
		public void NewerSave_BlockedUntilAllowWrite()
		{
			Setup(5);
			Save.Set("player", new PlayerData { Name = "new" });
			int newer = 0;
			Setup(3, Full);
			Save.NewerSaveDetected += e => newer++;

			Assert.AreEqual(SaveMigrationResult.NewerSave, Save.Migrate());
			Assert.AreEqual(1, newer);
			Assert.IsTrue(Save.IsWriteBlocked);
			Assert.IsTrue(Save.SetAsync("player", new PlayerData()).IsFaulted);
			Assert.AreEqual("new", Save.Get<PlayerData>("player").Name, "reading still works");
			Assert.DoesNotThrow(Save.Flush, "nothing pending: quitting must not throw");

			Save.AllowWrite();
			Save.Set("player", new PlayerData { Name = "old-build" });
			Assert.IsFalse(Save.IsWriteBlocked);
			Assert.AreEqual("old-build", Save.Get<PlayerData>("player").Name);
		}

		[Test]
		public void NewerSave_WriteInsideHookStillBlocked()
		{
			Setup(5);
			Save.Set("player", new PlayerData { Name = "new" });
			var threw = false;
			Setup(3, Full);
			Save.NewerSaveDetected += e =>
			{
				try { Save.Set("player", new PlayerData()); }
				catch (InvalidOperationException) { threw = true; }
			};

			Save.Migrate();
			Assert.IsTrue(threw);
		}

		[Test]
		public void Library110Save_ReadAsSchema0_AndMigrates()
		{
			MakeV0Save();
			StripSchemaFromManifest(keepMarker: false);
			Setup(3, Full);

			Assert.AreEqual(0, Save.SaveSchemaVersion);
			Assert.AreEqual(SaveMigrationResult.Migrated, Save.Migrate());
			Assert.AreEqual(250, Save.Get<PlayerData>("player").Gold);
		}

		[Test]
		public void OldBuildRewrite_DetectedWithMarkerVersion()
		{
			Setup(3);
			Save.Set("player", new PlayerData { Name = "x" });
			StripSchemaFromManifest(keepMarker: true);
			int touched = 0;
			Setup(3);
			Save.SaveTouchedByOlderVersion += e => touched++;

			Assert.AreEqual(3, Save.SaveSchemaVersion);
			Assert.AreEqual(1, touched);
			Assert.AreEqual(SaveMigrationResult.UpToDate, Save.Migrate());
		}

		[Test]
		public void HookCallingSave_SeesMigratedData()
		{
			Setup(1);
			Save.Set("player", new PlayerV1 { Name = "hook", Image = "a", Level = 4 });
			Save.Set("coins", new Coins { Value = 9 });
			StripSchemaFromManifest(keepMarker: true);
			var gold = -1;
			Setup(3, c => c.AddMigrations<MigrationsV2>());
			Save.SaveTouchedByOlderVersion += e => gold = Save.Get<PlayerData>("player").Gold;

			Assert.AreEqual(3, Save.SaveSchemaVersion);
			Assert.AreEqual(9, gold);
		}

		[Test]
		public void EmptyOldSave_CountsAsNew()
		{
			Setup(0);
			Save.Set("tmp", new Coins());
			Save.Delete("tmp");
			StripSchemaFromManifest(keepMarker: false);
			Setup(3, c => c.AddMigrations<MigrationsV2>()); // no step from 0

			Assert.AreEqual(SaveMigrationResult.UpToDate, Save.Migrate());
			Assert.IsFalse(Save.IsWriteBlocked);
		}

		[Test]
		public void SetupWithoutVersion_KeepsSchemaVersion()
		{
			Relaunch();
			Save.SchemaVersion = 7;
			Save.Setup(new TestSetup { Version = null });
			Assert.AreEqual(7, Save.SchemaVersion);
		}
	}
}
