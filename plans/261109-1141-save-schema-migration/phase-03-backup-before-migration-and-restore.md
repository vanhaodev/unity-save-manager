# Phase 3 — Backup trước migrate + khôi phục backup

## Overview
Ưu tiên: cao · Trạng thái: chưa làm · Phụ thuộc phase 1, 2.

## Requirements
- Luôn bật. Trước commit migrate: copy `manifest.dat` + các file `.sav` đang dùng vào `SaveData/backups/schema-<N>-<yyyyMMdd-HHmmss>/`.
- Cũng backup trước lần ghi đầu tiên lên save mới hơn game (sau `AllowWrite()`).
- Backup lỗi (đầy ổ...) → **không migrate**, kết quả `Failed`, save cũ nguyên.
- `Save.BackupsToKeep` (mặc định 3): sau backup mới, xoá bản cũ nhất vượt số lượng.
- `Save.ListBackups()` → `SaveBackupInfo { SchemaVersion, CreatedAt, Path }` (mới nhất trước).
- `Save.RestoreBackup(info)`:
  1. Kiểm tra backup (chữ ký mục lục + dấu vân tay từng file) — hỏng → `InvalidDataException`, không đụng save hiện tại.
  2. Backup save hiện tại trước.
  3. Ghi nội dung backup thành 1 lần lưu mới (commit nguyên tử sẵn có), schema = schema backup.
  4. Lần mở / `Migrate()` tiếp theo migrate lại bằng code đã sửa.
- Menu Editor: "Explore Save Files" đã có; thêm không cần.

## Architecture
- Tạo `Backup/SaveBackupStore.cs` (copy, xoay vòng, liệt kê, kiểm tra), `Backup/SaveBackupInfo.cs`, partial `Save` trong `Backup/SaveBackup.cs`.
- Kiểm tra backup: tái dùng `SaveManifest.Deserialize` + `SaveFileStore.Hash` (tách `TryLoadVerified` nhận thư mục).
- Thư mục con `backups/` không bị `ListDataFiles`/`DeleteOrphans`/`Quarantine` đụng tới (chỉ quét file cấp trên) — xác nhận lại khi làm.

## Todo
- [ ] Backup tự động trước migrate / trước ghi đè save mới hơn
- [ ] Giữ N bản gần nhất
- [ ] Liệt kê + khôi phục (kiểm tra trước, backup save hiện tại)
- [ ] Compile sạch

## Success criteria
- Migrate xong có đúng 1 thư mục backup chứa save schema cũ; khôi phục → save về schema cũ, migrate lại chạy được.
- 5 lần migrate → còn 3 backup.
- Backup bị sửa tay → khôi phục từ chối, save hiện tại không đổi.

## Risks
- Backup ký bằng secret cũ; sau khi đổi secret và bỏ secret cũ khỏi `SetKey` → backup không kiểm tra được. Ghi README.

## Security
- Backup giữ nguyên mã hoá + chữ ký; khôi phục đi qua kiểm tra như load thường → không thành cửa sau để nạp save sửa tay.
