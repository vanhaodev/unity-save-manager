# Code Review: backups / restore / StartFresh + migration simulator + snapshot tools

Scope: commit 093b457 + uncommitted Runtime/Editor/Tests under Assets/com.vanhaodev.savemanager. Correctness only. Paths relative to `Assets/com.vanhaodev.savemanager/Runtime/Scripts/Manager/` unless noted.

## High

### H1. Backup/snapshot of a save still on a previous SetKey secret cannot be read after restore (silent data loss)
- `SaveManifestStore.cs:66-73` `Snapshot()` re-signs the manifest with **CurrentKeys** (`SaveManifest.Serialize` -> `SaveEncryption.Sign(body)`), but `SaveBackupStore.cs:197-198` copies the data files raw. If `_pendingKeyMove != null`, those files are still AES-encrypted with the old key.
- `SaveBackupStore.cs:277` sees `signedWith == CurrentKeys` and skips `Reencrypt`. The restored save then has old-key ciphertext under a current-key manifest. `Save.ReadPlainData` decrypts with CurrentKeys only (`SaveEncryption.cs:143`), so every AES key comes back Corrupt. Verify passes the whole time, so nothing warns the player.
- When this happens: a save written by a newer game and loaded with a previous secret. `CheckSchema` skips the key move when the save is newer (`SaveSchemaGuard.cs:553-563`), and then:
  - `AllowWrite` calls `Create` **before** `MoveToCurrentKeyIfNeeded` (`SaveSchemaGuard.cs:525-526`);
  - `StartFresh` or `RestoreBackup` runs while saving is blocked;
  - a migration runs after `MigrateToCurrentKey` failed;
  - `CaptureSnapshot` runs (the simulator then fails or migrates garbage).
- Fix: in `Snapshot()`, sign with `_pendingKeyMove ?? CurrentKeys` (add an optional `KeySet` parameter to `Serialize`). `ReadFolderVerified` then re-encrypts on restore. This matches the already-accepted rule "backup with a dropped secret cannot be restored".

### H2. If the device clock goes back, rotation deletes the backup it just made, and the migration still commits
- `SaveBackupStore.cs:174-181,240,283-288`: names start with `DateTime.UtcNow`, and rotation sorts by name. Players setting the clock back (common time cheat on mobile) give the new backup the oldest name. Once there are more than `keep` backups, `DeleteOldest` removes it right away, and `Create` returns a `SaveBackupInfo` for a deleted folder. `FinishMigration` (`Migration/SaveMigrate.cs:214-215`) then commits with no backup. This breaks invariant 1 ("no backup, no migration") and "keeps N newest".
- Fix: never delete `target` in `DeleteOldest` (pass it in and skip it). Better: name folders with a monotonic sequence (max existing seq + 1) and keep time only for display.

### H3. RestoreBackup can rotate out the backup being restored before the commit succeeds
- `Backup/SaveBackup.cs:54-64`: `ReadVerified(backup)` loads the files into memory, then `Create(_backupsToKeep)` rotates. If the chosen backup is the oldest (for example, rolling back past several migration attempts), it is deleted. If `SaveManifestStore.Commit` then throws (disk full, IO error), the live save is unchanged but the backup the user wanted is gone for good.
- Fix: do the rotation after `Commit` succeeds (split `Create` into copy + `DeleteOldest`), or exclude `backup.Path` from deletion in this call.

### H4. StartFresh / AllowWrite throw forever when a data file of the loaded save is missing, so the player is stuck
- `SaveManifestStore.cs:119-132` keeps a manifest that parses even when some files are missing ("intact keys still load"). `SaveBackupStore.cs:197-198` `File.ReadAllBytes(file)` then throws `FileNotFoundException`, so `StartFresh` (`Backup/SaveBackup.cs:84`) and `AllowWrite` (`SaveSchemaGuard.cs:525`) always throw.
- Scenario: a 0-schema save with one `.sav` deleted or partly synced. The migration fails (`ReadVerified` throws), saving is blocked, the game offers StartFresh, StartFresh throws. This repeats on every launch and the escape hatch is unusable.
- Fix: back up what exists. Skip missing files and drop their entries from the backup manifest so the backup stays verifiable, and log what was left out. If the documented "no backup, no write" rule must hold for AllowWrite, at least let StartFresh fall back to a best-effort backup.

## Medium

### M1. Stale `_pendingKeyMove` after RestoreBackup / StartFresh can silently corrupt restored keys
- `SaveManifestStore.cs:141,177-181`: `_pendingKeyMove` is set only at load and cleared only by a successful `MigrateToCurrentKey`. RestoreBackup and StartFresh commit content that is already on the current key (`ReadFolderVerified` re-encrypts it) but leave the flag set.
- After a restore, `CheckSchema` (not newer) calls `MoveToCurrentKeyIfNeeded` -> `SaveKeyMigration.TryReencrypt`, which tries the **old** key first. AES-CBC/PKCS7 has no authentication (`encryptionmanager/AesEncryption.cs:34-48`), so a wrong-key decrypt passes the padding check about 1 time in 256 per file. The garbage is re-encrypted and committed with a fresh valid hash, so the key is corrupted with no warning. In the other cases it is a pointless full rewrite.
- Fix: add `SaveManifestStore.ClearPendingKeyMove()` and call it after the Commit in RestoreBackup and StartFresh (inside IoLock).

### M2. Simulator key swap can sign the live save with the wrong secret
- `Migration/SaveMigrationSimulator.cs:30-41`: `UseTemporarily` swaps the global keys without holding IoLock. `SaveMigrationSimulator.Run` is public runtime API. If it runs in a game (or a test) while a background `SetAsync` batch is committing, the worker's `FinishCommit` -> `Serialize` signs `manifest.dat` with the setup's secret. When that secret differs from the game's (`WrongKeySetup`, or a per-player key set outside ISaveSetup), the live save fails its signature check on next launch and is quarantined. The doc comment only says "not while saving". The Editor window guards against Play Mode; the API does not.
- Fix: hold `SaveFileStore.IoLock` for the whole `using` scope. Commits can then not interleave, and `Serialize`/`Encrypt` for SetAsync run on the caller thread.

### M3. "Take Snapshot Now" before the game calls Save.Setup loads the live save without the game's key
- `Editor/Scripts/SaveSnapshotRecorder.cs:75` (`Save.SaveSchemaVersion`) and `Backup/SaveBackup.cs:100` (`CaptureSnapshot` -> `LoadIfNeeded`). The menu is enabled whenever `isPlaying`. If the game calls Setup or SetKey late (after login, per-player key), clicking it loads the save with the default key and schema 0. Result: Quarantine of the real save, or a wrong NewerSave/migration decision, and the game's later `Save.Setup` throws "call at game start". This breaks invariant 4 (no load without the game's key).
- Fix: `CaptureSnapshot` must not load. Return null when `!SaveManifestStore.IsLoaded` and read the schema from `SaveManifestStore.SchemaVersion` under the same lock. The recorder should use the returned schema rather than `Save.SaveSchemaVersion`, which also removes the folder-name/content race.

### M4. Public `CaptureCurrentSave(folder)` deletes whatever folder it is given
- `Backup/SaveBackup.cs:110-116`: `Directory.Delete(folder, true)` with no check. Passing `SaveFileStore.Folder` or an ancestor (such as `persistentDataPath`) deletes the live save **and all backups**, then puts the copy back.
- Fix: reject a `folder` that equals, contains, or is inside `SaveFileStore.Folder` (compare full paths).

## Low

- **L1.** `Editor/Scripts/SaveSnapshotRecorder.cs:44` `GetDirectories("schema-*")` also matches crash leftovers `schema-N.new` and `schema-N.new.tmp`. The "All snapshots" run then simulates partial folders and shows false FAILED results. Filter for exact `schema-<int>` and skip `.new`/`.tmp` folders.
- **L2.** `SaveBackupStore.cs:197-198` copies data files without checking their hash. A file damaged on disk after load gives a "complete" backup that `ReadVerified` later refuses. Hash-check while copying and fail (or skip, per H4) instead.
- **L3.** Auto snapshot does a full durable copy (fsync per file) under IoLock on the main thread after every save in Play Mode. That causes Editor frame hitches and blocks the worker. Acceptable in the Editor; consider throttling.

## Checked, no issue
- Backup tmp folder + rename (`SaveBackupStore.cs:189-207`); `.tmp` leftovers are cleaned in `List`; Quarantine moves only top-level files, so backups survive; `ListDataFiles` is top-level `*.sav` only.
- RestoreBackup verifies before changing anything, deletes keys missing from the backup, and runs the schema check again (block or migrate) with hooks raised again. StartFresh commits empty at `_schemaVersion` and clears the block.
- Migration backup failure -> `FailMigration`, save unchanged, write block set (`Current` is null after Execute, so failure attribution is correct).
- The simulator never writes save folders. `KeyScope` always restores the keys, including when `Simulate` throws. The window refuses to run in Play Mode and reads the live folder only through `ReadFolderVerified` (no Save load).
- Tests: `SaveTestBase` redirects to a GUID temp folder before `ResetStatics`. Teardown resets under IoLock before `UseFolder(null)`, so a stale worker or async migration cannot commit to the real folder (empty queue, session check).

## Recommended order
H1, H3, H2 (all three are small changes in SaveBackupStore/Snapshot), then H4, M1, M2, M3, M4.

## Unresolved questions
- H4: should AllowWrite also proceed on a partial backup, or only StartFresh? (This is a policy decision.)
- H2: is a sequence-numbered folder name acceptable (it changes the backup folder name format; `List` parsing would need to change)?

**Status:** DONE
**Summary:** 4 high (old-key backups unreadable after restore; clock-back rotation deletes the new backup; restore rotates out its own source; StartFresh stuck when a file is missing), 4 medium, 3 low.
