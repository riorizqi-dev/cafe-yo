# RBAC + Modul Dapur

## Role yang aktif
- `Owner`: akses penuh modul admin (`/Admin`, users, roles, menu, stok, transaksi) + kasir + dapur.
- `Supervisor`: akses operasional (`/Admin/Orders`, `/Admin/StockItems`, `/Admin/Tables`, `/Kasir`, `/Kitchen`), tanpa kelola user/role/menu.
- `Koki`: akses hanya `/Kitchen` dan endpoint kitchen.
- `Kasir`: akses `/Kasir` + endpoint notifikasi dapur.

## Akun seed untuk test
- `owner.rani / owner123456*`
- `supervisor.andi / supervisor123456*`
- `koki.sinta / koki123456*`
- `budi.s / kasir123456*`

## Endpoint utama
- `GET /Kitchen/orders?status=pending|processing|ready`
- `PATCH /Kitchen/orders/{id}/status` body: `{ "status": "processing|ready", "updatedAt": "..." }`
- `GET /cashier/notifications`
- `POST /cashier/notifications/{id}/ack`

## Flow test cepat
1. Login sebagai `koki.sinta`, buka `/Kitchen`.
2. Klik `Mulai` pada order pending, pastikan order pindah ke kolom Diproses.
3. Klik `Tandai Selesai`, konfirmasi modal, pastikan order masuk `Siap Diantar`.
4. Login sebagai `budi.s`, buka `/Kasir`, cek toast + badge `Notifikasi Dapur`.
5. Klik chip notifikasi, sistem `ack` dan notifikasi hilang dari list.
6. Login `supervisor.andi`, pastikan bisa akses `/Kitchen` dan `/Kasir` tapi tidak bisa `/Admin/Users`.
