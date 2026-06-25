# Sportive API

A **multi-tenant retail ERP & Point-of-Sale (POS) platform** built with ASP.NET Core 9 (.NET 9) and MySQL. One running API serves many independent businesses ("tenants"), each with its own isolated database, and gives every business a full back-office: selling at the counter and online, inventory and purchasing, double-entry accounting, HR & payroll, CRM, and reporting — fully bilingual (Arabic / English).

> This is the **backend API only**. The React/Vite frontend lives in a separate repository and talks to this API over REST + SignalR.

---

## Table of Contents
- [What this project is](#what-this-project-is)
- [Technology stack](#technology-stack)
- [Architecture](#architecture)
  - [Layers](#layers)
  - [Multi-tenancy](#multi-tenancy-the-most-important-concept)
  - [Request pipeline](#request-pipeline-programcs)
- [Modules](#modules)
- [Authentication, roles & permissions](#authentication-roles--permissions)
- [Code map](#code-map)
- [Getting started](#getting-started)
- [Configuration](#configuration)
- [Database & migrations](#database--migrations)
- [API surface](#api-surface)
- [Deployment](#deployment)
- [Further documentation](#further-documentation)

---

## What this project is

Sportive started as a simple sports-shop e-commerce API and grew into a **white-label, subscription-based retail management SaaS**. Today it contains **85+ controllers**, **86 database tables**, and **94 migrations** across seven business modules.

Each business that subscribes is a **tenant**. A central *master* database tracks all tenants, their plans and subscriptions; each tenant's actual data (products, orders, ledgers, staff) lives in a **separate MySQL database**. A single deployment transparently routes every request to the correct tenant database.

---

## Technology stack

| Concern | Choice |
|---|---|
| Framework / language | ASP.NET Core 9.0, C# |
| Database / ORM | MySQL 8.x via Entity Framework Core (Pomelo provider) |
| Auth | JWT (Bearer) + ASP.NET Core Identity |
| Authorization | Roles + custom fine-grained `[RequirePermission]` module permissions |
| Real-time | SignalR (`NotificationHub` at `/notifications-hub`) |
| Background jobs | `IHostedService` (daily HR task generator) + Hangfire (scheduled jobs, currently suspended) |
| Caching | In-memory by default, Redis optional |
| Validation | FluentValidation |
| Logging / tracing | Serilog (console + rolling files) + OpenTelemetry (OTLP) |
| Documents | QuestPDF (PDF receipts/reports), ClosedXML (Excel import/export) |
| Media | Cloudinary + local `/uploads` static files |
| Integrations | Paymob (payments), Egyptian Tax Authority / ZATCA (e-invoicing), WhatsApp Cloud API, ZK biometric clock devices, SMTP email |
| API docs | Swagger / OpenAPI (served at `/` in Development) |
| Deployment | Docker (`Dockerfile`, listens on port 8080) |

---

## Architecture

### Layers

The backend follows a clean **Controller → Service → Database** pattern.

| Layer | Folder | Responsibility |
|---|---|---|
| **Controllers** | `Controllers/` | HTTP entry points; permission checks; map DTOs; delegate to services |
| **Services** | `Services/` | All business logic |
| **Interfaces** | `Interfaces/` | Service contracts (DI + testability) |
| **Models** | `Models/` | EF Core entities (database tables) |
| **DTOs** | `DTOs/` | Request/response shapes (internal models are never exposed directly) |
| **Validators** | `Validators/` | FluentValidation input rules |
| **Data** | `Data/` | `AppDbContext` (per-tenant) + `MasterDbContext` (registry) + migrations |
| **Middleware** | `Middleware/` | Tenant resolution, exception handling, session tracking |
| **Extensions** | `Extensions/` | DI registration helpers (`DependencyInjection.cs`) |
| **Attributes / Filters** | `Attributes/`, `Filters/` | `RequirePermission`, tenant-aware job filter |
| **Hubs** | `Hubs/` | SignalR real-time hub |
| **Utils** | `Utils/` | Time, encryption, telemetry, PDF helpers |

**Data flow:** `Client → Controller → Service → AppDbContext (tenant DB)` for requests, and the reverse mapped through DTOs for responses.

### Multi-tenancy (the most important concept)

```
                         ┌─────────────────────┐
   request (JWT /        │  MasterDbContext     │   registry of all businesses:
   subdomain / header) ─▶│  (master database)   │   Tenants, Plans, Subscriptions
                         └─────────┬───────────┘
                                   │ looks up tenant's DB name/credentials
                                   ▼
        TenantValidationMiddleware → TenantContext (per-request)
                                   │
                                   ▼
        TenantConnectionResolver swaps the connection string
                                   │
                                   ▼
                         ┌─────────────────────┐
                         │  AppDbContext        │   that tenant's own MySQL database
                         │  (per-tenant DB)     │   products, orders, ledger, staff…
                         └─────────────────────┘
```

- **`Middleware/TenantValidationMiddleware.cs`** resolves the tenant on every request (JWT claim has highest priority, so a user can't bypass their assigned tenant via header/subdomain), stores it in a scoped `ITenantContext`, and enforces **subscription expiry** — once past expiry + grace period, write operations are rejected with `402 Payment Required`.
- **`Services/System/TenantConnectionResolver.cs`** builds the per-request connection string from the resolved tenant's database name/user/password, so `AppDbContext` always points at the right database.
- A baseline `sportive` tenant + Enterprise subscription is seeded into the master registry on startup (`Program.cs`).

### Request pipeline (`Program.cs`)

Order of middleware: Serilog request logging → **security headers** (CSP, HSTS, X-Frame-Options…) → exception handling → static files (`/uploads`) → **rate limiting** → **tenant validation** → authentication → authorization → session last-seen tracking → controllers, health check (`/health`), SignalR hub.

---

## Modules

Both `Controllers/` and `Services/` are organized by the same seven business domains.

### 🛒 Orders & POS — `Controllers/Orders`, `Services/Orders`
The cash register and order lifecycle: carts, checkout, coupons, installments, held carts, shipping zones, and Paymob payments. Order creation is **atomic** — customer resolution, pricing/discounts, VAT extraction, stock deduction, and the accounting journal entry all happen inside one DB transaction and either all commit or all roll back. Handles full/partial returns and "convert to cost". *(Core: `OrderService.cs`.)*

### 📦 Inventory & Purchasing — `Controllers/Inventory`, `Services/Inventory`
Products with variants (size/color groups), brands, categories, units, barcodes; warehouses & stock transfers; purchase invoices, returns and supplier payments; inventory audits, adjustments, opening balances; fixed assets & depreciation; low-stock "inventory intelligence".

### 💰 Accounting & Finance — `Controllers/Accounting`, `Services/Accounting`
Real double-entry bookkeeping. Sales, purchases and payments automatically generate balanced **journal entries** against a mapped chart of accounts (Cash/Receivables, Revenue, VAT, Sales Discount, COGS, Inventory…). Includes receipt/payment vouchers, POS shift closures, financial reports (P&L, balance sheet, GL) and profitability analysis. *(Core: `SalesAccountingService.cs`, `AccountingCoreService.cs`.)*

### 👥 Customers & CRM — `Controllers/Customers`, `Services/Customers`
Customer profiles (PII stored **encrypted**, searched via hashes), customer categories for tiered pricing (Retail/Wholesale/VIP), automatic re-tiering on purchase, wishlists and reviews.

### 🧑‍💼 HR & Staff — `Controllers/HR`, `Services/HR`
Employees, departments, attendance (including **ZK biometric clock** devices via the `iclock` protocol and self-service punching), payroll, commissions (groups/schemes), bonuses, deductions, advances, cashier performance, and an automated daily **task generator** (`Services/TaskGenerationService.cs`) driven by task blueprints.

### 📊 Reports & Analytics — `Controllers/Reports`, `Services/Reports`
Dashboard KPIs, operational reports, and Excel/PDF **import & export** of catalog and report data.

### ⚙️ System & Platform — `Controllers/System`, `Services/System`
Cross-cutting platform features: auth & users, roles/permissions, tenant & subscription/plan management, branches & warehouses, settings, audit logs, security events, backups, notifications, an AI controller, WhatsApp messaging, ETA/ZATCA tax integration, and various data-maintenance/backfill utilities.

---

## Authentication, roles & permissions

- **Authentication:** JWT Bearer tokens (login/register via `AuthController`). Configure under `JWT:*`.
- **Roles** (`Models/AppRoles.cs`): `SuperAdmin`, `Admin`, `Manager`, `Cashier`, `Accountant`, `Staff`, `Customer`, plus `Custom`. Convenience groups exist (e.g. `PosAccess = Admin,Manager,Cashier`).
- **Fine-grained permissions:** the custom `[RequirePermission]` attribute (`Attributes/RequirePermissionAttribute.cs`) gates individual actions against **module keys** (`Models/ModuleKeys.cs`, e.g. `pos`, `orders`, `inventory`, `returns-full`). Per-user/role permissions are cached (~15 min TTL); `Admin`/`Manager` may bypass certain checks.

### Default seeded admin
```
Email:    admin@sportive.com
Password: Admin@123456
```
**Change this immediately in any non-local environment.**

---

## Code map

```
Sportive.API/
├── Program.cs                 # Startup, middleware pipeline, tenant seeding
├── Controllers/               # 85+ HTTP endpoints, grouped by module
│   ├── Accounting/  Customers/  HR/  Inventory/
│   ├── Orders/  Reports/  System/
│   └── DiagnosticsController.cs
├── Services/                  # Business logic, mirrors the module folders
│   ├── Accounting/ … System/  # System/ holds tenant resolver, auth, backups, etc.
│   ├── ETA/                   # Egyptian Tax Authority / ZATCA e-invoicing
│   └── TaskGenerationService.cs   # hosted background service (daily HR tasks)
├── Interfaces/                # Service contracts (by module)
├── Models/                    # 86 EF Core entities
├── DTOs/                      # Request/response objects (by module)
├── Data/
│   ├── AppDbContext.cs        # per-tenant context
│   ├── MasterDbContext.cs     # tenant registry context
│   └── *Factory.cs, Migrations/
├── Middleware/                # TenantValidation, Exception, SessionLastSeen
├── Extensions/                # DependencyInjection.cs (DI wiring), claims helpers
├── Attributes/ Filters/       # RequirePermission, TenantJobFilter
├── Hubs/                      # NotificationHub (SignalR)
├── Validators/ Utils/ Resources/ Fonts/
├── Migrations/                # 94 EF Core migrations
├── docs/                      # Detailed architecture & per-module docs
└── Dockerfile, nuget.config
```

---

## Getting started

### Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- MySQL 8.x (local or hosted)
- Optional: Redis, Visual Studio 2022 / VS Code + C# Dev Kit

### Run locally
```bash
# 1. Configure connection strings & secrets (see Configuration below)
#    Use dotnet user-secrets or appsettings.Development.json — never commit secrets.

# 2. Apply database migrations
dotnet ef database update

# 3. Run
dotnet run
```
In Development, Swagger UI is served at the root (`/`). The app listens on the `PORT` env var (default `8080`).

---

## Configuration

Settings come from `appsettings.json` / `appsettings.Development.json`, **User Secrets**, or environment variables (env vars win). The committed repo does **not** include secrets.

### Required
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=HOST;Port=3306;Database=TENANT_DB;User=USER;Password=PASS;",
    "MasterConnection":  "Server=HOST;Port=3306;Database=MASTER_DB;User=USER;Password=PASS;",
    "Redis": ""                       // optional; empty → in-memory cache
  },
  "JWT": {
    "Secret":   "<long-random-32+ chars>",
    "Issuer":   "Sportive",
    "Audience": "SportiveClient",
    "ExpiresHours": 24
  },
  "AllowedOrigins": "https://your-frontend.example"   // added to CORS (localhost:3000/5173 allowed by default)
}
```
Env-var equivalents: `DATABASE_URL`, `MASTER_DATABASE_URL`, `DATABASE_MAX_POOL_SIZE`, `DATABASE_MIN_POOL_SIZE`, `PORT`. If `MasterConnection` / `MASTER_DATABASE_URL` is missing the app will not start.

### Optional integrations
| Feature | Keys |
|---|---|
| Payments (Paymob) | `Paymob:ApiKey`, `Paymob:IntegrationId`, `Paymob:IframeId`, `Paymob:HmacSecret` |
| Media (Cloudinary) | `Cloudinary:CloudName`, `Cloudinary:ApiKey`, `Cloudinary:ApiSecret` |
| Email (SMTP) | `Email:Host`, `Email:Port`, `Email:User`, `Email:Pass`, `Email:From`, `Email:AdminEmail` |
| WhatsApp Cloud API | `WhatsApp:PhoneNumberId`, `WhatsApp:AccessToken`, `WhatsApp:TemplateName`, `WhatsApp:LanguageCode` |
| Backups | `Backup:LocalPath`, `Backup:Email:To`, `Security:BackupSecret` |
| Tracing | `OpenTelemetry:Endpoint` (default `http://localhost:4317`) |

> **MySQL tips:** locally you may need `SslMode=None;AllowPublicKeyRetrieval=true`. For remote hosts (e.g. Hostinger) enable Remote MySQL and use `SslMode=Required;AllowPublicKeyRetrieval=true`.

---

## Database & migrations

Two EF Core contexts:
- **`MasterDbContext`** — the tenant registry (Tenants, Plans, Subscriptions). Migrated on startup.
- **`AppDbContext`** — per-tenant business data. Tenant migrations are applied **on-demand** via an admin endpoint rather than automatically at startup, to avoid scaling bottlenecks.

```bash
# create a migration after changing models (defaults to AppDbContext)
dotnet ef migrations add YourMigrationName

# apply migrations
dotnet ef database update
```

---

## API surface

Browse the full, always-current API in **Swagger** (root `/` in Development). A few notable endpoints:

| Area | Examples |
|---|---|
| Auth | `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/customer-id`, `POST /api/auth/change-password` |
| Orders / POS | `GET /api/orders`, `GET /api/orders/my`, `POST /api/orders` (atomic checkout) |
| Cart | `GET /api/cart/{customerId}` |
| Health | `GET /health` |
| Real-time | SignalR hub at `/notifications-hub` |

> On register/login, a `Customer` record is auto-created and linked to the account for `Customer`-role users; the response includes `customerId` (use it for cart routes — don't assume `1`).

---

## Deployment

```bash
docker build -t sportive-api .
docker run -p 8080:8080 \
  -e DATABASE_URL="..." -e MASTER_DATABASE_URL="..." \
  -e JWT__Secret="..." \
  sportive-api
```
The image is based on `mcr.microsoft.com/dotnet/aspnet:9.0` and includes `mysql-client` for `mysqldump`-based backups. The app binds to `0.0.0.0:8080`.

---

## Further documentation

Detailed docs live in [`docs/`](./docs/INDEX.md):
- [Architecture](./docs/ARCHITECTURE.md) · [Technologies](./docs/TECHNOLOGIES.md) · [Workflow](./docs/WORKFLOW.md) · [Key Rotation](./docs/key_rotation_strategy.md)
- Modules: [Inventory](./docs/features/INVENTORY.md) · [Accounting](./docs/features/ACCOUNTING.md) · [POS & Orders](./docs/features/POS_ORDERS.md) · [CRM](./docs/features/CUSTOMER_CRM.md) · [HR & Staff](./docs/features/HR_STAFF.md) · [Reports](./docs/features/REPORTS.md)
</content>
