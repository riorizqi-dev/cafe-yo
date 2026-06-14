# 📖 Dokumentasi Lengkap Projek Cafe Yo

## Informasi Umum

| Item | Detail |
|------|--------|
| Nama Projek | **Cafe Yo** |
| Framework | ASP.NET Core 8 (MVC + Web API) |
| Database | SQL Server (LocalDB) |
| Autentikasi | ASP.NET Core Identity |
| Payment Gateway | BayarGG (QRIS) |
| Bahasa | C# (.NET 8) |

---

## 🔄 Alur Sistem (Flow Utama)

```
┌─────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  CUSTOMER   │────▶│   PAYMENT    │────▶│   KITCHEN    │────▶│   CASHIER    │
│  Pesan Menu │     │  QRIS/Kasir  │     │  Proses Order│     │  Selesaikan  │
└─────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
       │                    │                    │                    │
       ▼                    ▼                    ▼                    ▼
  Pilih Meja          Bayar QRIS/         Koki mulai           Notifikasi
  Pilih Menu          Tunai di Kasir      masak, stok          "Siap Antar"
  Checkout                                auto berkurang        ke Customer
```

### Alur Detail Step-by-Step

1. **Customer scan QR / buka website** → masuk halaman menu (`/?meja=5`)
2. **Pilih meja** → status meja berubah jadi "Booking"
3. **Pilih menu, tambah ke keranjang** → bisa lihat harga, stok, gambar
4. **Checkout** → pilih metode bayar (QRIS atau Bayar di Kasir)
5. **Pembayaran**:
   - QRIS → sistem buat invoice ke BayarGG → customer scan QR → callback otomatis
   - Kasir → kasir konfirmasi manual di dashboard kasir
6. **Order masuk dapur** → status `pending` di Kitchen Display
7. **Koki klik "Mulai"** → status jadi `processing`, stok bahan otomatis berkurang
8. **Koki klik "Selesai"** → status jadi `ready`, notifikasi dikirim ke kasir
9. **Kasir terima notifikasi** → panggil customer / antar pesanan
10. **Kasir selesaikan order** → meja kembali "Kosong"

---

## 👥 Role & Hak Akses

| Role | Dashboard | Akses Utama |
|------|-----------|-------------|
| **Admin** | `/admin` | Semua fitur admin (user, role, menu, stok, meja, order, FAQ, settings) |
| **Owner** | `/owner` | Laporan keuangan, analytics, approval menu, export CSV |
| **Supervisor** | `/supervisor` | Monitoring operasional, bahan baku, resep, inventaris, alert |
| **Kasir** | `/kasir` | Buat order, konfirmasi pembayaran, kelola meja, notifikasi dapur |
| **Koki/Dapur** | `/kitchen` | Kitchen Display System (KDS), proses pesanan |
| **Customer** | `/` | Pesan menu, lihat status pesanan, chatbot |

---

## 📱 Fitur Lengkap Per Role

---

### 1. 🏠 Halaman Customer (Homepage)

**URL:** `/` atau `/?meja=5`

**Fitur:**
- Lihat daftar menu lengkap (nama, gambar, harga, kategori, stok)
- Filter menu berdasarkan kategori
- Tambah item ke keranjang
- Pilih meja (otomatis dari QR code atau manual)
- Pilih tipe pesanan: Dine-in atau Take Away
- Membership discount (10%-20% tergantung jumlah item)
- Checkout dengan pilihan QRIS atau bayar di kasir
- Halaman "Pesanan Saya" (`/pesanan-saya`) untuk tracking status real-time
- Chatbot widget untuk tanya menu, harga, stok, FAQ

**Screenshot placeholder:**
> 📸 `[SS: Halaman utama menu customer dengan grid menu dan keranjang]`
> 📸 `[SS: Modal checkout dengan pilihan pembayaran QRIS/Kasir]`
> 📸 `[SS: Halaman pesanan saya dengan status tracking]`

---

### 2. 🔐 Halaman Login

**URL:** `/staff` (staff) dan `/Auth` (customer)

**Fitur:**
- Login staff dengan username & password (AJAX, tanpa reload)
- Register customer baru (otomatis dapat role Customer)
- Auto-redirect berdasarkan role setelah login
- Logout dengan clear cookies

**Screenshot placeholder:**
> 📸 `[SS: Halaman login staff dengan form username/password]`
> 📸 `[SS: Halaman auth customer dengan tab Login/Register]`

---

### 3. 💰 Dashboard Kasir

**URL:** `/kasir`

**Fitur:**
- **Peta Meja** — Lihat semua meja dengan status warna (Kosong/Booking/Terisi)
- **Buat Order Baru** — Pilih meja → pilih menu → tentukan jumlah → submit
- **Pilih Metode Bayar** — QRIS (auto generate QR) atau Kasir (tunai)
- **Konfirmasi Pembayaran** — Untuk order yang bayar tunai
- **Daftar Pending Payment** — Lihat semua order yang belum lunas
- **Notifikasi Dapur** — Badge + toast saat pesanan siap dari dapur
- **Acknowledge Notifikasi** — Klik untuk tandai sudah diterima
- **Panggil Customer** — Kirim notifikasi "Pesanan Siap" ke meja tertentu
- **Selesaikan Order** — Tandai order selesai
- **Selesaikan Meja** — Selesaikan semua order di satu meja sekaligus
- **Batalkan Order** — Cancel order + notifikasi ke supervisor

**Screenshot placeholder:**
> 📸 `[SS: Dashboard kasir dengan grid meja berwarna]`
> 📸 `[SS: Form buat order baru dengan daftar menu]`
> 📸 `[SS: Panel notifikasi dapur dengan badge merah]`
> 📸 `[SS: Modal konfirmasi pembayaran tunai]`

---

### 4. 🍳 Kitchen Display System (KDS)

**URL:** `/kitchen`

**Fitur:**
- **3 Kolom Kanban**: Menunggu | Diproses | Siap
- **Card Order** — Menampilkan: nomor meja, daftar item + qty, catatan, waktu order
- **Tombol "Mulai"** — Pindahkan order dari Menunggu ke Diproses
  - Otomatis kurangi stok bahan baku sesuai resep
  - Jika stok tidak cukup → order ditolak + notifikasi ke Supervisor
- **Tombol "Tandai Selesai"** — Pindahkan dari Diproses ke Siap
  - Otomatis kirim notifikasi ke Kasir
- **Auto-refresh** — Polling otomatis untuk update real-time
- **Concurrency control** — Mencegah 2 koki update order yang sama

**Screenshot placeholder:**
> 📸 `[SS: Kitchen Display dengan 3 kolom kanban (Pending/Processing/Ready)]`
> 📸 `[SS: Card order detail dengan item dan tombol aksi]`
> 📸 `[SS: Alert stok tidak cukup saat mulai proses]`

---

### 5. 📊 Dashboard Supervisor

**URL:** `/supervisor`

**Fitur:**
- **Dashboard Summary** — Total bahan, stok menipis, stok habis, menu tidak bisa dijual
- **Kelola Bahan Baku** (`/Supervisor/Ingredients`):
  - CRUD bahan (nama, tipe, stok, minimal stok, satuan, harga beli)
  - Adjust stok manual (tambah/kurang)
  - Aktifkan/nonaktifkan bahan
  - Filter bahan kritis (stok ≤ minimal)
- **Kelola Resep** (`/Supervisor/Recipes`):
  - Mapping menu → bahan baku + jumlah yang dibutuhkan
  - Indikator "bisa dijual" berdasarkan ketersediaan bahan
- **Log Pemakaian Stok** (`/Supervisor/UsageLogs`):
  - Riwayat pemakaian otomatis per order
  - Info: order, menu, bahan, qty terpakai, sisa stok, koki
- **Inventaris Fisik** (`/Supervisor/Inventory`):
  - Barang inventaris (meja, kursi, peralatan, dll)
  - Tracking: total/baik/rusak/hilang
  - Log kerusakan & kehilangan
- **Log Bahan Expired** — Catat bahan yang dibuang karena kadaluarsa
- **Monitoring Order** (`/Supervisor/Orders`):
  - Lihat semua order dengan timeline lengkap
  - Status pembayaran dan dapur
- **Alert Operasional** (`/Supervisor/Alerts`):
  - Stok menipis/habis
  - Order yang perlu perhatian

**Screenshot placeholder:**
> 📸 `[SS: Dashboard supervisor dengan summary cards]`
> 📸 `[SS: Halaman kelola bahan baku dengan tabel dan tombol aksi]`
> 📸 `[SS: Halaman resep dengan mapping menu-bahan]`
> 📸 `[SS: Log pemakaian stok otomatis]`
> 📸 `[SS: Alert operasional (stok menipis, order pending)]`

---

### 6. 📈 Dashboard Owner

**URL:** `/owner`

**Fitur:**
- **Ringkasan Omzet** — Hari ini, 7 hari, bulanan
- **Jumlah Transaksi** — Per periode
- **Rata-rata Transaksi** — Per order dan per customer
- **Grafik Omzet** — Line chart 7 hari terakhir
- **Grafik Transaksi** — Jumlah transaksi per hari
- **Top 5 Produk Terlaris** — Berdasarkan qty terjual
- **Produk Kurang Laku** — 5 produk paling sedikit terjual
- **Top Profit Produk** — Estimasi margin per produk
- **Revenue per Kategori** — Pie chart (Makanan/Minuman/Jajanan)
- **Jam & Hari Tersibuk** — Analisis waktu ramai
- **Estimasi HPP & Profit** — Berdasarkan harga beli bahan
- **Pengeluaran** — Dari log bahan expired
- **Transaksi Terakhir** — 8 transaksi terbaru
- **Business Alerts** — Stok habis, bahan expired, barang rusak
- **Auto Insights** — Rekomendasi otomatis berdasarkan data
- **Export CSV** — Download laporan (harian/mingguan/bulanan)
- **Kontrol Menu** (`/Owner/MenuControl`):
  - Approve/reject menu item
  - Tambah catatan approval
- **Halaman Analytics** (`/Owner/Analytics`) — Visualisasi data lanjutan
- **Halaman Reports** (`/Owner/Reports`) — Laporan detail

**Screenshot placeholder:**
> 📸 `[SS: Dashboard owner dengan cards omzet dan grafik]`
> 📸 `[SS: Grafik line chart omzet 7 hari]`
> 📸 `[SS: Top produk dan revenue per kategori]`
> 📸 `[SS: Business alerts dan auto insights]`
> 📸 `[SS: Halaman kontrol menu (approve/reject)]`

---

### 7. ⚙️ Admin Panel

**URL:** `/admin`

#### 7a. Manajemen User (`/Admin/Users`)
- Lihat semua user dengan role
- Buat user staff baru (username, nama, password, pilih role)
- Edit role user (multi-role checkbox)
- Lock/Unlock akun
- Reset password ke default
- Hapus user

> 📸 `[SS: Daftar user dengan kolom role dan tombol aksi]`
> 📸 `[SS: Form buat user baru dengan dropdown role]`

#### 7b. Manajemen Role (`/Admin/Roles`)
- Lihat semua role yang ada
- Buat role baru
- Reset ke Admin-only (hapus semua role & user kecuali admin)

> 📸 `[SS: Halaman roles dengan list dan form tambah]`

#### 7c. Manajemen Menu (`/Admin/MenuItems`)
- Lihat semua menu (nama, kategori, harga, stok, status, gambar)
- Tambah menu baru dengan upload gambar
- Edit menu (nama, kategori, harga, stok, ketersediaan, gambar)
- Hapus menu
- Format gambar: jpg, png, webp (max 2MB)

> 📸 `[SS: Daftar menu dengan gambar thumbnail dan info]`
> 📸 `[SS: Form tambah/edit menu dengan upload gambar]`

#### 7d. Manajemen Kategori (`/Admin/Category`)
- Lihat semua kategori menu
- Tambah kategori baru
- Toggle aktif/nonaktif
- Hapus kategori
- Auto-sync dari menu items yang sudah ada

> 📸 `[SS: Daftar kategori dengan toggle dan tombol hapus]`

#### 7e. Manajemen Stok (`/Admin/StockItems`)
- Lihat semua item stok (nama, tipe, qty, minimal qty)
- Tambah item stok baru
- Edit item stok
- Hapus item stok

> 📸 `[SS: Daftar stok items dengan quantity dan status]`

#### 7f. Manajemen Meja (`/Admin/Tables`)
- Lihat semua meja dengan status real-time
- Tambah meja baru
- Edit nomor meja & status
- Hapus meja

> 📸 `[SS: Daftar meja dengan nomor dan status]`

#### 7g. Manajemen Order (`/Admin/Orders`)
- Lihat semua order (ID, meja, tanggal, status, total)
- Detail order dengan item-item pesanan
- Edit status order

> 📸 `[SS: Daftar order dengan filter dan detail]`

#### 7h. Manajemen FAQ (`/Admin/Faq`)
- Lihat semua FAQ (pertanyaan, jawaban, keywords, urutan)
- Tambah FAQ baru
- Toggle aktif/nonaktif
- Hapus FAQ
- Digunakan oleh chatbot untuk menjawab pertanyaan customer

> 📸 `[SS: Daftar FAQ dengan form tambah]`

#### 7i. Settings (`/Admin/Settings`)
- Tax Percent (pajak)
- Service Charge Percent
- QRIS Image URL (gambar QR untuk pembayaran)

> 📸 `[SS: Halaman settings dengan form input]`

---

### 8. 🤖 Chatbot

**URL:** Widget di semua halaman customer (partial `_ChatbotWidget.cshtml`)
**API:** `POST /api/chatbot/ask`

**Fitur:**
- Tanya harga menu → "Harga Americano adalah Rp25.000"
- Cek stok menu → "Americano tersedia. Stok saat ini: 15"
- Lihat daftar menu → "Berikut daftar menu yang tersedia: ..."
- Lihat menu per kategori → "daftar menu kopi"
- FAQ otomatis:
  - Cara pesan
  - Metode pembayaran
  - Jam operasional
  - Dine-in / Take away
- FAQ custom dari database (dikelola admin)
- Fallback response jika tidak mengerti
- Semua percakapan di-log ke database

**Cara Kerja:**
1. User ketik pertanyaan
2. Sistem cek FAQ database (keyword scoring, threshold ≥45)
3. Jika tidak match → cek default FAQ (hardcoded)
4. Jika tidak match → cek intent "list menu"
5. Jika tidak match → fuzzy search menu items (token matching, threshold ≥25)
6. Jika tidak match → fallback response

> 📸 `[SS: Widget chatbot di pojok kanan bawah]`
> 📸 `[SS: Percakapan chatbot tanya harga menu]`
> 📸 `[SS: Chatbot menampilkan daftar menu]`

---

### 9. 💳 Sistem Pembayaran

**Payment Gateway:** BayarGG (https://www.bayar.gg)

**Metode:**
- **QRIS** — Generate QR code via BayarGG API, customer scan, callback otomatis
- **Kasir/Tunai** — Customer bayar langsung, kasir konfirmasi manual

**Alur QRIS:**
1. Order dibuat → `POST /api/payments/orders/{id}/create`
2. BayarGG return QR string + payment URL
3. Customer scan QR / buka payment URL
4. BayarGG kirim callback ke `/api/payments/gateway/callback`
5. Sistem auto-update: PaymentStatus → "lunas", Order → "diproses"

**Fitur Tambahan:**
- Refresh status pembayaran (polling)
- Check invoice manual
- List semua payment dari gateway (admin/owner)
- Fee gateway configurable (default Rp200)
- Member discount: 10% (1-2 item), 15% (3-5 item), 20% (6+ item)

> 📸 `[SS: QR code pembayaran QRIS]`
> 📸 `[SS: Status pembayaran "Menunggu" → "Lunas"]`

---

### 10. 🔔 Sistem Notifikasi

**Jenis Notifikasi:**

| Dari | Ke | Trigger | Pesan |
|------|----|---------|-------|
| Kitchen | Kasir | Order siap | "Pesanan Meja X / Order #Y sudah siap" |
| Kasir | Customer | Panggil customer | "Pesanan Anda sudah siap. Silakan ke meja X" |
| Kitchen | Supervisor | Stok tidak cukup | "Bahan tidak cukup untuk memproses pesanan" |
| Kasir | Supervisor | Order dibatalkan | "Order #X dibatalkan oleh kasir" |

**Endpoint:**
- `GET /cashier/notifications` — Notifikasi dapur untuk kasir
- `POST /cashier/notifications/{id}/ack` — Acknowledge notifikasi
- `GET /api/notifications` — Notifikasi umum per role
- `GET /api/table-alerts/latest?tableNumber=X` — Alert untuk customer di meja

> 📸 `[SS: Badge notifikasi di dashboard kasir]`
> 📸 `[SS: Toast notification "Pesanan siap"]`

---

### 11. 🪑 Manajemen Meja

**Status Meja:**
- **Kosong** — Tidak ada order aktif
- **Booking** — Customer sudah pilih meja tapi belum order
- **Terisi** — Ada order aktif di meja tersebut

**Auto-Sync:**
- Status meja otomatis update berdasarkan order aktif
- Saat semua order di meja selesai → meja kembali "Kosong"
- Customer bisa select/release meja via API

**Akses QR Code:**
- URL: `/?meja=5` → otomatis set meja 5 untuk customer
- Cookie `nr_tableNumber` menyimpan nomor meja

> 📸 `[SS: Grid meja dengan warna status (hijau/kuning/merah)]`

---

## 🗄️ Struktur Database (Tabel Utama)

| Tabel | Fungsi |
|-------|--------|
| `AspNetUsers` | User Identity (login) |
| `AspNetRoles` | Role definitions |
| `AspNetUserRoles` | User-role mapping |
| `MenuItems` | Daftar menu (nama, harga, stok, gambar, kategori) |
| `MenuCategories` | Kategori menu |
| `MenuIngredients` | Resep: mapping menu → bahan baku |
| `Orders` | Order header (meja, total, status, payment) |
| `OrderItems` | Detail item per order |
| `Tables` | Daftar meja |
| `StockItems` | Bahan baku / ingredient |
| `StockUsageLogs` | Log pemakaian bahan otomatis |
| `StockExpiredLogs` | Log bahan expired/dibuang |
| `InventoryItems` | Inventaris fisik (peralatan) |
| `InventoryDamageLogs` | Log kerusakan/kehilangan inventaris |
| `Faqs` | FAQ untuk chatbot |
| `ChatbotLogs` | Log percakapan chatbot |
| `KitchenNotifications` | Notifikasi dapur → kasir |
| `UserNotifications` | Notifikasi umum per role |
| `TableCallNotifications` | Notifikasi panggilan ke meja |
| `SystemSettings` | Konfigurasi sistem (tax, QRIS, dll) |

---

## 🔑 Akun Default untuk Testing

| Role | Username | Password | Redirect |
|------|----------|----------|----------|
| Admin | `admin` | `admin123` | `/admin` |
| Owner | Buat via Admin | Set manual | `/owner` |
| Supervisor | Buat via Admin | Set manual | `/supervisor` |
| Kasir | Buat via Admin | Set manual | `/kasir` |
| Koki | Buat via Admin | Set manual | `/kitchen` |
| Customer | Register sendiri | Set sendiri | `/` |

> **Catatan:** Hanya akun `admin` yang dibuat otomatis saat startup. Role lain dibuat manual via Admin > Roles, lalu user dibuat via Admin > Users.

---

## 🛠️ Teknologi & Library

| Komponen | Teknologi |
|----------|-----------|
| Backend | ASP.NET Core 8 MVC |
| Database | SQL Server (LocalDB) |
| ORM | Raw ADO.NET (SqlConnection) + EF Core (Identity only) |
| Auth | ASP.NET Core Identity |
| Payment | BayarGG REST API |
| Frontend | Razor Views + Vanilla JS |
| CSS | Custom CSS |
| Chatbot | Rule-based (keyword matching + fuzzy search) |

---

## 📂 Struktur Folder Projek

```
cafe_yo/
├── Controllers/          # Semua controller (Admin, Auth, Kasir, Kitchen, Owner, dll)
├── Data/                 # DbContext, Seeder, Schema Initializer
├── Models/               # ViewModel & Entity classes
├── Security/             # AppRoles constants
├── Services/             # ChatbotService, BayarGgClient
├── Views/
│   ├── Admin/            # Semua view admin (Category, Faq, Menu, Orders, dll)
│   ├── Auth/             # Login, Register, Forbidden
│   ├── Home/             # Customer pages (Index, MyOrders, Privacy)
│   ├── Kasir/            # Dashboard kasir
│   ├── Kitchen/          # Kitchen Display System
│   ├── Owner/            # Owner dashboard, reports, analytics
│   ├── Shared/           # Layout, ChatbotWidget, Error
│   └── Supervisor/       # Supervisor pages (Ingredients, Recipes, dll)
├── wwwroot/              # Static files (CSS, JS, images)
├── docs/                 # Dokumentasi
├── Program.cs            # Entry point & service registration
└── appsettings.json      # Configuration
```

---

## 🧪 Cara Test Flow Lengkap

### Skenario 1: Customer Order via QRIS
1. Buka `http://localhost:PORT/?meja=1`
2. Pilih beberapa menu, tambah ke keranjang
3. Klik Checkout → pilih QRIS
4. Scan QR / tunggu callback
5. Cek di Kitchen Display → order muncul di kolom "Menunggu"

### Skenario 2: Kasir Buat Order
1. Login sebagai kasir di `/staff`
2. Di dashboard kasir, klik meja yang kosong
3. Pilih menu, tentukan qty
4. Submit order → pilih "Bayar di Kasir"
5. Konfirmasi pembayaran di panel Pending Payments
6. Order masuk ke Kitchen Display

### Skenario 3: Kitchen Flow
1. Login sebagai koki di `/staff`
2. Buka `/kitchen`
3. Klik "Mulai" pada order pending → pindah ke "Diproses"
4. Klik "Tandai Selesai" → pindah ke "Siap"
5. Login sebagai kasir → cek notifikasi dapur muncul

### Skenario 4: Owner Cek Laporan
1. Login sebagai owner di `/staff`
2. Buka `/owner` → lihat ringkasan omzet
3. Klik "Export" → download CSV laporan
4. Buka `/Owner/MenuControl` → approve/reject menu

---

## 📌 Catatan Penting

- **Stok otomatis berkurang** saat koki mulai proses order (berdasarkan resep di `MenuIngredients`)
- **Jika stok tidak cukup**, order tidak bisa diproses dan supervisor dapat notifikasi
- **Status meja auto-sync** berdasarkan order aktif
- **Chatbot** menggunakan rule-based matching (bukan AI/LLM), cocok untuk FAQ sederhana
- **Payment callback** dari BayarGG otomatis update status order
- **Concurrency control** di kitchen menggunakan database locking (UPDLOCK, ROWLOCK)
- **Semua log tersimpan** (chatbot, pemakaian stok, kerusakan, expired)

---

## 📸 Panduan Screenshot

Untuk melengkapi dokumentasi ini dengan screenshot, ambil SS dari halaman-halaman berikut:

1. **Homepage** — `/?meja=1` (tampilan menu grid)
2. **Keranjang & Checkout** — Klik icon keranjang
3. **Login Staff** — `/staff`
4. **Dashboard Admin** — `/admin`
5. **Admin > Menu** — `/Admin/MenuItems`
6. **Admin > Users** — `/Admin/Users`
7. **Admin > Roles** — `/Admin/Roles`
8. **Admin > Stok** — `/Admin/StockItems`
9. **Admin > Meja** — `/Admin/Tables`
10. **Admin > Orders** — `/Admin/Orders`
11. **Admin > FAQ** — `/Admin/Faq`
12. **Admin > Settings** — `/Admin/Settings`
13. **Admin > Kategori** — `/Admin/Category`
14. **Dashboard Kasir** — `/kasir`
15. **Kitchen Display** — `/kitchen`
16. **Dashboard Supervisor** — `/supervisor`
17. **Supervisor > Bahan** — `/Supervisor/Ingredients`
18. **Supervisor > Resep** — `/Supervisor/Recipes`
19. **Supervisor > Log** — `/Supervisor/UsageLogs`
20. **Supervisor > Inventaris** — `/Supervisor/Inventory`
21. **Dashboard Owner** — `/owner`
22. **Owner > Reports** — `/Owner/Reports`
23. **Owner > Menu Control** — `/Owner/MenuControl`
24. **Chatbot Widget** — Klik icon chat di homepage
25. **Pesanan Saya** — `/pesanan-saya`
26. **QR Payment** — Saat checkout QRIS

> **Tips:** Jalankan aplikasi, login dengan akun yang sesuai, lalu screenshot setiap halaman. Ganti placeholder `[SS: ...]` di atas dengan gambar yang sudah diambil.

---

*Dokumentasi ini dibuat berdasarkan source code projek Cafe Yo per Mei 2026.*
