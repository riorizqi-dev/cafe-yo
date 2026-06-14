# Cafe Yo — Technical Repository Info

> Deep-dive into the project's technical architecture, database schema, and API routes.

---

## 📦 Database Structure

The application uses **SQL Server** with hybrid access: **Entity Framework Core** manages Identity/Auth tables, while **ADO.NET (SqlClient)** handles operational queries for performance.

### Identity Tables (EF Core — auto-migrated)

| Table | Purpose |
|-------|---------|
| `AspNetUsers` | Users (`ApplicationUser` : `IdentityUser`) — extended with `FullName` & `Role` |
| `AspNetRoles` | Roles: Admin, Owner, Supervisor, Kasir, Koki, Customer |
| `AspNetUserRoles` | Many-to-many user-role mapping |
| `AspNetRoleClaims` | Role-based claims |
| `AspNetUserClaims` | Per-user claims |
| `AspNetUserLogins` | External login providers |
| `AspNetUserTokens` | Auth tokens |

### Operational Tables (seeded by `OperationalSchemaInitializer.cs`)

| Table | Columns | Purpose |
|-------|---------|---------|
| `MenuCategories` | `CategoryId` (PK), `Name`, `IsActive` | Menu categories |
| `MenuItems` | `MenuItemId` (PK), `Name`, `Category`, `ImageUrl`, `Description`, `Price`, `Stock`, `IsAvailable` | Food/drink items |
| `Tables` | `TableId` (PK), `TableNumber`, `Status` | Cafe tables (available/occupied) |
| `Orders` | `OrderId` (PK), `OrderNumber`, `TableId` (FK), `OrderDate`, `Status`, `Total`, `KitchenStatus`, `PaymentMethod`, `PaymentStatus`, `PaymentInvoice`, `PaymentQrString`, `PaymentCheckoutUrl`, `UpdatedAt` | Customer orders |
| `OrderItems` | `OrderItemId` (PK), `OrderId` (FK), `MenuItemId`, `ItemName`, `Quantity`, `UnitPrice`, `Notes` | Line items per order |
| `StockItems` | `StockItemId` (PK), `Name`, `Type`, `Quantity`, `MinQuantity` | Inventory tracking |
| `SystemSettings` | `[Key]`, `[Value]` | Key-value config (e.g., QRIS image URL) |
| `Notifications` | User alerts & notifications |
| `TableAlerts` | Table-specific alerts |
| `Ingredients` / `Recipes` | Recipe management |
| `UsageLogs` | Ingredient usage tracking |

> 💡 All operational DDL is idempotent — safe to run multiple times.

---

## 🔐 Authentication & Authorization

### Identity Configuration (`Program.cs`)

```csharp
services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();
```

### Authorization Policies

| Policy | Required Role(s) |
|--------|------------------|
| `AdminOnly` | Admin |
| `OwnerOnly` | Owner |
| `SupervisorOnly` | Supervisor |
| `KasirOnly` | Kasir |
| `KokiOnly` | Koki / Dapur |

### Cookie Settings
- **Login Path**: `/staff`
- **Access Denied Path**: `/forbidden`

---

## 🌐 Key API Routes

### Public (Customer)

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/` or `/menu` | Home page with menu |
| `GET` | `/keranjang` | Cart page |
| `GET` | `/pesanan-saya` | Customer order history |
| `POST` | `/api/customer/orders/create` | Create new order |
| `POST` | `/api/customer/orders/mine` | Get customer's orders |
| `POST` | `/api/payments/customer/orders/{orderId}/create` | Create QRIS payment |
| `GET` | `/api/payments/customer/orders/{orderId}/refresh` | Poll payment status |

### Staff (Authenticated)

| Method | Route | Controller | Description |
|--------|-------|------------|-------------|
| `GET` | `/staff` | `AuthController` | Staff login page |
| `GET` | `/dashboard` | `AdminController` | Admin dashboard |
| `GET` | `/admin/users` | `AdminUsersController` | User management |
| `GET` | `/admin/roles` | `AdminRolesController` | Role management |
| `GET` | `/admin/menuitems` | `AdminMenuItemsController` | Menu CRUD |
| `GET` | `/admin/orders` | `AdminOrdersController` | Order management |
| `GET` | `/admin/tables` | `AdminTablesController` | Table management |
| `GET` | `/admin/categories` | `AdminCategoryController` | Category management |
| `GET` | `/admin/stock` | `AdminStockItemsController` | Inventory management |
| `GET` | `/admin/settings` | `AdminSettingsController` | System settings |
| `GET` | `/admin/faq` | `AdminFaqController` | FAQ management |
| `GET` | `/kasir` | `KasirController` | Cashier dashboard |
| `GET` | `/koki` | `KitchenController` | Kitchen display |
| `GET` | `/owner` | `OwnerController` | Owner dashboard |
| `GET` | `/supervisor` | `SupervisorController` | Supervisor panel |
| `GET` | `/supervisor/inventory` | `SupervisorController` | Inventory view |
| `GET` | `/supervisor/ingredients` | `IngredientsApiController` | Ingredients |
| `GET` | `/supervisor/alerts` | `TableAlertsController` | Alerts |
| `POST` | `/api/chatbot` | `ChatbotApiController` | AI chatbot |
| `POST` | `/api/auth/login` | `ApiAuthController` | API login |

---

## 💳 Payment Flow

1. Customer creates order → status `menunggu_pembayaran`
2. If **QRIS** selected → auto-create payment via Bayar.gg
3. QR code displayed → customer scans → pays
4. Frontend polls `/refresh` every 5 seconds
5. On `paid` → order confirmed → kitchen notified
6. If **Cash** selected → order status `belum_bayar` → cashier handles payment

### Payment Gateway Config (`appsettings.json`)

```json
{
  "PaymentGateway": {
    "BayarGG": {
      "BaseUrl": "https://www.bayar.gg",
      "ApiKey": "your-api-key",
      "FixedFee": 200,
      "MaxExtraFee": 5000,
      "TimeoutSeconds": 30
    }
  }
}
```

---

## 🔧 Build & Run Commands

```bash
# Restore
dotnet restore

# Build
dotnet build

# Run (Development)
dotnet run --project cafe_yo/cafe_yo.csproj

# Run (HTTPS)
dotnet run --launch-profile https

# Run (IIS Express)
dotnet run --launch-profile "IIS Express"
```

**Default URLs:**
- HTTP: `http://localhost:5168`
- HTTPS: `https://localhost:7240`

---

## 📁 Environment Profiles

| Profile | `ASPNETCORE_ENVIRONMENT` | Config File |
|---------|-------------------------|-------------|
| Development | `Development` | `appsettings.Development.json` |
| Production | `Production` | `appsettings.json` |

---

## 🔗 Useful Links

- **GitHub Repository**: [https://github.com/riorizqi-dev/cafe-yo](https://github.com/riorizqi-dev/cafe-yo)
- **Author**: [@riorizqi-dev](https://github.com/riorizqi-dev)
- **Report Issues**: [GitHub Issues](https://github.com/riorizqi-dev/cafe-yo/issues)

---

## 📝 Notes for Contributors

- All operational DB changes go through `OperationalSchemaInitializer.cs` (not EF migrations).
- Identity schema is managed by EF Core — use `Add-Migration` for Identity changes.
- The kitchen display uses polling (every 5 seconds) — consider SignalR for real-time.
- QRIS payment polling uses a 15-minute expiry timeout.
- Discount logic applies **member pricing** based on item quantity tiers.