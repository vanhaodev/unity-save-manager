# Code review: save schema versioning and migration (phase 1 + 2)

Scope: `git diff HEAD~1` (3bffe78), working tree, untracked `Manager/Migration/*`. Correctness only.
Paths below are relative to `Assets/com.vanhaodev.savemanager/Runtime/Scripts/Manager/`.

## Invariant check

| # | Invariant | Result |
|---|-----------|--------|
| 1 | Failed migration leaves disk unchanged (one Commit at end) | Holds. `FinishMigration` is the only migration write (`Migration/SaveMigrate.cs:214-219`). `Commit` aborts new files on throw. After `WriteAtomic` the code only calls best-effort deletes and the marker write, so nothing can throw once the manifest has been swapped. |
| 2 | Up-to-date save: no sets created, schema.dat not read | Holds in a player build. `ResolveSchema` reads the marker only when the manifest has no trailing schema int (`SaveManifestStore.cs:145-156`). `Setup` creates sets only in the Editor (by design). |
| 3 | 1.1.0 parser reads the new manifest | Holds. The old `Deserialize` stops after the entries and ignores trailing body bytes. The signature covers the whole body. VERSION stays 1. The old `ListDataFiles` matches `*.sav` only, so old builds never delete schema.dat. |
| 4 | Writes blocked until AllowWrite | Holds once `CheckSchema` has run. There is a gap before that (F1). |
| 5 | MigrateAsync threading / reset | Holds within one session. `_needsMigration` stays true while the run is in progress, so every `EnsureLoaded` caller throws. `finally` resets `_migrationRunning`. Editor reset race: see F3. |
| 6 | Key move normal + held back while blocked | Holds for NewerSave. For TooOld/Failed the key move runs before the block is set; spec phase-02 line 118 says this is intended. Side effect: F4. |

## Findings

### F1 [High] Save calls made from inside the version events skip migration and the newer-save check
- `Save.cs:199-215` + `SaveSchemaGuard.cs:94-122`. `_loaded` becomes true inside `EnsureLoaded` (`SaveManifestStore.cs:129`). `CheckSchema` runs only after that, and it sets `_needsMigration` last (line 122).
- Scenario: a save at schema 1, the game at 3, and the save was touched by an older build. The `SaveTouchedByOlderVersion` handler runs inline (line 101) and calls `Save.Set("player", newLayoutData)`. Inside that call, `EnsureLoaded` sees the save as loaded, and `EnsureMigrated` sees `_needsMigration == false`. The key is committed with schema 1. When the handler returns, `_needsMigration` becomes true, and the 1->2 and 2->3 steps then run over "player", which is already in the new layout. Result: data corrupted, or the migration fails and writes are blocked.
- Same gap across threads: a worker thread calling `Save.Set` between `_loaded = true` and `BlockWrites` writes over a newer save without being blocked.
- Fix: decide all state before anything can see the save as loaded or any event fires. Move `_needsMigration` and `BlockWrites` ahead of every `InvokeEach` in `CheckSchema`, and raise `SaveTouchedByOlderVersion`/`NewerSaveDetected` last. Better still, gate `EnsureLoaded` on a "schema checked" flag, not on `IsLoaded`.

### F2 [Medium] After `AllowWrite()` on Failed/TooOld/NewerSave, the save mixes layouts but keeps one schema number
- `SaveManifestStore.cs:196` (`BeginCommit` keeps `_schema`) + `SaveSchemaGuard.cs:69-81`.
- Scenario: migration fails, the game calls `AllowWrite()` and writes "player" in its v3 layout. The manifest still says v1. Next launch migrates 1->3 again, over keys that are already v3: corruption or a second failure. NewerSave mirror case: an older game writes old-layout keys under the newer schema number, and the newer build later reads them as current.
- Spec phase-02 line 120 accepts this for TooOld only. Failed and NewerSave carry the same hazard, and nothing tells the game.
- Fix: document it on `AllowWrite` and in the failure types at minimum. Option: write the game's `_schemaVersion` on the first commit after `AllowWrite` for Failed/TooOld (the game now owns the data), or mark the save as mixed.

### F3 [Medium, Editor with domain reload off] A migration still running after play mode exits can commit into the next session
- `Save.cs:42-55` resets `_migrationRunning` (`Migration/SaveMigrate.cs:279`) and the store, but does not cancel a Background `Task.Run` or a MainThread continuation that is still pending.
- Scenario: a long Background migration, then exit and re-enter Play. The new session loads. The stale run reaches `FinishMigration` and calls `Commit(files, _schemaVersion)`. `BeginCommit` reads the new session's `_current.Generation`, so the generation check passes. Files built from the old snapshot overwrite whatever the new session wrote, or the new session's own migration then hits "out of date", fails, and blocks writes. If the store is not loaded yet, the stale run calls `FailMigration` and blocks writes in the new session instead.
- Fix: save a session token (or the manifest generation at `KeysSnapshot`) in `SaveMigrationRun`. In `FinishMigration`/`FailMigration`, check it under IoLock and drop the run quietly if it no longer matches. The generation check also hardens the normal path against any commit made during the run.

### F4 [Low] While the key move is held back, AES keys cannot be read
- `Encryption/SaveEncryption.cs:103`: `Decrypt` uses `CurrentKeys` only. `Get` and `SaveMigrationContext.ReadSaved` (`Migration/SaveMigrationContext.cs:172-184`) both decrypt this way.
- Scenario: a NewerSave signed with a key that is still in the previous-key list. Writes are blocked, so the key move is held back, and every AES `Get` returns Corrupt. The docs say "loading still works". The same applies if the key move failed (I/O): the migration then fails on the first AES key read.
- Fix: decrypt with the key that signed the manifest when `_pendingKeyMove != null` (or try the accepted keys, like `SaveKeyMigration.TryReencrypt`).

### F5 [Low] A failed marker write is not retried in the same session
- `SaveManifestStore.cs:246-250`: `TryWrite` swallows errors, but `_markerSchema` is still set to the new value. After a migration commit whose marker write failed, schema.dat stays missing or stale (an earlier schema). If an older build then rewrites the save, the next load either sees schema 0 or the stale marker value, and steps run again over migrated data.
- Fix: make `TryWrite` return a bool and set `_markerSchema` only when it succeeds.

### F6 [Low] An empty manifest from 1.1.0 (all keys deleted) counts as schema 0, not as a new save
- `SaveManifestStore.cs:139`: only the `Empty` singleton gets `gameSchema`. A 1.1.0 manifest with 0 entries and no trailing schema int ends up at schema 0. With no step from 0 registered, the result is TooOld and writes are blocked for a player who has no data.
- Fix: also treat `_current.Entries.Count == 0` as new (`_schema = gameSchema`).

### F7 [Low] `Setup` can silently reset `SchemaVersion` to 0
- `Migration/SaveMigrate.cs:55`: `SchemaVersion = config.SchemaVersion` always runs, and the config defaults to 0. A game that sets `Save.SchemaVersion = 5` and then calls `Setup` without setting it in the config gets 0. Its own saves then look newer, so writes are blocked. This conflicts with the spec claim that the old API still works next to `Setup`.
- Fix: make `SaveConfig.SchemaVersion` an `int?` and apply it only when set.

## Spec deviation (informational)
- Phase-01 line 12 says the marker stores schema + generation. The code stores the schema only. That is enough for the current detection rule (old builds always drop the trailing int), so it is fine, but the plan text should be updated.

## Not issues (checked)
- Writing the manifest first and the marker second is the safe order. The reverse order would mark a 1.1.0 save as migrated after a crash.
- Background mode: `await Task.Run` resumes on Unity's SynchronizationContext, so `FinishMigration` commits on the main thread. Worker reads take IoLock.
- Context: rename/delete/write overlay, `BuildFileChanges` skips deletes of keys that were never saved, and the encryption type is kept.

**Status:** DONE_WITH_CONCERNS
**Summary:** Invariants 1-3 and 6 hold. Invariants 4 and 5 hold once the schema check has finished. One High: Save calls made from inside the version events, or from another thread, can run before the migration flag and write block are set. Two Medium: mixed layouts after AllowWrite, and an Editor-only stale-migration commit.
**Concerns/Blockers:** F1 should be fixed before release. F2 needs a design decision from the user.

## Unresolved questions
- Is the Save API meant to be main-thread only? If so, F1 is limited to the event-handler case.
- F2: should `AllowWrite` after Failed/TooOld write the game's schema number, or keep the current behavior and only document it?
