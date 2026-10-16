# Lưu đồng bộ nhiều file (savemanager 1.1.0)

Nguồn: `Logs/savemanager-261015-1347-atomic-write-and-queue-bugs.md`. Chốt với user ngày 2026-10-16.

## Đợt này
- [x] Mọi file save thuộc cùng 1 lần lưu (mục lục + số lần lưu trong tên file)
- [x] Crash giữa lúc lưu → quay về lần lưu hoàn chỉnh gần nhất
- [x] Lưu lỗi khi game còn chạy → giữ dữ liệu, thử lại ở lần lưu kế tiếp (không tự retry)
- [x] Chỉ ghi file thay đổi
- [x] Tự dọn file thừa khi load
- [x] Dấu vân tay mỗi file trong mục lục → phát hiện hỏng / tráo / đổi tên → về bản hợp lệ trước + báo game
- [x] Transaction `using (var tx = Save.BeginTransaction()) { ...; tx.Commit(); }` — không Commit → bỏ cả nhóm + log cảnh báo
- [x] `Save.Flush()` lưu hết ngay (quit/pause)
- [x] Menu Editor xoá toàn bộ save
- [x] README: API mới + giới hạn
- [x] Version 1.1.0

## Còn mở
- [ ] Test trong Unity Editor + build Android/iOS

## Để lần sau
- `SetKey` nâng cấp (tạo khoá chắc, tách khoá mã hoá / khoá ký) — lỗi 11, 12
- Ký mục lục bằng khoá bí mật (MAC) → chặn sửa tay + tự tính lại dấu vân tay. Hiện chỉ có dấu vân tay, chưa ký.
  - Khoá do dev game truyền vào `SetKey` (lib không quan tâm nguồn). Lib tự tách khoá ký / khoá mã hoá.
  - 2 lựa chọn cho dev: save **trao đổi được** (secret chung mọi máy) / **không trao đổi được** (secret theo account id). Không cần cờ riêng — ghi rõ trong README, cân nhắc overload `SetKey(secret, ownerId)`. Cảnh báo: dùng device id → đổi máy mất save.
- Mã hoá nội dung chắc hơn, không tin loại mã hoá trong header — lỗi 9, 10 (phần giấu nội dung)
- Đọc save từ bản lib cũ (1.0.0 `key.dat`) — cần nghiên cứu
- Chờ được tới khi lưu xong (`Task`) cho IAP
- Phân biệt "chưa có save" / "save hỏng" khi load

## Giới hạn (ghi vào README)
- Không chặn chép đè nguyên cả bộ save cũ.
- Không chặn người dịch ngược game.
