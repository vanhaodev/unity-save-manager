# Nâng cấp save cũ lên schema mới (savemanager 1.2.0)

Nguồn: `plans/reports/research-261109-1116-save-migrator-design-report.md`. Chốt với user ngày 2026-11-09.

## Đã chốt
- 1 số `Save.SchemaVersion` cho cả game, lưu trong mục lục save. Không dùng `Application.version` cho logic.
- Chỉ 1 cách: các bước migrate `from N → N+1`, chạy lần lượt từ version save tới version game. Không có `r.Version` trong `ReadSave` (user thấy rối).
- Gom bước theo class/interface do user tự tổ chức (lib không dùng partial được vì khác assembly).
- Tất cả bước lưu **1 lần** cuối cùng: lỗi/crash giữa chừng → save cũ nguyên vẹn.
- Chạy đồng bộ (đóng băng) hoặc `await`; `await` cho user chọn chạy nền hoặc rải theo frame. WebGL luôn rải theo frame.
- Save mới hơn game / quá cũ / migrate lỗi: lib chỉ báo hook + chặn ghi; user quyết cho ghi hay không.
- `await` mặc định rải theo frame trên main thread (an toàn); chạy nền là tuỳ chọn.
- Cấu hình gom 1 chỗ (`ISaveSetup`): secret, version, các bước, `Verify` (kiểm tra sau migrate, lỗi → không lưu).
- Editor: tự chụp snapshot save theo từng version vào `SaveSnapshots/`; menu giả lập migrate chạy trên mọi snapshot, không đụng save thật.
- Hàm migrate là instance, lib tự `new` khi cần rồi bỏ; save mới nhất thì không tạo gì.
- Save rất cũ: lib hỗ trợ chuỗi dài không giới hạn (mặc định giữ hết). User lib tự quyết: giữ hết / gộp bằng bước nhảy cóc (`from`, `to`) / ngừng hỗ trợ (`SaveTooOld`). Lib không tự cắt, không tự xoá save.
- **Đường nhanh khi save đã mới nhất** (yêu cầu user): chỉ so 1 số đã có sẵn trong mục lục vừa load → bỏ qua. Không đọc thêm file, không tạo class migrate, không kiểm tra chuỗi (chỉ Editor), không backup, `MigrateAsync` trả về ngay không đợi frame.
- Backup luôn bật trước khi migrate (và trước khi ghi đè save mới hơn); giữ 3 bản gần nhất, chỉnh được; có hàm khôi phục.

## Các bước
| # | Tính năng | Trạng thái |
|---|-----------|-----------|
| 1 | [Đánh số version cho save + chặn ghi + hook save mới hơn](phase-01-schema-version-stamp-and-newer-save-guard.md) | ✅ code |
| 2 | [Chuỗi bước migrate + chạy đồng bộ / await + hook](phase-02-migration-chain-runner-and-hooks.md) | ⬜ |
| 3 | [Backup trước migrate + khôi phục backup](phase-03-backup-before-migration-and-restore.md) | ⬜ |
| 4 | [Giả lập migrate trong Editor (lấy save thật, không đụng save thật), test, README, version 1.2.0](phase-04-fixture-tests-docs-and-release.md) | ⬜ |

Thứ tự bắt buộc: 1 → 2 → 3 → 4.

## Giới hạn (ghi vào README)
- Cho bản cũ ghi đè save mới hơn có thể làm save hỏng khi update lại → khôi phục bằng backup.
- Bản game dùng lib 1.1.0 vẫn đọc được save 1.2.0 (không quarantine), nhưng không biết save mới hơn nên không chặn ghi; lib 1.2.0 phát hiện lại khi người chơi update (hook `SaveTouchedByOlderVersion`).
- Code migrator chạy nền không được gọi Unity API.
