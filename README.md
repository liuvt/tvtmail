# TVT Mail

TVT Mail là webmail tự host viết lại độc lập bằng **ASP.NET Core Blazor + .NET 8** theo mô hình unified inbox: kết nối nhiều tài khoản IMAP, đồng bộ metadata về SQLite, xem hộp thư tổng, hộp thư riêng từng tài khoản và mở nội dung email.

> TVT Mail **không sao chép source MailFlow**. MailFlow chỉ được dùng làm tham chiếu về luồng sản phẩm/unified inbox. Repo MailFlow hiện dùng AGPL-3.0 hoặc commercial license, vì vậy project này được triển khai độc lập bằng C#/.NET.

## Chức năng bản MVP

- Đăng nhập trang quản trị TVT Mail bằng cookie.
- Thêm **nhiều tài khoản email IMAP**.
- Kiểm tra kết nối IMAP trước khi lưu tài khoản.
- Mật khẩu mailbox được mã hóa bằng **ASP.NET Core Data Protection** trước khi lưu SQLite.
- Đồng bộ thư nền định kỳ từ toàn bộ tài khoản.
- Đồng bộ nhiều mailbox song song, mặc định tối đa 4 account cùng lúc.
- **Hộp thư tổng**: gộp email của tất cả tài khoản, sắp xếp theo thời gian.
- **Hộp thư riêng**: xem email theo từng tài khoản.
- Tìm nhanh theo người gửi, địa chỉ gửi, tiêu đề hoặc mailbox.
- **Xem mail**: khi mở mail mới tải body đầy đủ từ IMAP rồi cache local.
- Hiện trạng thái mailbox, lần đồng bộ gần nhất và lỗi IMAP.
- Bật/tạm dừng/xóa mailbox.
- Giao diện responsive kiểu mail client.

## Kiến trúc

```text
Browser
   │
   ▼
Blazor Web App (Interactive Server)
   │
   ├── Cookie Admin Auth
   ├── Unified Inbox UI
   ├── Mailbox UI
   ├── Background Sync Service
   │       │
   │       └── MailKit IMAP
   │
   ├── EF Core
   │       └── SQLite: data/tvtmail.db
   │
   └── ASP.NET Core Data Protection
           └── DataProtection-Keys/
```

### Vì sao không đọc trực tiếp tất cả IMAP mỗi lần mở trang?

Nếu có vài chục hoặc vài trăm mailbox, cách đó rất chậm. TVT Mail chỉ dùng IMAP để đồng bộ. Header/metadata mail được cache SQLite để inbox tổng load nhanh. Body mail chỉ tải khi người dùng mở email lần đầu.

## Công nghệ

- .NET 8 / ASP.NET Core
- Blazor Web App - Interactive Server
- Entity Framework Core 8.0.31
- SQLite
- MailKit 4.17.0
- ASP.NET Core Data Protection

## Cấu hình Cock.li

Nếu tài khoản bạn nói là **Cock.li** (`@cock.li`), preset trong giao diện dùng:

```text
IMAP host : mail.cock.li
Port      : 993
Security  : SSL/TLS
Username  : địa chỉ email đầy đủ
Password  : mật khẩu mailbox
```

Nếu bạn thực sự dùng một nhà cung cấp/domain **cook.li** khác với Cock.li, không dùng preset trên. Hãy nhập đúng IMAP host/port của nhà cung cấp ở chế độ Custom.

## Chạy local

Yêu cầu .NET 8 SDK.

```bash
cd tvtmail
dotnet restore tvtmail.sln
dotnet run --project src/TvtMail/TvtMail.csproj
```

Mặc định app dùng tài khoản quản trị:

```text
Email:    admin@tvtmail.local
Password: ChangeMe-Immediately!
```

**Phải đổi mật khẩu trước khi public app lên Internet.**

Nên cấu hình bằng environment variable:

### Windows PowerShell

```powershell
$env:AdminAuth__Email="admin@your-domain.com"
$env:AdminAuth__Password="MAT_KHAU_RAT_MANH"
dotnet run --project .\src\TvtMail\TvtMail.csproj
```

### Linux

```bash
export AdminAuth__Email='admin@your-domain.com'
export AdminAuth__Password='MAT_KHAU_RAT_MANH'
dotnet run --project src/TvtMail/TvtMail.csproj
```

## Cấu hình đồng bộ

`src/TvtMail/appsettings.json`:

```json
"TvtMail": {
  "SyncIntervalSeconds": 60,
  "InitialSyncLimit": 0,
  "FetchBatchSize": 100,
  "MaxConcurrentAccountSyncs": 4
}
```

- `SyncIntervalSeconds`: chu kỳ background sync. Tối thiểu runtime đang ép 30 giây.
- `InitialSyncLimit`: `0` = lần đầu lấy toàn bộ thư hiện có trong INBOX; ví dụ `1000` = chỉ lấy 1000 thư mới nhất.
- `FetchBatchSize`: số mail metadata lấy mỗi batch IMAP.
- `MaxConcurrentAccountSyncs`: số mailbox được sync song song, nên giữ khoảng 3-6 trên VPS nhỏ.

## Publish Linux/VPS

```bash
cd tvtmail
dotnet restore tvtmail.sln
dotnet publish src/TvtMail/TvtMail.csproj -c Release -o publish
```

Copy thư mục `publish` lên:

```text
/var/www/tvtmail
```

Tạo quyền ghi cho database và Data Protection key:

```bash
sudo mkdir -p /var/www/tvtmail/data /var/www/tvtmail/DataProtection-Keys
sudo chown -R www-data:www-data /var/www/tvtmail
```

Tạo `/etc/tvtmail.env`:

```bash
AdminAuth__Email=admin@your-domain.com
AdminAuth__Password=DOI_THANH_MAT_KHAU_MANH
TvtMail__SyncIntervalSeconds=60
TvtMail__MaxConcurrentAccountSyncs=4
```

Sau đó dùng mẫu `deploy/tvtmail.service` và `deploy/nginx.conf.example`.

## Lệnh systemd

```bash
sudo cp deploy/tvtmail.service /etc/systemd/system/tvtmail.service
sudo systemctl daemon-reload
sudo systemctl enable --now tvtmail
sudo systemctl status tvtmail
```

Theo file mẫu, Kestrel chạy:

```text
127.0.0.1:4124
```

Xem log:

```bash
journalctl -u tvtmail -f
```

## Hạn chế của bản MVP

Bản này cố ý tập trung đúng nhu cầu ban đầu:

- Mới đồng bộ folder `INBOX`.
- Chưa gửi mail SMTP.
- Chưa Reply/Forward.
- Chưa tải attachment.
- Chưa render HTML mail trực tiếp; body hiển thị dạng text để tránh script/tracking HTML không tin cậy.
- Chưa có full-text search database.
- Chưa có IMAP IDLE realtime; đang background sync định kỳ.
- Chưa đồng bộ xóa/move mail hai chiều.

## Hướng nâng cấp tiếp theo

Ưu tiên hợp lý cho TVT Mail:

1. IMAP IDLE theo từng account để nhận mail gần realtime.
2. Folder Sent/Spam/Trash và cây thư mục.
3. Download attachment.
4. Mark read/unread, star, delete và move đồng bộ ngược lên IMAP.
5. Render HTML mail trong sandbox, khóa remote tracking image mặc định.
6. SMTP gửi mail, Reply, Forward.
7. Search toàn bộ mailbox.
8. Import hàng loạt account từ CSV/Excel theo dạng `email,password,imap_host,imap_port`.
9. Phân quyền nhiều user quản trị nếu cần.

## Lưu ý bảo mật

TVT Mail nắm giữ thông tin đăng nhập nhiều mailbox nên cần:

- Chỉ chạy sau HTTPS/reverse proxy.
- Không commit `data/`, `DataProtection-Keys/`, file env hoặc mật khẩu lên GitHub.
- Backup cả SQLite **và** thư mục `DataProtection-Keys`; mất key thì mật khẩu mailbox đã mã hóa có thể không giải mã lại được.
- Dùng mật khẩu quản trị mạnh.
- Hạn chế quyền filesystem của user chạy service.
- Không mở trực tiếp port Kestrel ra Internet nếu đã có Nginx.
