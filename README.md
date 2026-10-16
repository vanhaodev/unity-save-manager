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
        // Set AES key first (call once at game start)
        SaveEncryption.SetKey(EncryptionType.AES, "my-secret-key");

        // Save with AES encryption
        var user = new UserModel { Name = "vanhaodev" };
        Save.Set("user-encrypted", user, EncryptionType.AES);

        // Load (auto-detect encryption from file)
        var loaded = Save.Get<UserModel>("user-encrypted");
        Debug.Log(loaded.Name); // "vanhaodev"
    }
}
```

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

**Limits:** fingerprints are not signed yet, so a user who edits a file *and* recomputes the index is not detected. Restoring a full copy of an older save folder, or reverse-engineering the game, cannot be prevented by a local save library.

> **1.1.0:** save files use a new layout. Saves written by 1.0.x are not loaded.

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
| `Save.Exists(key)` | Check if key has been saved |
| `Save.Delete(key)` | Delete key (sync) |
| `Save.IntegrityFailed` | Event: save files broken / swapped / edited; previous save was used |

### SaveEncryption

| Method | Description |
|--------|-------------|
| `SaveEncryption.SetKey(type, key)` | Set encryption key. Call once at game start |

### EncryptionType

| Value | Description |
|-------|-------------|
| `EncryptionType.None` | No encryption (default) |
| `EncryptionType.AES` | AES-256 encryption |

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
