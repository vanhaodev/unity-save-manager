# Phase 4 — Giả lập migrate (Editor), test, README, version 1.2.0

## Overview
Ưu tiên: trung bình · Trạng thái: chưa làm · Phụ thuộc 1–3.

## Giả lập migrate (Tools > Save Manager > Simulate Migration)
Yêu cầu user (2026-11-09): menu tự lấy save của game dùng lib để test hàm migrate đúng không.

### Lấy save phiên bản cũ: tự chụp snapshot theo version (chỉ Editor)
- Mỗi lần lưu thành công trong Play Mode, lib tự copy save vào `<project>/SaveSnapshots/schema-<N>/` (đè bản cũ cùng schema → mỗi version giữ 1 bản mới nhất).
- Nằm ngoài `Assets/` (Unity không import), commit được lên git → cả team có save của mọi version.
- Dev tăng `SchemaVersion` 4 → 5: `schema-4/` đã có sẵn từ lúc chơi thử bản 4 → giả lập luôn, không phải làm gì.
- Bật/tắt + "Chụp ngay" trong menu. Không có trong build.
- Save cũ hơn lúc chưa có tính năng: lấy từ backup, máy thật (Android: thư mục `persistentDataPath/SaveData`), hoặc checkout bản cũ chơi 1 lần.

### Nguồn save (chọn trong cửa sổ)
  - **Tất cả snapshot** (mặc định): chạy giả lập từ mọi version cũ → version hiện tại, báo từng cái
  - Save hiện tại của game trong Editor (`persistentDataPath/SaveData`)
  - 1 bản trong `backups/`
  - 1 thư mục bất kỳ (save kéo từ máy thật / bản release cũ)
- **Không bao giờ đụng save thật**: đọc vào bộ nhớ, chạy chuỗi bước, không commit.
- Cấu hình lấy từ `ISaveSetup` của user (TypeCache tìm trong Editor) → có secret, SchemaVersion, bước migrate y như lúc chạy game.
- Kết quả hiển thị:
  - version save → version game, từng bước: OK / lỗi + exception + dòng code, thời gian
  - thay đổi key: thêm / xoá / đổi tên / đổi nội dung (kích thước trước → sau)
  - kết quả kiểm tra `Verify` của user (xem phase 2)
  - Save đã mới nhất → báo "không có gì để migrate", gợi ý chọn backup / thư mục save cũ
- Nút "Xuất kết quả ra thư mục" để mở bằng Explore / load thử.
- Dùng chung lõi chạy bước với runtime (`SaveMigrationRunner` chạy trên snapshot trong bộ nhớ) → giả lập đúng y như thật.

## Test & phát hành
- Tạo `Tests/Editor` (Unity Test Framework, EditMode): đọc save 1.1.0; chuỗi đủ/thiếu/trùng; bước throw; Verify fail; save mới hơn + `AllowWrite`; TooOld; backup + xoay vòng + khôi phục; async 2 chế độ; giả lập không đổi save thật.
- README: mục Migration — `ISaveSetup`, ví dụ bước, quy tắc tăng version, hook, backup/khôi phục, giả lập, giới hạn.
- Đồng bộ `Exported/`, sample, `package.json` 1.2.0.

## Todo
- [ ] Cửa sổ giả lập: chọn nguồn, chạy, xem từng bước + thay đổi key + Verify
- [ ] Xuất kết quả giả lập
- [ ] Test EditMode
- [ ] README + sample
- [ ] Đồng bộ Exported, version 1.2.0
- [ ] Code review

## Success criteria
- Giả lập trên save cũ: thấy đủ bước, lỗi chỉ đúng bước; thư mục SaveData thật giữ nguyên (so hash trước/sau).
- Mọi test pass; build Android/WebGL compile được.
