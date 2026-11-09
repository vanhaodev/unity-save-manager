# Phase 1 — Đánh số version cho save + chặn ghi + hook save mới hơn

## Overview
Ưu tiên: cao · Trạng thái: chưa làm · Nền cho phase 2–3.

## Key insights
- `SaveManifest` (VERSION=1) đã ký toàn bộ body → thêm field schema vào body là được ký luôn.
- Version chỉ cần ở mục lục (không cần trong từng file key, vì đã bỏ đọc theo version trong `ReadSave`).
- **Giữ manifest VERSION 1**, ghi `SchemaVersion` vào cuối body (sau danh sách entry, vẫn trong vùng ký). Lib 1.1.0 đọc entry xong là dừng, không kiểm tra phần dư (verified by `SaveManifest.cs` Deserialize) → bản cũ đọc được save mới, **không quarantine**.
- Manifest không có phần cuối → schema 0 (save 1.1.0) — coi như 1 bước migrate nội bộ, tự động, không cần user làm gì.
- Đường nhanh: manifest có phần cuối (schema) → lib cũ không ghi lần cuối → **không đọc `schema.dat`**. Chỉ đọc khi manifest thiếu phần cuối.
- Phát hiện save bị bản lib cũ ghi đè: 1.2.0 ghi thêm file đánh dấu `schema.dat` (schema + generation, có ký). Lib cũ không biết file này nên không xoá/cập nhật. Load: có `schema.dat` mà manifest mất phần cuối / generation lệch → save đã bị bản cũ ghi → dùng schema trong `schema.dat`, bắn `Save.SaveTouchedByOlderVersion` (game tự quyết, gợi ý khôi phục backup).

## Requirements
- `Save.SchemaVersion` (int, mặc định 0) — set trước mọi lời gọi Save, cùng chỗ `SetKey`. Set sau khi đã load → `InvalidOperationException`.
- Mục lục lưu schema của save. Manifest v1 cũ đọc ra schema 0.
- Save mới (chưa có gì) → nhận schema hiện tại ngay.
- Mỗi lần commit bình thường ghi schema = schema của save đang mở (không tự nâng — chỉ migrate mới nâng).
- Save schema > game: bắn `Save.NewerSaveDetected(SaveVersionEvent e)` (`e.SaveVersion`, `e.GameVersion`, `e.AllowWrite()`), **chặn ghi** mặc định.
- Khi bị chặn ghi: `Set`/`Flush` ném `InvalidOperationException`; `SetAsync` trả Task lỗi + `SaveFailed`. `Get`/`TryGet` vẫn hoạt động (đọc layout lạ → `Corrupt`, như hiện tại).
- `Save.IsWriteBlocked` (bool) cho game kiểm tra.

## Architecture
- `SaveManifest`: thêm `SchemaVersion`; Serialize giữ VERSION 1, ghi int schema sau danh sách entry; Deserialize: còn byte dư → đọc schema, hết dữ liệu → schema 0.
- `SaveManifestStore.Commit/FinishCommit`: nhận schema đích (mặc định = schema hiện tại của manifest).
- File mới `SaveSchemaGuard.cs` (partial `Save`): property `SchemaVersion`, cờ chặn ghi, kiểm tra sau `EnsureLoaded`, event args.
- Hook bắn trên main thread qua `SaveEvents` sẵn có. Handler gọi `AllowWrite()` đồng bộ trong lúc bắn (cần bắn ngay, không đợi frame sau — khác các hook khác; ghi rõ).

## Related code files
- Sửa: `SaveManifest.cs`, `SaveManifestStore.cs`, `Save.cs` (EnsureLoaded, Set/SetAsync/Flush check), `SaveEvents.cs`, `SaveQueue.cs` (từ chối khi chặn).
- Tạo: `SaveSchemaGuard.cs`, `SaveVersionEvent.cs`.

## Implementation steps
1. Schema ở cuối manifest (giữ VERSION 1) + `schema.dat` đánh dấu; thiếu phần cuối = schema 0.
2. Truyền schema đích qua `CommitJob`.
3. `Save.SchemaVersion` + khoá sau khi load.
4. Sau `EnsureLoaded`: so sánh → hook + cờ chặn ghi.
5. Chặn ở `Set`, `SetAsync`, `Flush`, `FlushAsync`, `Delete`, transaction commit.
6. Gộp: `SaveKeyMigration` (đổi secret) không chạy khi chặn ghi.

## Todo
- [ ] Manifest lưu schema, đọc được save 1.1.0
- [ ] `Save.SchemaVersion`
- [ ] Hook `NewerSaveDetected` + `AllowWrite()`
- [ ] Chặn ghi + `IsWriteBlocked`
- [ ] Compile sạch

## Success criteria
- Save 1.1.0 load bình thường, lần ghi sau thành manifest v2 schema 0.
- Sửa tay schema save lên 99 (qua code test) → hook bắn, ghi bị từ chối, đọc vẫn chạy.

## Risks
- Hook phải chạy đồng bộ để `AllowWrite` kịp trước lời ghi đầu → bắn ngay trong `EnsureLoaded` trên main thread (EnsureLoaded luôn ở main thread).

## Security
- Schema nằm trong body đã ký → sửa tay bị phát hiện như hiện tại.
