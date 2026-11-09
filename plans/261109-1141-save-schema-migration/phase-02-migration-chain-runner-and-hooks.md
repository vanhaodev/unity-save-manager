# Phase 2 — Chuỗi bước migrate + chạy đồng bộ / await + hook

## Overview
Ưu tiên: cao · Trạng thái: chưa làm · Phụ thuộc phase 1.

## API cho user
```csharp
public sealed class MigrationsV1 : ISaveMigrationSet          // user tự chia class theo big version
{
    public void Register(SaveMigrations m)
    {
        m.Add(from: 3, MigrateV3ToV4);
        m.Add(from: 4, MigrateV4ToV5);
    }

    static void MigrateV3ToV4(SaveMigrationContext ctx)
    {
        var old = ctx.Get<PlayerV3>("player");
        ctx.Set("player", new Player { Name = old.Name, Avatar = old.Image });
        ctx.Rename("coins", "gold");
        ctx.Delete("tutorial_cache");
    }
}

Save.SchemaVersion = 5;
Save.Migrations.Register(new MigrationsV1());

var result = Save.Migrate();                                                      // đóng băng
var result = await Save.MigrateAsync(p => bar.value = p);                          // nền (mặc định)
var result = await Save.MigrateAsync(p => bar.value = p, SaveMigrationThread.MainThread); // rải theo frame
```

### Cấu hình 1 chỗ (`ISaveSetup`) — để Editor giả lập dùng lại được
```csharp
public sealed class GameSaveSetup : ISaveSetup
{
    public void Configure(SaveConfig c)
    {
        c.SetKey("secret-v3", "secret-v2");
        c.SchemaVersion = 5;
        c.Migrations(new MigrationsV1(), new MigrationsV2());
        c.Verify(ctx => ctx.Get<Player>("player"));   // tuỳ chọn: kiểm tra sau migrate
    }
}

Save.Setup(new GameSaveSetup());   // game gọi 1 lần lúc khởi động
```
- `Verify` chạy sau bước cuối, trước khi lưu: throw → coi như migrate lỗi, không ghi gì. Dùng cả lúc thật lẫn giả lập.
- Cách cũ (`SaveEncryption.SetKey`, `Save.SchemaVersion`, `Save.Migrations.Register`) vẫn dùng được; `ISaveSetup` chỉ gom lại.

### Hàm migrate là hàm instance, lib tự `new` khi cần (user yêu cầu 2026-11-09)
- Đăng ký bằng kiểu: `c.AddMigrations<MigrationsV1>()` (ràng buộc `new()`), không truyền instance.
- Save đã mới nhất → **không tạo** class migrate nào.
- Cần migrate → lib `new` các class, gọi `Register`, chạy, rồi bỏ tham chiếu (GC dọn).
- Kiểm tra chuỗi (thiếu/trùng bước): chạy lúc migrate; trong Editor chạy thêm lúc vào Play Mode để dev thấy lỗi sớm.
- Class có thể giữ field dùng chung giữa các bước của nó (sống trong 1 lần migrate).

### Save rất cũ (người chơi quay lại sau nhiều năm)
- Lib không giới hạn độ dài chuỗi: chỉ chạy 1 lần, dữ liệu trong bộ nhớ, lưu 1 lần → 50 bước vẫn rất nhanh.
- Bước nhảy cóc để gộp bước cũ: `m.Add(from: 0, to: 30, fn)` (mặc định `to = from + 1`). Dev xoá các bước 0..29 nhỏ, thay bằng 1 bước → bớt code + class cũ. Kiểm tra chuỗi tính cả bước nhảy.
- Ngừng hỗ trợ: xoá luôn các bước cũ → save cũ hơn bước nhỏ nhất → `SaveTooOld` (game quyết: chơi mới / thông báo). Lib không tự xoá save.
- Snapshot `schema-0/` trong git giữ cho chuỗi luôn được test từ version đầu tiên.

## Requirements
- `SaveMigrationContext`: `Get<T>(key)`, `Read<T>(key, Func<SaveReader,T>)`, `Set(key, ISaveable, encryption?)` (mặc định giữ loại mã hoá cũ của key, key mới = AES), `Rename(from, to)`, `Delete(key)`, `Exists(key)`, `Keys`, `FromVersion`, `ToVersion`.
- Bước sau thấy thay đổi của bước trước (lớp phủ trong bộ nhớ trên save gốc).
- Kiểm tra lúc `Register`: trùng `from`, `from >= SchemaVersion`, chuỗi đứt (thiếu bước giữa min `from` và `SchemaVersion`) → `ArgumentException` ngay.
- Chạy: for từ schema save tới `SchemaVersion - 1`, mỗi bước đúng 1 lần, theo thứ tự.
- Kết thúc: 1 commit duy nhất (gộp luôn đổi secret nếu cần) với schema = `SchemaVersion`.
- Kết quả `SaveMigrationResult`: `UpToDate`, `Migrated`, `NewerSave`, `TooOld`, `Failed`.
- Save < `from` nhỏ nhất đã đăng ký (và < SchemaVersion) → `TooOld`, hook `Save.SaveTooOld(e)`, chặn ghi (user `AllowWrite()` được).
- Bước throw → không ghi gì, hook `Save.MigrationFailed(from, step, error)`, chặn ghi (tránh trộn dữ liệu cũ/mới), thử lại lần mở game sau.
- Hook: `MigrationStarted(from, to)`, `MigrationStepCompleted(step, progress 0..1)`, `MigrationCompleted(from, to)`.
- Không gọi `Migrate` mà đã đăng ký bước → lần truy cập Save đầu tiên tự chạy `Migrate()` đồng bộ + log warning.
- Trong lúc migrate (async) mọi `Get`/`Set` đợi/từ chối: `Get` trước khi xong → chạy nốt đồng bộ (giống WebGL Flush hiện tại). Giữ đơn giản: gọi Save trong lúc `MigrateAsync` chưa xong → `InvalidOperationException` với thông điệp rõ.
- `Background`: bước chạy trên thread pool; WebGL tự chuyển `MainThread`. `MainThread`: 1 bước / frame.

## Architecture
- Tạo `Migration/` folder:
  - `ISaveMigrationSet.cs`, `SaveMigrations.cs` (đăng ký + kiểm tra chuỗi)
  - `SaveMigrationContext.cs` (lớp phủ: key → bytes | null | rename)
  - `SaveMigrationRunner.cs` (vòng lặp, sync/async, gom changes → `SaveManifestStore.Commit`)
  - `SaveMigrationResult.cs`, `SaveMigrationThread.cs`
  - `SaveMigrate.cs` (partial `Save`: `Migrations`, `Migrate`, `MigrateAsync`, hook)
- Đọc key trong context: dùng `ReadVerified` + giải mã như `SaveLoad.Deserialize` → tách hàm dùng chung (DRY).
- Commit dưới `IoLock`; hook qua dispatcher main thread sẵn có.

## Implementation steps
1. Đăng ký + kiểm tra chuỗi.
2. Context + lớp phủ, tách hàm serialize/deserialize dùng chung với `Save`.
3. Runner sync → commit 1 lần, gộp đổi secret.
4. Kết quả + hook + chặn ghi khi TooOld/Failed.
5. Tự chạy khi user quên gọi.
6. `MigrateAsync` 2 chế độ + progress; WebGL fallback.

## Todo
- [ ] Đăng ký bước theo nhóm class, báo lỗi chuỗi sai ngay
- [ ] Context đọc/ghi/đổi tên/xoá key
- [ ] Chạy lần lượt, lưu 1 lần
- [ ] Đóng băng / await nền / await rải frame
- [ ] Hook started / step / completed / failed / too old
- [ ] Compile sạch

## Success criteria
- Save đã mới nhất: `Migrate()` / `MigrateAsync()` không đọc file, không tạo object migrate, Task hoàn thành ngay (đo bằng test: 0 lần gọi `Register`).
- Save schema 1, game schema 5, có bước 1..4 → chạy đủ 4 bước, đúng thứ tự, 1 lần ghi, manifest schema 5.
- Bước 3 throw → save trên đĩa y nguyên schema 1, hook failed, ghi bị chặn.
- Tắt game giữa lúc migrate → lần sau chạy lại từ schema 1.

## Risks
- User gọi Unity API trong bước khi chạy nền → crash khó hiểu. Giảm: ghi rõ README + XML doc; mặc định `Background` hay `MainThread`? → chọn `MainThread` mặc định nếu muốn an toàn (xem câu hỏi).
- Save lớn: lớp phủ giữ bytes trong RAM — save nhỏ nên chấp nhận.

## Đã chốt
- Mặc định `MigrateAsync` = `MainThread` (user đồng ý 2026-11-09).
