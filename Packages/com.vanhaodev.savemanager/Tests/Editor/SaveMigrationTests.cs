using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace vanhaodev.savemanager.Tests
{
	public sealed class SaveMigrationTests : SaveTestBase
	{
		[Test]
		public void Chain_RunsEveryStepInOrder_AndSavesOnce()
		{
			MakeV0Save();
			Setup(3, Full);
			int started = 0, steps = 0, completed = 0;
			Save.MigrationStarted += (a, b) => started++;
			Save.MigrationStepCompleted += (a, b, p) => steps++;
			Save.MigrationCompleted += (a, b) => completed++;

			Assert.AreEqual(SaveMigrationResult.Migrated, Save.Migrate());
			Assert.AreEqual((1, 3, 1), (started, steps, completed));

			var p = Save.Get<PlayerData>("player");
			Assert.AreEqual(("hao", "cat.png", 1, 250), (p.Name, p.Avatar, p.Level, p.Gold));
			Assert.IsFalse(Save.Exists("coins"));
			Assert.IsFalse(Save.Exists("settings"));
			Assert.AreEqual(7, Save.Get<Coins>("options").Value);
			Assert.AreEqual(3, Save.SaveSchemaVersion);
		}

		[Test]
		public void UpToDate_CreatesNothing_AndAsyncCompletesAtOnce()
		{
			MakeV0Save();
			Setup(3, Full);
			Save.Migrate();

			Setup(3, Full);
			MigrationsV1.Created = 0; // Setup checked the chain (Editor)
			Assert.AreEqual(SaveMigrationResult.UpToDate, Save.Migrate());
			Assert.AreEqual(0, MigrationsV1.Created);
			Assert.IsTrue(Save.MigrateAsync().IsCompleted);
		}

		[Test]
		public void StepThrows_SaveUnchanged_WritesBlocked()
		{
			MakeV0Save();
			var before = Hashes();
			SaveMigrationFailure failure = null;
			Setup(3, c => { c.AddMigrations<MigrationsV1>(); c.AddMigrations<FailingAt1>(); });
			Save.MigrationFailed += f => failure = f;
			ExpectMigrationErrorLog();

			Assert.AreEqual(SaveMigrationResult.Failed, Save.Migrate());
			Assert.AreEqual(1, failure?.StepFrom);
			Assert.AreEqual(before, Hashes());
			Assert.IsTrue(Save.IsWriteBlocked);
			Assert.Throws<InvalidOperationException>(() => Save.Set("x", new Coins()));
			Assert.AreEqual("cat.png", Save.Get<PlayerV0>("player").Image);

			Relaunch();
			Assert.AreEqual(0, Save.SaveSchemaVersion);
		}

		[Test]
		public void VerifyThrows_SaveUnchanged()
		{
			MakeV0Save();
			var before = Hashes();
			Setup(3, c =>
			{
				Full(c);
				c.Verify(ctx => { if (ctx.Get<PlayerData>("player").Gold < 1000) throw new Exception("gold too low"); });
			});

			ExpectMigrationErrorLog();
			Assert.AreEqual(SaveMigrationResult.Failed, Save.Migrate());
			Assert.AreEqual(before, Hashes());
		}

		[Test]
		public void ForgottenMigrate_FirstSaveCallMigrates()
		{
			MakeV0Save();
			Setup(3, Full);
			Assert.AreEqual(250, Save.Get<PlayerData>("player").Gold);
		}

		[TestCase(SaveMigrationThread.MainThread)]
		[TestCase(SaveMigrationThread.Background)]
		public void MigrateAsync_BothModes(SaveMigrationThread mode)
		{
			MakeV0Save();
			Setup(3, Full);
			var progress = new List<float>();

			var result = Save.MigrateAsync(progress.Add, mode).GetAwaiter().GetResult();

			Assert.AreEqual(SaveMigrationResult.Migrated, result);
			Assert.AreEqual(3, progress.Count);
			Assert.AreEqual(1f, progress[2], 1e-6f);
			Assert.AreEqual(250, Save.Get<PlayerData>("player").Gold);
		}

		[Test]
		public void SaveCallDuringMigrateAsync_Throws()
		{
			MakeV0Save();
			var gate = new System.Threading.ManualResetEventSlim();
			Setup(4, c => { Full(c); c.AddMigrations<WaitAt3>(); });
			WaitAt3.Gate = gate;

			var task = Save.MigrateAsync(null, SaveMigrationThread.Background);
			Assert.Throws<InvalidOperationException>(() => Save.Get<PlayerData>("player"));
			gate.Set();
			Assert.AreEqual(SaveMigrationResult.Migrated, task.GetAwaiter().GetResult());
		}

		[Test]
		public void TooOld_HookAndBlocked()
		{
			MakeV0Save();
			SaveVersionEvent tooOld = null;
			Setup(3, c => c.AddMigrations<MigrationsV2>()); // steps start at 1
			Save.SaveTooOld += e => tooOld = e;

			Assert.AreEqual(SaveMigrationResult.TooOld, Save.Migrate());
			Assert.AreEqual(1, tooOld?.OldestSupportedVersion);
			Assert.IsTrue(Save.IsWriteBlocked);
		}

		[Test]
		public void JumpStep_GoesStraightToTarget()
		{
			MakeV0Save();
			Setup(3, c => c.AddMigrations<JumpFrom0>());
			Assert.AreEqual(SaveMigrationResult.Migrated, Save.Migrate());
			Assert.IsFalse(Save.Exists("coins"));
			Assert.AreEqual(3, Save.SaveSchemaVersion);
		}

		[Test]
		public void BrokenChain_RejectedAtSetup()
		{
			Assert.Throws<ArgumentException>(() => Setup(3, c => c.AddMigrations<GapSet>()), "gap");
			Assert.Throws<ArgumentException>(() => Setup(3, c => { c.AddMigrations<MigrationsV1>(); c.AddMigrations<JumpFrom0>(); }), "duplicate");
			Assert.Throws<ArgumentException>(() => Setup(2, c => c.AddMigrations<JumpFrom0>()), "past SchemaVersion");
		}

		private sealed class WaitAt3 : ISaveMigrationSet
		{
			public static System.Threading.ManualResetEventSlim Gate;
			public void Register(SaveMigrations m) => m.Add(3, ctx => Gate.Wait(5000));
		}
	}
}
