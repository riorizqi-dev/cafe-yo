# Seed User Login (Staff)

Gunakan akun berikut untuk login di `/staff`.

## Akun Default (dibuat otomatis saat startup)

| Role | Username | Password | Redirect |
|---|---|---|---|
| Admin | `admin` | `admin123` | `/admin` |

## Role Lain Dibuat Saat Presentasi (via Admin > Roles)

| Role | Username | Password | Catatan |
|---|---|---|---|
| `koki` | Buat manual | Set via Admin Users | Setelah dibuat + assign ke user, redirect login ke `/kitchen`. |
| `kasir` | Buat manual | Set via Admin Users | Setelah dibuat + assign ke user, redirect login ke `/kasir`. |
| `supervisor` | Buat manual | Set via Admin Users | Setelah dibuat + assign ke user, redirect login ke `/supervisor`. |
| `owner` | Buat manual | Set via Admin Users | Setelah dibuat + assign ke user, redirect login ke `/owner`. |
| role lain | Buat manual | Set via Admin Users | Jika belum ada dashboard khusus, diarahkan ke `/staff/dashboard-unavailable`. |

## Referensi Kode

- Seeder akun: [IdentitySeeder.cs](./IdentitySeeder.cs)
- Redirect login per role: [AuthController.cs](../Controllers/AuthController.cs)
- Default reset password user staff: [AdminUsersController.cs](../Controllers/AdminUsersController.cs)
