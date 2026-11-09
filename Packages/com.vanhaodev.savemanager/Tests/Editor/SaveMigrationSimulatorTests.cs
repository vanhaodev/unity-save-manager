using System.IO;
using System.Linq;
using NUnit.Framework;

namespace vanhaodev.savemanager.Tests
{
	public sealed class SaveMigrationSimulatorTests : SaveTestBase
	{
		private string SnapshotV0()
		{
			MakeV0Save();
			Setup(0);
			Assert.AreEqual(0, Save.SaveSchemaVersion); // loads the save (capture never loads it itself)
			var snapshot = Path.Combine(Path.GetDirectoryName(Folder), "snapshot-0");
			Assert.AreEqual(0, SaveMigrationSimulator.CaptureCurrentSave(snapshot));
			Relaunch();
			return snapshot;
		}

		[Test]
		public void Simulation_Migrates_WithoutTouchingTheSave()
		{
			var snapshot = SnapshotV0();
			var before = Hashes();

			var sim = SaveMigrationSimulator.Run(new TestSetup { Version = 3, Extra = Full }, snapshot);

			Assert.AreEqual(SaveMigrationResult.Migrated, sim.Result, sim.ToString());
			Assert.AreEqual(3, sim.Steps.Count);
			Assert.IsTrue(sim.Steps.All(s => s.Error == null));
			CollectionAssert.AreEquivalent(new[] { "coins", "options", "player", "settings" }, sim.Changes.Select(c => c.Key));
			Assert.AreEqual(before, Hashes(), "the game's save is untouched");
		}

		[Test]
		public void Simulation_NamesTheFailingStep()
		{
			var snapshot = SnapshotV0();
			var sim = SaveMigrationSimulator.Run(new TestSetup
			{
				Version = 3,
				Extra = c => { c.AddMigrations<MigrationsV1>(); c.AddMigrations<FailingAt1>(); }
			}, snapshot);

			Assert.AreEqual(SaveMigrationResult.Failed, sim.Result);
			Assert.AreEqual(2, sim.Steps.Count);
			Assert.IsNull(sim.Steps[0].Error);
			Assert.AreEqual(1, sim.Steps[1].From);
			Assert.IsNotNull(sim.Steps[1].Error);
		}

		[Test]
		public void Simulation_VerifyAndExport()
		{
			var snapshot = SnapshotV0();
			var setup = new TestSetup
			{
				Version = 3,
				Extra = c => { Full(c); c.Verify(ctx => Assert.AreEqual(250, ctx.Get<PlayerData>("player").Gold)); }
			};

			var sim = SaveMigrationSimulator.Run(setup, snapshot);
			Assert.IsTrue(sim.VerifyPassed);

			var exported = Path.Combine(Path.GetDirectoryName(Folder), "exported");
			sim.ExportTo(exported);
			var again = SaveMigrationSimulator.Run(setup, exported);
			Assert.AreEqual(SaveMigrationResult.UpToDate, again.Result, "exported save is valid and already migrated");
		}

		[Test]
		public void Simulation_WrongSecret_Fails()
		{
			var snapshot = SnapshotV0();
			var sim = SaveMigrationSimulator.Run(new WrongKeySetup(), snapshot);
			Assert.AreEqual(SaveMigrationResult.Failed, sim.Result);
			Assert.IsInstanceOf<InvalidDataException>(sim.Error);
		}

		private sealed class WrongKeySetup : ISaveSetup
		{
			public void Configure(SaveConfig c)
			{
				c.SetKey("not-the-secret");
				c.SchemaVersion = 3;
			}
		}
	}
}
