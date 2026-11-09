# Unity Save Manager

**v1.2.0** · Unity 6 (6000.0+)

Fast binary save system for Unity: crash-safe, consistent across files, encrypted and signed by default.

- **Crash-safe** — a crash or power loss while saving never corrupts or mixes saves; the last complete save is used
- **Consistent** — all keys always load from the same save; group related data with transactions
- **Fast** — saves in the background, only changed keys are written
- **Secure by default** — AES-encrypted and signed; edited, swapped or broken files are detected
- **Awaitable** — `await Save.SetAsync(...)` when the data must be on disk (e.g. in-app purchases)
- **Hooks** — events for saving / saved / failed / tampered, delivered on the main thread
- **Save migration** — upgrade old saves after a game update, step by step, all-or-nothing, with automatic backups and an Editor simulator
- **WebGL ready** — no threads needed, saves are spread over frames

## Install

Add to `Packages/manifest.json`:

```json
"com.vanhaodev.encryptionmanager": "https://github.com/vanhaodev/unity-encryption-manager.git?path=Exported/com.vanhaodev.encryptionmanager#1.1.0",
"com.vanhaodev.savemanager": "https://github.com/vanhaodev/unity-save-manager.git?path=Packages/com.vanhaodev.savemanager#1.2.0"
```

> Up to 1.1.0 the package lived in `Exported/` (`?path=Exported/com.vanhaodev.savemanager#1.1.0` still works). From 1.2.0 use `?path=Packages/...`.

Or download a release: [Latest Release](https://github.com/vanhaodev/unity-save-manager/releases/latest) · [All Releases](https://github.com/vanhaodev/unity-save-manager/releases)

---

## Quick Start

```csharp
using UnityEngine;
using vanhaodev.savemanager;

// Data model
public class UserModel : ISaveable
{
    public string Name;

    public void WriteSave(SaveWriter w) => w.AddString(Name);
    public void ReadSave(SaveReader r) => Name = r.ReadString();
}

public class Example : MonoBehaviour
{
    void Awake()
    {
        // Once at game start, before any Save call (optional, see "Security")
        SaveEncryption.SetKey("my-secret");
    }

    void SaveAndLoad()
    {
        // Save in background (no lag)
        Save.SetAsync("user", new UserModel { Name = "vanhaodev" });

        // ... later: load
        var loaded = Save.Get<UserModel>("user");
        Debug.Log(loaded?.Name); // "vanhaodev"
    }

    // Write everything still pending when the app goes away
    void OnApplicationPause(bool paused) { if (paused) Save.Flush(); }
    void OnApplicationQuit() => Save.Flush();
}
```

| Want to... | Use |
|------------|-----|
| Save during normal gameplay | `Save.SetAsync(key, data)` |
| Save now and be sure it is on disk (checkpoint) | `Save.Set(key, data)` |
| Save several keys together, all or nothing | `Save.BeginTransaction()` |
| Wait until saved without freezing (purchase) | `await Save.SetAsync(key, data)` |
| Write everything pending (quit / pause) | `Save.Flush()` / `await Save.FlushAsync()` |
| Load | `Save.Get<T>(key)` or `Save.TryGet(key, out T data)` |

Data still waiting in the background queue is not visible to `Get` until it is written — `await` the save (or use `Set`) if you need to read it back right away.

---

## Consistent, Crash-Safe Saves

All keys always load from **the same save**. A crash or error while saving never leaves a mix of old and new data:

1. New data is written to new files (`money.43.sav`); old files are not touched
2. When every file is fully on disk, the save index (`manifest.dat`) switches to the new files in one step — the **commit point**
3. Old files are deleted

If the game crashes before step 2, the next load uses the previous complete save. Only changed keys are written, so saving stays fast.

If writing fails while the game runs (disk full, file locked...), the save on disk stays intact; the data is kept and written again on the next `SetAsync` / `Set` / `Flush`.

### Transactions: save related data together

Data that must stay in sync (quest reward + money) should be saved in one transaction, like a SQL transaction:

```csharp
using (var tx = Save.BeginTransaction())
{
    Save.SetAsync("quest", quest);
    Save.SetAsync("money", money);
    tx.Commit();
}   // both are written together, or not at all
```

- Every `SetAsync` inside the block joins the transaction, even when called from other classes.
- If the block ends without `Commit()` (exception, early return, forgotten), **nothing** inside is saved and a warning is logged. The save on disk stays as it was. Your in-memory game data is not rolled back.
- Nested transactions join the outer one. Do not `await` inside a transaction.

### Wait until saved (`await`)

`SetAsync` returns a `Task`. Await it when the next step must only happen once the data is on disk — e.g. confirming an in-app purchase. The game does not freeze while waiting, and code after `await` runs on the main thread.

```csharp
async void OnPurchaseCompleted(Order order)
{
    player.Gems += 100;
    try
    {
        await Save.SetAsync("player", player);
        store.ConfirmPurchase(order);          // only after the save is on disk
    }
    catch (Exception)
    {
        // Save failed: don't confirm, the store will deliver the purchase again
    }
}

// Several keys, then wait for all of them
Save.SetAsync("player", player);
Save.SetAsync("inventory", inventory);
await Save.FlushAsync();
```

- Not awaiting is fine too.
- Inside a transaction, the task completes when the whole transaction is saved, and is **canceled** if the transaction is discarded (no `Commit()`).
- Works inside `async Awaitable` methods as well.

### New player or broken save? (`TryGet`)

`Get` returns `null` for "never saved" and throws when a save is broken. `TryGet` tells them apart without exceptions, so a broken save is never mistaken for a new player and overwritten:

```csharp
switch (Save.TryGet("player", out PlayerData player))
{
    case SaveLoadStatus.Ok:              break;                            // loaded
    case SaveLoadStatus.NotFound:        player = new PlayerData(); break; // new player
    case SaveLoadStatus.Corrupt:         ShowSaveProblem(); break;         // broken / edited / data class changed
    case SaveLoadStatus.WrongEncryption: ShowSaveProblem(); break;         // not stored with the required encryption
}
```

---

## Security

Saves are **always signed** and **AES-encrypted by default**. Editing any save file — even recomputing fingerprints — is detected: the previous valid save is used and `Save.IntegrityFailed` is raised.

`SaveEncryption.SetKey(secret)` turns any string into your own two strong keys (one encrypts, one signs). Call it once at game start, **before any Save call**.

> **No `SetKey`?** A built-in default key is used. Saves still work, are encrypted and signed, and can be copied between devices — but the default key is public (it is in this library's source), so anyone who reads it can decrypt and re-sign saves. Call `SetKey` for real protection.

To store a key without encryption (still signed), pass `EncryptionType.None`. To reject a file that is not stored the way you expect, pass the required type when loading: `Save.Get<T>(key, EncryptionType.AES)`.

**Shareable or not — you choose with the secret:**

| Secret | Result |
|--------|--------|
| Same on every device (e.g. built from constants in your code) | Save files can be copied between devices / players |
| Per player (e.g. hash of the account id) | A save only loads for its owner; copied saves are rejected |

> Don't use a device id as the secret: the player loses the save when changing phones.

### Changing the secret in an update (players keep their saves)

Keep the secrets of older versions in the update. Saves made with an old secret load once and are moved to the new one automatically:

```csharp
// v1 shipped WITHOUT SetKey  ->  v2 adds a secret:
SaveEncryption.SetKey("secret-v2", SaveEncryption.DefaultKey);

// v3 changes the secret again (players may jump straight from v1 to v3):
SaveEncryption.SetKey("secret-v3", "secret-v2", SaveEncryption.DefaultKey);
```

- Current secret first, then older ones. A player who skipped versions still loads, as long as their secret is listed.
- After a save is moved, the old secret is no longer needed for it.
- **Security:** while an old secret is listed, a save signed with it is still accepted. For `DefaultKey` (public) this means a forged save is accepted — drop old secrets once players had time to update.

If a save matches **no** listed secret, it is moved to quarantine (see [Save Folder](#save-folder)), never deleted.

### Limits

- Without `SetKey`, the public default key is used, so a user who knows it can edit and re-sign saves.
- Restoring a full copy of an older save folder cannot be detected by a local save library.
- Reverse-engineering the game can reveal the secret.

---

## Hooks (Events)

Subscribe to show a saving icon, popups, analytics or cheat reports — it's up to you:

```csharp
Save.SaveStarted          += keys => savingIcon.SetActive(true);
Save.Saved                += keys => savingIcon.SetActive(false);
Save.SaveFailed           += (keys, error) => ShowPopup("Could not save, please free some space");
Save.IntegrityFailed      += message => CheatReporter.Report(message);
Save.TransactionDiscarded += keys => Debug.LogWarning("Not saved: " + string.Join(", ", keys));
```

- Always called on the **main thread** (safe to touch UI), on the next frame — even for `Set` / `Flush`.
- An exception in your handler is logged and never breaks saving or other handlers.
- Events right before the app quits may not be delivered.

The sample's `SaveHookLogger` component logs every hook to the Console — drop it into any scene.

---

## Save Migration (game updates)

Your data classes change between game versions. Give the save data a version number and write one small step per change; players' old saves are upgraded when they open the new version.

### 1. One setup class

```csharp
public sealed class GameSaveSetup : ISaveSetup
{
    public void Configure(SaveConfig c)
    {
        c.SetKey("secret-v2", "secret-v1");
        c.SchemaVersion = 3;                 // +1 whenever saved data changes shape
        c.AddMigrations<MigrationsV1>();
        c.AddMigrations<MigrationsV2>();

        // Optional: checked after the last step, before anything is saved. Throw = keep the old save.
        c.Verify(ctx =>
        {
            if (ctx.Get<PlayerData>("player").Gold < 0) throw new Exception("negative gold");
        });
    }
}
```

`SchemaVersion` is not the app version: a patch that does not change saved data keeps the same number.

### 2. Migration steps, grouped as you like

```csharp
public sealed class MigrationsV2 : ISaveMigrationSet
{
    int _coins; // shared by the steps of this class, lives for one migration only

    public void Register(SaveMigrations m)
    {
        m.Add(from: 1, MoveCoins);          // 1 -> 2
        m.Add(from: 2, AddAvatar);          // 2 -> 3
    }

    void MoveCoins(SaveMigrationContext ctx)
    {
        _coins = ctx.Exists("coins") ? ctx.Read("coins", r => r.ReadInt()) : 0;
        ctx.Delete("coins");
    }

    void AddAvatar(SaveMigrationContext ctx)
    {
        var old = ctx.Get<PlayerV2>("player");                  // old data class kept for reading
        ctx.Set("player", new PlayerData { Name = old.Name, Avatar = "default", Gold = _coins });
        ctx.Rename("opts", "settings");
    }
}
```

| `ctx.` | |
|---|---|
| `Get<T>(key)` / `Read(key, r => ...)` | Read with a data class, or field by field (no old class needed) |
| `Set(key, data)` / `Write(key, w => ...)` | Write (keeps the key's encryption, AES for new keys) |
| `Rename(from, to)` / `Delete(key)` / `Exists(key)` / `Keys` | Move, remove, check keys |

- A save of version 1 in a version-3 game runs `1 -> 2`, then `2 -> 3` (players may skip updates).
- Each step sees what the steps before it changed. Nothing is written until every step and `Verify` succeeded, then the whole result is saved **at once**: a crash or exception leaves the old save untouched, and the next launch tries again.
- Migration classes are created only when a save needs migrating, and dropped right after.
- In the Editor, `Save.Setup` checks the chain: a missing or duplicate step fails when you press Play.
- Very old saves: keep every step (default), replace many old steps with one jump `m.Add(from: 0, to: 30, ...)`, or remove old steps and handle `SaveTooOld`. Your call.

### 3. At game start

```csharp
Save.Setup(new GameSaveSetup());                     // before any other Save call

var result = Save.Migrate();                          // freezes until done (loading screen)
// or, without freezing:
var result = await Save.MigrateAsync(p => bar.value = p);                          // main thread, one step per frame
var result = await Save.MigrateAsync(p => bar.value = p, SaveMigrationThread.Background); // steps must not use Unity APIs
```

- Save already up to date (almost every launch): one number comparison, nothing else.
- Forgot to call it? The first Save call migrates (sync) and logs a warning.
- While `MigrateAsync` runs, other Save calls throw. WebGL always uses the main thread.

### Hooks: the library reports, the game decides

| Event | When | Saving |
|---|---|---|
| `MigrationStarted` / `MigrationStepCompleted` / `MigrationCompleted` | Progress | — |
| `MigrationFailed` `(failure)` | A step / `Verify` threw. Save unchanged, retried next launch | Blocked |
| `SaveTooOld` `(e)` | No step starts at the save's version | Blocked |
| `NewerSaveDetected` `(e)` | Save made by a newer game version (player reinstalled an old build) | Blocked |
| `SaveTouchedByOlderVersion` `(e)` | A build with Save Manager 1.1.0 or older saved over this save | — |

While blocked, `Set` / `Delete` throw and `SetAsync` fails; loading still works (`Save.IsWriteBlocked`). Your options:

```csharp
Save.NewerSaveDetected += e => ShowPopup("Please update the game");   // keep blocked
Save.NewerSaveDetected += e => e.AllowWrite();                         // play anyway (backup made first)
Save.SaveTooOld        += e => Save.StartFresh();                      // empty save at the game's version (backup first)
```

`AllowWrite()` is your risk: keys this game writes may lose what the other version added, and the save may mix layouts. The backup lets you recover.

### Backups

Before a migration is saved, before `AllowWrite()`, `RestoreBackup` and `StartFresh`, the whole save is copied to `SaveData/backups/`. The newest 3 are kept (`Save.BackupsToKeep` / `c.BackupsToKeep`). No backup possible (disk full) = no migration, the old save stays.

```csharp
foreach (var b in Save.ListBackups())                 // newest first: b.SchemaVersion, b.CreatedAt
    AddButton(b.ToString(), () => Save.RestoreBackup(b));
```

`RestoreBackup` checks the backup first (broken or edited = refused, nothing changes), backs up the current save, then restores it; an older restored save is migrated again by the next Migrate / Save call. Use it to ship a fixed migration step.

### Test migrations in the Editor

- **Snapshots**: while you play in the Editor, every save is copied to `<project>/SaveSnapshots/schema-<N>/` (one per version). Commit the folder: the team keeps a real save of every version. Menu: **Tools > Save Manager > Snapshots**.
- **Tools > Save Manager > Simulate Migration**: runs your `ISaveSetup` migrations on all snapshots (or the current save, backups, any folder such as a save pulled from a phone) in memory. Shows each step OK / failed with the exception, time, keys added / removed / changed and the `Verify` result, and can export the migrated save. Never writes to any save.
- In unit tests: `SaveMigrationSimulator.Run(new GameSaveSetup(), "SaveSnapshots/schema-1")` returns the same report.

### Compatibility and limits

- Saves of 1.1.0 load as version 0 and are migrated like any other.
- A game build with 1.1.0 can still read 1.2.0 saves (nothing is quarantined), but it does not know about versions, so it does not block writing. When the player updates again, `SaveTouchedByOlderVersion` is raised; keys that build rewrote may be in its older layout.
- Backups are signed with the secret of their time: keep that secret listed in `SetKey` to restore them.
- `MigrateAsync(..., Background)`: steps run on a worker thread, so no Unity API inside them.

---

## WebGL

WebGL has no worker threads, so background saves run on the **main thread, spread over frames**: one file per frame, then the save index switches on the last frame (the commit point, so a crash mid-way still keeps the previous complete save). `SetAsync` returns immediately, and `await`, `FlushAsync`, transactions and hooks work the same. A sync `Set` / `Flush` finishes a save in progress first. Nothing to configure.

> Tip: the heavier part is turning your data into bytes, which happens when you call `SetAsync`. Split big data into several keys (per region / system) and save only what changed.

---

## Save Folder

Everything lives in `Application.persistentDataPath/SaveData/`:

| File / folder | What it is |
|---------------|------------|
| `manifest.dat` | Save index: which file is current for each key, their fingerprints, signed |
| `manifest.dat.bak` | Previous save index — fallback if the current one is broken |
| `<key>.<number>.sav` | Data of one key. The number grows with each save; the previous one is kept as fallback |
| `quarantine/<time>/` | Saves that could not be verified (wrong / missing secret, or edited). Moved aside instead of deleted, so they can be recovered |
| `schema.dat` | Last save version written by this library (detects a rewrite by an older build) |
| `backups/<time>-schema-<N>/` | Full copies made before migrations and other risky changes (newest 3 kept) |

Menu: **Tools > Save Manager > Explore Save Files** / **Delete All Save Data**.

---

## Supported Data Types

| Type | Write | Read |
|------|-------|------|
| `byte` | `AddByte()` | `ReadByte()` |
| `byte[]` | `AddBytes()` | `ReadBytes(length)` |
| `int` | `AddInt()` | `ReadInt()` |
| `long` | `AddLong()` | `ReadLong()` |
| `float` | `AddFloat()` | `ReadFloat()` |
| `bool` | `AddBool()` | `ReadBool()` |
| `string` | `AddString()` | `ReadString()` |
| `List<T>` | `WriteList()` | `ReadList<T>()` |

**Unsupported types workaround:**
- `Dictionary<K,V>` → use 2 lists: `List<K>` keys + `List<V>` values
- `double` → use `long` (multiply by 1000000, divide when read)
- `decimal` → use `string` (ToString / Parse)

---

## API Reference

### Save

| Method | Description |
|--------|-------------|
| `Save.Set(key, data, encryption?)` | Save immediately (sync), together with everything pending. Use for checkpoint, purchase |
| `Save.SetAsync(key, data, encryption?, forceSync?)` | Save in background (no lag). Returns a `Task` you may `await` (completes when on disk) |
| `Save.BeginTransaction()` | `using` block + `Commit()`: all `SetAsync` inside are saved together, or none |
| `Save.Flush()` | Write everything pending now (sync). Use on quit / pause |
| `Save.FlushAsync()` | Write everything pending in the background; `await` until it is on disk |
| `Save.Get<T>(key)` | Load data. Returns `null` if never saved. Throws `InvalidDataException` if the save is broken |
| `Save.Get<T>(key, required)` | Same, but the file must use `required` encryption, otherwise rejected |
| `Save.TryGet<T>(key, out data)` / `TryGet<T>(key, required, out data)` | Load without exceptions; returns `Ok` / `NotFound` / `Corrupt` / `WrongEncryption` |
| `Save.Exists(key)` | Check if key has been saved |
| `Save.Delete(key)` | Delete key (sync) |
| `Save.Setup(setup)` | Apply an `ISaveSetup` (secret, SchemaVersion, migrations, Verify, BackupsToKeep). Before any other call |
| `Save.SchemaVersion` | Game's save data version (default 0) |
| `Save.SaveSchemaVersion` | Version of the save on disk |
| `Save.Migrate()` / `Save.MigrateAsync(progress?, thread?)` | Bring an older save up to date; returns `UpToDate` / `Migrated` / `NewerSave` / `TooOld` / `Failed` |
| `Save.IsWriteBlocked` / `Save.AllowWrite()` | Saving blocked (newer / too old / failed migration); allow anyway (backup first) |
| `Save.ListBackups()` / `Save.RestoreBackup(b)` / `Save.StartFresh()` | Backups, restore one, start an empty save |
| `Save.BackupsToKeep` | Backups kept (default 3) |
| `SaveMigrationSimulator.Run(setup, folder)` | Simulate a migration on a save folder, nothing written |

### Save events

| Event | Description |
|-------|-------------|
| `Save.SaveStarted` | `(keys)`: writing these keys started |
| `Save.Saved` | `(keys)`: these keys are safely on disk (deleted keys included) |
| `Save.SaveFailed` | `(keys, error)`: writing failed; previous save intact, retried on next save |
| `Save.IntegrityFailed` | `(message)`: save files broken / swapped / edited; previous save was used |
| `Save.TransactionDiscarded` | `(keys)`: transaction closed without `Commit()`, changes not saved |
| `Save.MigrationStarted` / `MigrationStepCompleted` / `MigrationCompleted` | Migration progress |
| `Save.MigrationFailed` | `(failure)`: a step or Verify threw; save unchanged, saving blocked |
| `Save.SaveTooOld` | `(e)`: no migration starts at the save's version; saving blocked (raised right away) |
| `Save.NewerSaveDetected` | `(e)`: save made by a newer game version; saving blocked (raised right away) |
| `Save.SaveTouchedByOlderVersion` | `(e)`: an older build (1.1.0 or older) saved over this save (raised right away) |

### SaveEncryption

| Member | Description |
|--------|-------------|
| `SaveEncryption.SetKey(secret, previousSecrets...)` | Set the secret for AES + save signing. Call once at game start, before any Save call. List older secrets to keep players' saves after changing it |
| `SaveEncryption.DefaultKey` | Secret used when `SetKey` is never called (public) |
| `SaveEncryption.SetKey(type, key)` | Old form, same as `SetKey(key)` |

### EncryptionType

| Value | Description |
|-------|-------------|
| `EncryptionType.AES` | AES-256 encryption (default) |
| `EncryptionType.None` | No encryption (still signed) |

### SaveLoadStatus

| Value | Description |
|-------|-------------|
| `Ok` | Loaded |
| `NotFound` | Never saved (or deleted) — new player |
| `Corrupt` | Save exists but is broken, edited, or no longer matches the data class |
| `WrongEncryption` | File uses a different encryption than required |

### ISaveable

Interface for saveable data models:

```csharp
public interface ISaveable
{
    void WriteSave(SaveWriter w);
    void ReadSave(SaveReader r);
}
```

### ISaveSetup / SaveConfig

`ISaveSetup.Configure(SaveConfig c)` — all save settings in one class, applied with `Save.Setup(...)` and reused by the Editor simulator.

| `SaveConfig` member | Description |
|--------|-------------|
| `SetKey(secret, previousSecrets...)` | Same as `SaveEncryption.SetKey` |
| `SchemaVersion` | Same as `Save.SchemaVersion`; unset keeps the current value |
| `BackupsToKeep` | Same as `Save.BackupsToKeep`; unset keeps the current value |
| `AddMigrations<T>()` | Register a migration set (`T : ISaveMigrationSet, new()`), created only when a save needs migrating |
| `Verify(ctx => ...)` | Check after the last step, before saving; throw to reject (several calls add up) |

### ISaveMigrationSet / SaveMigrations

`ISaveMigrationSet.Register(SaveMigrations m)` — a group of steps (e.g. one class per big game version).

| `SaveMigrations` member | Description |
|--------|-------------|
| `Add(from, step)` | Step `from -> from + 1` |
| `Add(from, to, step)` | Step jumping `from -> to` (e.g. to replace many old steps) |

### SaveMigrationContext

The save as seen by a step; changes stay in memory until the whole migration is saved.

| Member | Description |
|--------|-------------|
| `Get<T>(key)` | Read with a data class (old or current); `default` if missing |
| `Read(key, r => ...)` | Read field by field; throws if missing |
| `Set(key, data, encryption?)` | Write with a data class (keeps the key's encryption, AES for new keys) |
| `Write(key, w => ..., encryption?)` | Write field by field |
| `Rename(from, to)` | Move a key (throws if `from` is missing or `to` exists) |
| `Delete(key)` / `Exists(key)` / `Keys` | Remove, check, list keys |
| `SaveVersion` / `GameVersion` | Version of the save before migrating / of the game |
| `FromVersion` / `ToVersion` | Step running now (`GameVersion` during `Verify`) |

### SaveMigrationResult

| Value | Description |
|-------|-------------|
| `UpToDate` | Save already at the game's version (or new); nothing done |
| `Migrated` | Migrated and saved |
| `NewerSave` | Made by a newer game version; saving blocked |
| `TooOld` | No step starts at the save's version; saving blocked |
| `Failed` | A step / Verify / backup / saving failed; save unchanged, saving blocked |

### SaveMigrationThread

| Value | Description |
|-------|-------------|
| `MainThread` | One step per frame on the main thread; steps may use Unity APIs (default) |
| `Background` | Worker thread; steps must not use Unity APIs (WebGL uses `MainThread`) |

### Event data

| Type | Members |
|------|---------|
| `SaveVersionEvent` (`NewerSaveDetected`, `SaveTooOld`, `SaveTouchedByOlderVersion`) | `SaveVersion`, `GameVersion`, `OldestSupportedVersion` (SaveTooOld only), `AllowWrite()` |
| `SaveMigrationFailure` (`MigrationFailed`) | `SaveVersion`, `GameVersion`, `StepFrom` / `StepTo` (null if setup / Verify / backup / saving failed), `Error`, `AllowWrite()` |
| `SaveBackupInfo` (`ListBackups`) | `Path`, `SchemaVersion`, `CreatedAt` |

### SaveMigrationSimulator / SaveMigrationSimulation

| Member | Description |
|--------|-------------|
| `SaveMigrationSimulator.Run(setup, folder)` | Simulate migrating the save in `folder` (holding `manifest.dat`), in memory, nothing written |
| `SaveMigrationSimulator.CaptureCurrentSave(folder)` | Copy the loaded save into `folder` (outside SaveData), replacing it; `null` if not loaded / nothing saved |
| `sim.Result` / `sim.Error` / `sim.SaveVersion` / `sim.GameVersion` | Outcome |
| `sim.Steps` | Each step: `From`, `To`, `Owner`, `Milliseconds`, `Error` |
| `sim.Changes` | Each key: `Key`, `Kind` (Added / Removed / Changed), `SizeBefore`, `SizeAfter` |
| `sim.VerifyPassed` / `sim.ExportTo(folder)` / `sim.ToString()` | Verify passed; write the migrated save to an empty folder; readable report |

---

## Add Sample to Project

1. Open **Window > Package Manager**
2. Find **Save Manager** in the list
3. Expand **Samples** section
4. Click **Import** next to "Inventory"

---

## Repository layout

```
Packages/com.vanhaodev.savemanager/   ← the package (single source, installed via ?path=)
├── Runtime/  Editor/  Tests/Editor/
└── Samples~/Inventory/               ← hidden from Unity; imported into Assets/Samples (git-ignored)
```

Developing the sample: import it once, edit it under `Assets/Samples/...`, then copy the changes back to
`Samples~/Inventory` before committing. Tests: **Window > General > Test Runner > EditMode**.
