# ☕ Cafe Yo — Professional Cafe Management System

> **A full-featured web-based cafe management platform** built with ASP.NET Core MVC 8.0.  
> Manage orders, tables, menus, inventory, staff roles, and payments — all in one place.

---

## ✨ Features

| Area | Features |
|------|----------|
| 🧑‍🍳 **Customer Portal** | Browse menu, add to cart, dine-in / takeaway, QRIS payment, order history |
| 🪑 **Table Management** | Real-time table status (available, occupied, reserved), table sync engine |
| 📋 **Order Management** | Create orders, auto-calculate subtotal/discount/service fee, kitchen status tracking |
| 🍳 **Kitchen Display** | Real-time order streaming for cooks, status updates (pending → cooking → ready → done) |
| 👨‍💼 **Staff Roles** | Full RBAC: Admin, Owner, Supervisor, Cashier (Kasir), Cook (Koki) |
| 💳 **Payment Gateway** | QRIS online payment via **Bayar.gg** integration, cash payment at counter |
| 📦 **Inventory & Stock** | Track stock items, min quantities, ingredient usage per menu item |
| 🤖 **AI Chatbot** | Integrated intelligent assistant for customer inquiries |
| 📊 **Dashboard & Reports** | Role-based dashboards, daily revenue stats, order analytics |
| 📱 **Responsive UI** | Customer-facing mobile-first design with Bootstrap 5 |

---

## 🛠️ Tech Stack

### Backend
| Technology | Description |
|------------|-------------|
| **ASP.NET Core 8.0** | MVC framework (C# 12 / .NET 8) |
| **Entity Framework Core 8.0** | ORM for database access |
| **ASP.NET Core Identity** | Authentication & role-based authorization |
| **Microsoft.Data.SqlClient** | Direct SQL access for operational queries |

### Frontend
| Technology | Description |
|------------|-------------|
| **Razor Views** | Server-side rendering with C# / HTML |
| **Bootstrap 5** | Responsive CSS framework |
| **JavaScript (Vanilla)** | Client-side interactivity (cart, modals, payment polling) |
| **Font Awesome 4** | Icon library |
| **jQuery** | DOM utilities & AJAX |

### Database
| Technology | Description |
|------------|-------------|
| **SQL Server** | Primary database (LocalDB / Express / Full) |
| **ADO.NET + EF Core** | Hybrid data access pattern |

### Packages (NuGet)
| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 8.0.0 | Identity + EF Core storage |
| `Microsoft.EntityFrameworkCore.SqlServer` | 8.0.0 | SQL Server provider |
| `Microsoft.EntityFrameworkCore.Tools` | 8.0.0 | EF migrations tooling |
| `Microsoft.Data.SqlClient` | 5.2.0 | Direct SQL client |

---

## 🗂️ Project Architecture

```
cafe_yo/
├── Controllers/               # MVC Controllers
│   ├── Admin*.cs              # Admin area (users, roles, menus, orders, tables, stock, settings)
│   ├── ApiAuthController.cs   # API authentication endpoints
│   ├── AuthController.cs      # Staff login / logout
│   ├── CashierNotificationsController.cs
│   ├── ChatbotApiController.cs
│   ├── HomeController.cs      # Customer-facing pages & order API
│   ├── IngredientsApiController.cs
│   ├── KasirController.cs     # Cashier dashboard
│   ├── KitchenController.cs   # Kitchen display system
│   ├── NotificationsController.cs
│   ├── OrdersApiController.cs
│   ├── OwnerController.cs     # Owner dashboard & analytics
│   ├── PaymentsApiController.cs
│   ├── Supervisor*.cs         # Supervisor area (inventory, alerts, recipes)
│   ├── TableAlertsController.cs
│   ├── TablesApiController.cs
│   └── UsersController.cs
│
├── Models/                    # ViewModels & Domain Models
│   ├── ApplicationUser.cs     # Extended IdentityUser
│   ├── CafeTable.cs           # Table entity
│   ├── MenuCategory.cs        # Menu categories
│   ├── MenuItem.cs            # Menu items
│   ├── StockItem.cs           # Inventory items
│   ├── ChatbotModels.cs       # Chatbot request/response models
│   ├── KitchenModels.cs       # Kitchen display models
│   ├── OwnerModels.cs         # Owner analytics models
│   ├── AdminOrderModels.cs    # Admin order management views
│   ├── AdminRoleModels.cs     # Role management views
│   ├── AdminUserRoleModels.cs
│   ├── AdminDashboardViewModel.cs / AdminDashboardVM.cs
│   ├── DashboardViewModel.cs
│   ├── HomeIndexViewModel.cs
│   ├── KasirDashboardViewModel.cs
│   └── ErrorViewModel.cs
│
├── Data/                      # Data Access Layer
│   ├── ApplicationDbContext.cs
│   ├── IdentitySeeder.cs      # Seed default roles & admin user
│   ├── LegacyUsersSeeder.cs   # Legacy user table migration
│   ├── OperationalSchemaInitializer.cs  # Kitchen/order schema init
│   ├── OrderTableSync.cs      # Table status sync engine
│   ├── TableStateStore.cs     # Table state persistence
│   ├── SeedDummy.sql          # Dummy data for development
│   └── SeedUsers.md           # Seed user documentation
│
├── Services/                  # Business Logic
│   ├── ChatbotService.cs / IChatbotService.cs
│   └── Payments/
│       └── BayarGgClient.cs   # Bayar.gg payment gateway integration
│
├── Security/                  # Authorization
│   └── AppRoles.cs            # Role constants
│
├── Views/                     # Razor Views
│   ├── Home/                  # Customer pages (menu, cart, orders)
│   ├── Auth/                  # Staff login pages
│   ├── Admin/                 # Admin dashboard & management
│   ├── Kasir/                 # Cashier interface
│   ├── Kitchen/               # Kitchen display
│   ├── Owner/                 # Owner analytics & reports
│   ├── Supervisor/            # Supervisor inventory & alerts
│   └── Shared/                # Layouts, error page, chatbot widget
│
├── wwwroot/                   # Static assets
│   ├── css/                   # Stylesheets (Bootstrap, custom, vendor)
│   ├── js/                    # Client-side scripts
│   ├── images/                # Menu images, QR codes, payment icons
│   ├── fonts/                 # Font Awesome icons
│   └── lib/                   # Client libraries (jQuery, Bootstrap, validation)
│
├── docs/                      # Project documentation
├── Program.cs                 # Application entry point
├── appsettings.json           # Main configuration
├── appsettings.Development.json
├── cafe_yo.csproj             # Project file
├── Properties/
│   └── launchSettings.json    # Dev server configuration
│
├── .gitignore
├── .env.example
├── README.md                   # ← You are here
└── REPO_INFO.md               # Technical deep-dive
```

---

## 🚀 Installation Guide

### Prerequisites

| Requirement | Version | Download |
|-------------|---------|----------|
| .NET SDK | 8.0+ | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| SQL Server | 2019+ / LocalDB | [microsoft.com](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) |
| Visual Studio | 2022 (recommended) | [visualstudio.com](https://visualstudio.microsoft.com/vs/) |
| Git | 2.x | [git-scm.com](https://git-scm.com/) |

### Step-by-Step

```bash
# 1. Clone the repository
git clone https://github.com/riorizqi-dev/cafe-yo.git
cd cafe-yo

# 2. Restore .NET packages
dotnet restore

# 3. Configure the database connection
#    Edit appsettings.json → ConnectionStrings:DefaultConnection
#    Or set environment variable:
#      ConnectionStrings__DefaultConnection=Server=(local);Database=CafeYoDB;...;

# 4. Apply database migrations (auto-seeded on first run)
dotnet run

#    The app will auto-create:
#      • Identity tables (AspNetUsers, AspNetRoles, ...)
#      • Operational tables (Orders, OrderItems, Tables, MenuItems, ...)
#      • Default seeder user (see SeedUsers.md)
```

> ⚠️ The first run will seed default data:  
> **Admin** — `admin` / `Admin123!`  
> **Kasir** — `kasir` / `Kasir123!`  
> **Koki** — `koki` / `Koki123!`  
> See [`SeedUsers.md`](Data/SeedUsers.md) for the full list.

---

## 📖 Usage Guide

### 👤 Customer
1. Open the app at `http://localhost:5168`
2. Enter your **table number** (e.g., `?meja=1`)
3. Browse **menu items**, add them to cart
4. Choose **Dine-in** or **Takeaway**
5. Pay via **QRIS** (scan & confirm) or **Cash at counter**
6. View order history at `/pesanan-saya`

### 🧑‍💼 Staff Roles

| Role | Login URL | Permissions |
|------|-----------|-------------|
| **Admin** | `/staff` → `admin` / `Admin123!` | Full access: users, roles, menus, orders, tables, stock, settings |
| **Owner** | `/staff` → `owner` / `Owner123!` | Analytics, reports, menu control |
| **Supervisor** | `/staff` → `supervisor` / `Super123!` | Inventory, alerts, ingredients, recipes |
| **Kasir** | `/staff` → `kasir` / `Kasir123!` | Cashier dashboard, payment handling |
| **Koki** | `/staff` → `koki` / `Koki123!` | Kitchen display, order status updates |

### 🍳 Kitchen Flow
1. Cook logs in at `/koki`
2. New orders appear automatically in the **Kitchen Display**
3. Cook updates status: **Pending → Cooking → Ready → Done**
4. Wait staff is notified when food is ready

### 📊 Owner Dashboard
- View **daily revenue**, **order analytics**, **popular menu items**
- Access at `/owner` after login as `owner`

---

## 🔮 Future Improvements

### Planned Features
- [ ] Multi-language support (EN / ID)
- [ ] Email & WhatsApp order notifications
- [ ] Customer loyalty program & points system
- [ ] Table reservation system with time slots
- [ ] Direct integration with GoFood / GrabFood
- [ ] Real-time chat between customer & staff
- [ ] Printable receipt generation (PDF)
- [ ] Export reports to Excel / CSV

### Technical Enhancements
- [ ] Migrate to **React / Vue.js** frontend with API-only backend
- [ ] Containerize with **Docker** (Dockerfile + docker-compose)
- [ ] Add **unit tests** (xUnit) & integration tests
- [ ] Implement **CQRS / MediatR** pattern
- [ ] Switch to **MySQL / PostgreSQL** support
- [ ] Add **Redis caching** for menu & session data
- [ ] CI/CD pipeline with **GitHub Actions**
- [ ] API documentation with **Swagger / OpenAPI**
- [ ] Audit logging for all admin actions
- [ ] Performance optimization (lazy loading, pagination, indexing)

---

## 👨‍💻 Author

**RIO RIZQI SAPUTRA**  
- 🌐 GitHub: [@riorizqi-dev](https://github.com/riorizqi-dev)  
- 📧 Email: [riorizqi918@gmail.com](mailto:riorizqi918@gmail.com)

---

## 📄 License

Distributed under the **MIT License**.  
See [`LICENSE`](LICENSE) for more information.

---

## 🤝 Contributing

Contributions are what make the open-source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

1. **Fork** the Project
2. **Create** your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. **Commit** your Changes (`git commit -m 'Add some AmazingFeature'`)
4. **Push** to the Branch (`git push origin feature/AmazingFeature`)
5. Open a **Pull Request**

---

<p align="center">Made with ☕ and ❤️ by RIO RIZQI SAPUTRA</p>