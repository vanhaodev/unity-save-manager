using System;

namespace vanhaodev.savemanager.Tests
{
	// Game data as it looked at each schema version.
	internal sealed class PlayerV0 : ISaveable
	{
		public string Name, Image;
		public void WriteSave(SaveWriter w) { w.AddString(Name); w.AddString(Image); }
		public void ReadSave(SaveReader r) { Name = r.ReadString(); Image = r.ReadString(); }
	}

	internal sealed class PlayerV1 : ISaveable
	{
		public string Name, Image;
		public int Level;
		public void WriteSave(SaveWriter w) { w.AddString(Name); w.AddString(Image); w.AddInt(Level); }
		public void ReadSave(SaveReader r) { Name = r.ReadString(); Image = r.ReadString(); Level = r.ReadInt(); }
	}

	internal sealed class PlayerData : ISaveable
	{
		public string Name, Avatar;
		public int Level, Gold;
		public void WriteSave(SaveWriter w) { w.AddString(Name); w.AddString(Avatar); w.AddInt(Level); w.AddInt(Gold); }
		public void ReadSave(SaveReader r) { Name = r.ReadString(); Avatar = r.ReadString(); Level = r.ReadInt(); Gold = r.ReadInt(); }
	}

	internal sealed class Coins : ISaveable
	{
		public int Value;
		public void WriteSave(SaveWriter w) => w.AddInt(Value);
		public void ReadSave(SaveReader r) => Value = r.ReadInt();
	}

	// 0 -> 1: player gets Level 1 (read / written field by field).
	internal sealed class MigrationsV1 : ISaveMigrationSet
	{
		public static int Created;
		public MigrationsV1() { Created++; }
		public void Register(SaveMigrations m) => m.Add(from: 0, AddLevel);

		private void AddLevel(SaveMigrationContext ctx)
		{
			var (name, image) = ctx.Read("player", r => (r.ReadString(), r.ReadString()));
			ctx.Write("player", w => { w.AddString(name); w.AddString(image); w.AddInt(1); });
		}
	}

	// 1 -> 2: coins read and removed; 2 -> 3: player rebuilt with Gold, settings renamed.
	internal sealed class MigrationsV2 : ISaveMigrationSet
	{
		private int _coins;

		public void Register(SaveMigrations m)
		{
			m.Add(from: 1, ReadCoins);
			m.Add(from: 2, Merge);
		}

		private void ReadCoins(SaveMigrationContext ctx)
		{
			_coins = ctx.Exists("coins") ? ctx.Read("coins", r => r.ReadInt()) : 0;
			ctx.Delete("coins");
		}

		private void Merge(SaveMigrationContext ctx)
		{
			var p = ctx.Get<PlayerV1>("player");
			ctx.Set("player", new PlayerData { Name = p.Name, Avatar = p.Image, Level = p.Level, Gold = _coins });
			if (ctx.Exists("settings"))
				ctx.Rename("settings", "options");
		}
	}

	internal sealed class FailingAt1 : ISaveMigrationSet
	{
		public void Register(SaveMigrations m)
		{
			m.Add(1, ctx => throw new InvalidOperationException("boom at 1"));
			m.Add(2, ctx => { });
		}
	}

	internal sealed class JumpFrom0 : ISaveMigrationSet
	{
		public void Register(SaveMigrations m) => m.Add(from: 0, to: 3, ctx => ctx.Delete("coins"));
	}

	internal sealed class GapSet : ISaveMigrationSet
	{
		public void Register(SaveMigrations m) { m.Add(0, ctx => { }); m.Add(2, ctx => { }); }
	}

	internal sealed class TestSetup : ISaveSetup
	{
		public int? Version;
		public Action<SaveConfig> Extra;

		public void Configure(SaveConfig c)
		{
			c.SetKey("secret-test");
			c.SchemaVersion = Version;
			Extra?.Invoke(c);
		}
	}
}
