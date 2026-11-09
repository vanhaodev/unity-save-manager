# Research: Save Migrator (nâng cấp save cũ lên schema mới)

Ngày: 2026-11-09 · Phạm vi: library `com.vanhaodev.savemanager` v1.1.0

## 1. Hiện trạng code liên quan

- Format **binary theo vị trí** (`ISaveable.WriteSave/ReadSave`, `SaveReader` đọc tuần tự) — không có tên field, không JSON.
- `SaveHeader.VERSION` (byte) = version **format file của lib**, được đọc nhưng bỏ qua (`SaveHeader.cs:25`). Chưa có version **dữ liệu của game**.
- Chỉ key thay đổi mới được ghi → các key trên đĩa có **tuổi khác nhau** (key `settings` có thể ghi từ v2, `player` từ v7).
- Đã có commit nguyên tử nhiều file + `SaveKeyMigration` (đổi secret) chạy lúc `EnsureLoaded` → migrator có thể tái dùng y hệt: build changes → `Commit` 1 lần.
- Đọc sai layout hiện trả `Corrupt` (`SaveLoad.cs:119`) — đây chính là chỗ đổi data class làm hỏng save hôm nay.

## 2. Các game / hệ thống khác làm thế nào

| Hệ thống | Cách làm | Bài học cho lib |
|---|---|---|
| **Stardew Valley 1.6** | Mỗi big version 1 class `SaveMigrator_1_3`, `_1_4`, `_1_5`, `_1_6` cùng implement 1 interface; chạy tuần tự theo version save | Đúng ý "class theo big version" của bạn — nhưng làm bằng **interface**, không phải partial |
| **Minecraft** (DataFixerUpper) | Mỗi chunk/entity/player lưu `DataVersion` riêng; chuỗi fixer chạy từ version cũ → mới. Mở world của bản mới hơn: cảnh báo; world cũ: gợi ý **"Make backup and load"** | Version **theo từng mục** (≈ theo key); backup trước khi nâng cấp; hook "save mới hơn game" |
| **Unreal Engine** | `FCustomVersion` (GUID + int) theo subsystem; trong `Serialize`: `if (Ar.CustomVer(X) >= N) Ar << NewField;` | Với binary, 80% thay đổi giải quyết ngay trong `ReadSave` bằng `r.Version` |
| **Factorio** | Script migration theo version, áp dụng theo thứ tự, có log; save bản mới hơn → **từ chối load** | Chặn downgrade mặc định; log từng bước |
| **Android Room** | `Migration(from, to)`; thiếu đường đi → throw; `fallbackToDestructiveMigration` tuỳ chọn | Kiểm tra **thiếu bước** trước khi chạy; policy rõ cho save quá cũ |
| **redux-persist** | `migrations = { 2: s => ..., 3: s => ... }`, lưu `version`, chạy các bước > version cũ | API đăng ký đơn giản: map version → hàm |

## 3. Đánh giá ý tưởng của bạn

| Ý tưởng | Đánh giá |
|---|---|
| Chuỗi bước V1→V2→…→V10 (while loop) | ✅ Đúng, mọi hệ thống trên đều làm vậy. Nhưng nên là **for qua danh sách đã đăng ký + kiểm tra liên tục**, không while mù (tránh lặp vô hạn / nhảy cóc). |
| Ví dụ `JObject` đổi `image` → `avatar` | ⚠️ Không khớp lib: lib là binary, không có JSON/Newtonsoft. Và trong binary **đổi tên field không cần migrate** (chỉ đổi tên field C#, byte y nguyên). Thứ cần migrate: thêm/xoá/đổi thứ tự field, đổi kiểu, tách/gộp/đổi tên **key**, tính lại dữ liệu. |
| `partial class` để user chia file | ❌ `partial` chỉ ghép được trong **cùng assembly**. Lib nằm ở asmdef riêng → user không thể "partial" vào class của lib. User vẫn tự dùng partial trong code của họ được, nhưng lib nên đưa ra **interface** (kiểu Stardew). |
| Migrator sync (đóng băng) hoặc await | ✅ Hợp lý. Sync cho màn loading đơn giản, `await` cho có progress bar. |
| Hook "game cũ hơn save" | ✅ Rất nên. Mặc định phải **chặn ghi** để bản cũ không ghi đè mất dữ liệu bản mới. |

## 4. Đề xuất thiết kế (2 tầng, 1 con số version)

### Một con số duy nhất: `Save.SchemaVersion`
```csharp
Save.SchemaVersion = 10;   // tăng khi format dữ liệu game đổi; KHÔNG dùng Application.version
```
- Mỗi file key được đóng dấu schema lúc nó được ghi (header lib v2; file cũ v1 = schema 0).
- Manifest đóng dấu schema của cả save lúc commit.
- `Application.version` chỉ lưu kèm để hiển thị/analytics, không điều khiển logic (patch không đổi data cũng đổi version).

### Tầng 1 — đọc theo version trong `ReadSave` (đa số trường hợp, không cần migrator)
```csharp
public void ReadSave(SaveReader r)
{
    Name = r.ReadString();
    Gold = r.ReadInt();
    Avatar = r.Version >= 4 ? r.ReadString() : "default";   // field thêm ở v4
}
```
- Load xong, key cũ hơn được lib **tự ghi lại** ở schema hiện tại (lazily, lần save kế tiếp hoặc ngay sau migrate).

### Tầng 2 — bước migrate cả save (đổi cấu trúc, nhiều key)
```csharp
// User tổ chức tuỳ ý — 1 class / big version, có thể partial trong code của user
public sealed class Migrations_V1 : ISaveMigrationSet
{
    public void Register(SaveMigrations m)
    {
        m.Add(from: 3, MigrateV3ToV4);
        m.Add(from: 4, MigrateV4ToV5);
    }

    static void MigrateV3ToV4(SaveMigrationContext ctx)
    {
        var old = ctx.Get<PlayerV3>("player");      // class cũ giữ lại để đọc
        ctx.Set("player", new Player { Name = old.Name, Avatar = old.Image });
        ctx.Rename("coins", "gold");
        ctx.Delete("tutorial_cache");
    }
}

Save.Migrations.Register(new Migrations_V1(), new Migrations_V2());
```
- Không dùng reflection/attribute tự quét: IL2CPP strip code, khởi động chậm, thứ tự ngầm. Đăng ký rõ ràng an toàn hơn.
- `ctx.Get` thấy cả thay đổi của bước trước (bước 5 đọc được kết quả bước 4) — dữ liệu giữ trong bộ nhớ.
- **Tất cả bước commit 1 lần duy nhất** cuối cùng (dùng cơ chế commit hiện có): crash/exception giữa chừng → save cũ nguyên vẹn, lần sau chạy lại từ đầu. Đây là lợi thế lớn mà lib sẵn có.

### Chạy migrate
```csharp
// Đóng băng (đơn giản, cho màn loading)
var result = Save.Migrate();

// Không đóng băng + progress
var result = await Save.MigrateAsync(p => loadingBar.value = p);
```
- Nếu user không gọi, lần truy cập Save đầu tiên tự chạy `Migrate()` sync + log warning (giống `EnsureLoaded` hiện tại) → không bao giờ đọc nhầm dữ liệu chưa migrate.
- Migrate là **eager cho cả save**, không lazy theo từng key khi `Get`: giữ đúng cam kết "mọi key load từ cùng 1 save".

### Kết quả & hook
`SaveMigrationResult`: `UpToDate` / `Migrated` / `NewerSave` / `TooOld` / `Failed`.

| Hook | Khi nào | Dùng để |
|---|---|---|
| `Save.NewerSaveDetected (saveVer, gameVer)` | Save schema > game | Popup "Hãy cập nhật game". **Mặc định: chỉ đọc, chặn ghi** (policy: `ReadOnly` / `Refuse` / `Allow`) |
| `Save.MigrationStarted (from, to)` | Bắt đầu | Hiện loading, analytics |
| `Save.MigrationStepCompleted (step, progress)` | Mỗi bước xong | Progress bar, log |
| `Save.MigrationCompleted (from, to)` | Commit xong | Analytics "bao nhiêu người lên từ v3" |
| `Save.MigrationFailed (from, step, error)` | Bước throw | Save cũ còn nguyên; game chọn: báo lỗi / chơi chế độ an toàn |
| `Save.SaveTooOld (saveVer, minSupported)` | Save < `MinSupportedSchema` (đã xoá migrator cũ) | Reset / quarantine (không bao giờ xoá thầm) |

## 5. Tính năng bổ sung đáng có (ngoài ý tưởng ban đầu)

1. **Backup trước khi migrate** (Minecraft): copy save cũ vào `SaveData/backups/schema-3/` trước commit. Commit nguyên tử chỉ chống crash; lỗi phổ biến nhất là **migrator có bug logic** chạy thành công nhưng sai dữ liệu → backup cứu được. Giữ N bản gần nhất.
2. **Kiểm tra chuỗi lúc đăng ký**: thiếu bước (có 3→4, 5→6 nhưng không có 4→5), trùng bước, bước vượt `SchemaVersion` → lỗi ngay khi Register (dev thấy trong Editor), không đợi tới máy người chơi.
3. **Chặn ghi khi save mới hơn game**: tránh bản cũ ghi đè làm mất tiến trình (lỗi kinh điển khi người chơi cài lại APK cũ).
4. **Test bằng save mẫu (golden saves)**: Editor menu "Export save fixture" + helper `SaveMigrationTest.Run(fixtureFolder)` → mỗi bản release lưu 1 bộ save thật, test migrate lên bản mới nhất. Đây là cách các studio bắt bug migrator.
5. **Log chi tiết**: mỗi bước, key nào bị đổi/xoá — giúp debug báo lỗi người chơi.
6. (Tuỳ chọn, sau) Editor: dry-run migrate trên thư mục save, xem trước diff key.

## 6. Thứ tự làm đề xuất (theo tính năng)

1. **Version stamp + `r.Version` + chặn save mới hơn + hook NewerSave** — nhỏ, giải quyết phần lớn nhu cầu, cần đổi format header/manifest (đọc được file cũ).
2. **Chuỗi bước migrate nguyên tử + `Migrate()`/`MigrateAsync()` + hook + kiểm tra chuỗi.**
3. **Backup trước migrate + `MinSupportedSchema`/TooOld.**
4. **Helper test bằng save mẫu + công cụ Editor.**

## 7. Rủi ro

- Đổi header file & manifest (VERSION 1→2): phải đọc được save v1.1.0 hiện có (coi là schema 0). Chữ ký/fingerprint không ảnh hưởng vì nằm trong body đã ký.
- `MigrateAsync` chạy code user trên thread nền → code migrator không được gọi Unity API. WebGL không có thread → chạy rải theo frame (giống save hiện tại).
- Giữ class cũ (`PlayerV3`) để đọc trong migrator: code phình theo thời gian → `MinSupportedSchema` cho phép xoá dần.
- Migrate đổi secret (`SaveKeyMigration`) và migrate schema cùng chạy lúc load: nên gộp vào 1 commit để không ghi save 2 lần.

## Quyết định (user, 2026-11-09)

- Chấp nhận: backup, kiểm tra chuỗi, MinSupportedSchema, test save mẫu.
- `MigrateAsync`: có cả 2 chế độ cho user chọn — `Background` (thread nền, cấm Unity API) / `MainThread` (rải theo frame). WebGL luôn MainThread.
- Save mới hơn game: lib chỉ bắn hook `NewerSaveDetected` + chặn ghi mặc định; user quyết (không cho vào / cho vào kèm cảnh báo bằng `AllowWrite()`). Không ném exception: đọc layout lạ → `Corrupt` như hiện tại. Trước lần ghi đầu tiên lên save mới hơn → backup tự động.
- ~~Làm cả 2 tầng~~ → **Bỏ Tầng 1** (`r.Version` trong ReadSave) — user thấy rối. Mọi thay đổi data đều qua bước migrate (Tầng 2); bước đọc dữ liệu cũ bằng class cũ (`PlayerV3`) hoặc đọc thô `ctx.Read(key, r => ...)`.
- Backup = lib tự copy toàn bộ save hiện tại (manifest + file .sav) vào `SaveData/backups/schema-<cũ>/` ngay trước khi commit migrate. Hiện lib chưa có backup (file thế hệ cũ bị xoá sau commit).
- Backup luôn bật; giữ N bản gần nhất (mặc định 3, chỉnh được).
- Version: 1 số `SchemaVersion` cho cả game, lib tự đóng dấu vào từng key khi ghi.

## Câu hỏi chưa giải quyết (bản đầu, đã trả lời ở trên)

1. `MigrateAsync`: chạy migrator trên thread nền (nhanh, cấm Unity API) hay main thread rải theo frame (an toàn, có thể giật mỗi bước)?
2. Mặc định khi save mới hơn game: `ReadOnly` (chơi được, không lưu) hay `Refuse` (không load)?
3. Có làm Tầng 1 (`r.Version` trong `ReadSave`) không, hay chỉ muốn chuỗi bước (Tầng 2)? Đề xuất làm cả hai, Tầng 1 trước.
4. Backup trước migrate: bật mặc định? Giữ bao nhiêu bản?
5. Version là 1 số cho cả game (đề xuất) hay mỗi key/class tự khai báo version riêng (kiểu Unreal/Minecraft)?
