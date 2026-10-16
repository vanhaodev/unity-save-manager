# Unity Save Manager

**v1.1.0** · Unity 6 (6000.0+)

Fast binary save system for Unity: crash-safe, consistent across files, encrypted and signed by default.

- **Crash-safe** — a crash or power loss while saving never corrupts or mixes saves; the last complete save is used
- **Consistent** — all keys always load from the same save; group related data with transactions
- **Fast** — saves in the background, only changed keys are written
- **Secure by default** — AES-encrypted and signed; edited, swapped or broken files are detected
- **Awaitable** — `await Save.SetAsync(...)` when the data must be on disk (e.g. in-app purchases)
- **Hooks** — events for saving / saved / failed / tampered, delivered on the main thread
- **WebGL ready** — no threads needed, saves are spread over frames

## Download

- [Latest Release](https://github.com/vanhaodev/unity-save-manager/releases/latest)
- [All Releases](https://github.com/vanhaodev/unity-save-manager/releases)

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

### Save events

| Event | Description |
|-------|-------------|
| `Save.SaveStarted` | `(keys)`: writing these keys started |
| `Save.Saved` | `(keys)`: these keys are safely on disk (deleted keys included) |
| `Save.SaveFailed` | `(keys, error)`: writing failed; previous save intact, retried on next save |
| `Save.IntegrityFailed` | `(message)`: save files broken / swapped / edited; previous save was used |
| `Save.TransactionDiscarded` | `(keys)`: transaction closed without `Commit()`, changes not saved |

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

---

## Add Sample to Project

1. Open **Window > Package Manager**
2. Find **Save Manager** in the list
3. Expand **Samples** section
4. Click **Import** next to "Inventory"
