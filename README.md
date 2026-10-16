# Unity Save Manager

Fast binary save system with encryption for Unity.

## Download

- [Latest Release](https://github.com/vanhaodev/unity-save-manager/releases/latest)
- [All Releases](https://github.com/vanhaodev/unity-save-manager/releases)

---

## Example 1: Basic Save & Load

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

// Usage
public class Example1 : MonoBehaviour
{
    void LoadSave()
    {
        // Save
        var user = new UserModel { Name = "vanhaodev" };
        Save.Set("user", user);

        // Load
        var loaded = Save.Get<UserModel>("user");
        Debug.Log(loaded.Name); // "vanhaodev"
    }
}
```

---

## Example 2: Save & Load with AES Encryption

```csharp
using UnityEngine;
using vanhaodev.savemanager;

public class Example2 : MonoBehaviour
{
    void LoadSaveEncryption()
    {
        // Set the secret first (once at game start, BEFORE any Save call).
        // Used for AES and for signing saves.
        SaveEncryption.SetKey("my-secret-key");

        // Save with AES encryption
        var user = new UserModel { Name = "vanhaodev" };
        Save.Set("user-encrypted", user, EncryptionType.AES);

        // Load, requiring AES (a file switched to "no encryption" is rejected)
        var loaded = Save.Get<UserModel>("user-encrypted", EncryptionType.AES);
        Debug.Log(loaded.Name); // "vanhaodev"
    }
}
```

### Secret and signed saves

Saves are **always signed** and **AES-encrypted by default**. Editing any save file — even recomputing fingerprints — is detected: the previous valid save is used and `Save.IntegrityFailed` is raised.

`SaveEncryption.SetKey(secret)` turns any string into your own two strong keys (one encrypts, one signs).

> **No `SetKey`?** A built-in default key is used. Saves still work, are encrypted and signed, and can be copied between devices — but the default key is public (it is in this library's source), so anyone who reads it can decrypt and re-sign saves. Call `SetKey` for real protection.

To store a key without encryption (still signed), pass `EncryptionType.None`.

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

If a save matches **no** listed secret (secret forgotten, or files edited), it is **not deleted**: it is moved to `SaveData/quarantine/<time>/`, the game starts with an empty save and `Save.IntegrityFailed` is raised. Ship an update that lists the right secret and move the files back to recover.

---

## Example 3: Download Image & Save Async

```csharp
using UnityEngine;
using UnityEngine.Networking;
using vanhaodev.savemanager;
using System.Collections;

// Image data model
public class ImageModel : ISaveable
{
    public byte[] Data;

    public void WriteSave(SaveWriter w)
    {
        w.AddInt(Data?.Length ?? 0);
        if (Data != null) w.AddBytes(Data);
    }

    public void ReadSave(SaveReader r)
    {
        int length = r.ReadInt();
        Data = length > 0 ? r.ReadBytes(length) : null;
    }
}

// Usage
public class Example3 : MonoBehaviour
{
    IEnumerator LoadSaveImageAsync()
    {
        string url = "https://example.com/image.png";

        // Download image
        using var request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            // Save async (no lag)
            var image = new ImageModel { Data = request.downloadHandler.data };
            Save.SetAsync("my-image", image);
            Debug.Log("Image saved!");

            // Load
            var loaded = Save.Get<ImageModel>("my-image");
            Debug.Log($"Loaded image: {loaded.Data.Length} bytes");
        }
    }
}
```

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

## Consistent Save (Anti-Corruption)

All keys always load from **the same save**. A crash or error while saving never leaves a mix of old and new data:

1. New data is written to new files (`money.43.sav`), old files are not touched
2. When every file is fully on disk, the save index (`manifest.dat`) switches to the new files in one step
3. Old files are deleted

If the game crashes before step 2, the next load uses the previous complete save. Only changed keys are written, so saving stays fast.

### Save related data together with a transaction

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

### Save on quit / pause

```csharp
void OnApplicationPause(bool paused) { if (paused) Save.Flush(); }
void OnApplicationQuit() => Save.Flush();
```

### Broken or edited save files

Each file's fingerprint is stored in the save index. If a file is broken, swapped or renamed, the library falls back to the previous complete save and raises `Save.IntegrityFailed`:

```csharp
Save.IntegrityFailed += message => Debug.LogWarning("Save problem: " + message);
```

If a save fails (disk full, file locked...), the data is kept and written again on the next `SetAsync` / `Set` / `Flush`.

**Limits:** without `SaveEncryption.SetKey`, the public default key is used, so a user who knows it can edit and re-sign saves. Restoring a full copy of an older save folder, or reverse-engineering the game to extract the secret, cannot be prevented by a local save library.

> **1.1.0:** save files use a new layout. Saves written by 1.0.x are not loaded.

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

---

## API Reference

### Save

| Method | Description |
|--------|-------------|
| `Save.Set(key, data, encryption?)` | Save immediately (sync), together with everything pending. Use for checkpoint, purchase |
| `Save.SetAsync(key, data, encryption?, forceSync?)` | Save in background (no lag). Use for normal gameplay |
| `Save.BeginTransaction()` | `using` block + `Commit()`: all `SetAsync` inside are saved together, or none |
| `Save.Flush()` | Write everything pending now (sync). Use on quit / pause |
| `Save.Get<T>(key)` | Load data. Returns `null` if never saved. Throws `InvalidDataException` if the file is broken |
| `Save.Get<T>(key, required)` | Same, but the file must use `required` encryption, otherwise rejected (recommended for encrypted saves) |
| `Save.Exists(key)` | Check if key has been saved |
| `Save.Delete(key)` | Delete key (sync) |
| `Save.SaveStarted` | Event `(keys)`: writing these keys started |
| `Save.Saved` | Event `(keys)`: these keys are safely on disk (deleted keys included) |
| `Save.SaveFailed` | Event `(keys, error)`: writing failed; previous save intact, retried on next save |
| `Save.IntegrityFailed` | Event `(message)`: save files broken / swapped / edited; previous save was used |
| `Save.TransactionDiscarded` | Event `(keys)`: transaction closed without `Commit()`, changes not saved |

### SaveEncryption

| Method | Description |
|--------|-------------|
| `SaveEncryption.SetKey(secret, previousSecrets...)` | Set the secret for AES + save signing. Call once at game start, before any Save call. List older secrets to keep players' saves after changing it |
| `SaveEncryption.DefaultKey` | Secret used when `SetKey` is never called (public) |
| `SaveEncryption.SetKey(type, key)` | Old form, same as `SetKey(key)` |

### EncryptionType

| Value | Description |
|-------|-------------|
| `EncryptionType.None` | No encryption (still signed) |
| `EncryptionType.AES` | AES-256 encryption (default) |

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
