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

## Atomic Save (Anti-Corruption)

All save operations use **atomic write** to prevent file corruption:

1. Write data to temp file (`save.dat.tmp`)
2. Delete old file (`save.dat`)
3. Rename temp to main (`save.dat.tmp` → `save.dat`)

**Why?** If game crashes during save, only temp file is corrupted. Main file stays safe.

---

## API Reference

### Save

| Method | Description |
|--------|-------------|
| `Save.Set(key, data, encryption?)` | Save immediately (sync). Use for exit game, checkpoint |
| `Save.SetAsync(key, data, encryption?, forceSync?)` | Save in background (no lag). Use for normal gameplay |
| `Save.Get<T>(key)` | Load data. Returns `null` if file not exists |
| `Save.Exists(key)` | Check if file exists |
| `Save.Delete(key)` | Delete file |

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
