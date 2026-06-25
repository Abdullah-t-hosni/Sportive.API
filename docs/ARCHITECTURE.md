# Project Architecture

## Overview
Sportive is a **multi-tenant** retail ERP — Point of Sale (POS), Inventory, Accounting, HR, CRM, and Reporting — designed to serve many independent businesses from a single deployment. It follows a decoupled architecture with a C# .NET backend and a React frontend.

## Backend Architecture (Sportive.API)
The backend is built using **ASP.NET Core 9.0** following a clean, modular controller-service pattern. Controllers and services are grouped by business module: `Accounting`, `Customers`, `HR`, `Inventory`, `Orders`, `Reports`, and `System`.

### Layers
1.  **Controllers** (`Controllers/`): Entry points for HTTP requests. They handle request validation/authorization and delegate business logic to services.
2.  **Services** (`Services/`): Contain the core business logic. They interact with the database context directly for data operations.
3.  **Data/Models**:
    - `AppDbContext`: The per-tenant Entity Framework Core context (business data).
    - `MasterDbContext`: The registry context (tenants, plans, subscriptions).
    - `Models`: Database entities representing the schema.
    - `DTOs` (Data Transfer Objects): Used for transferring data between the API and clients, ensuring internal models are not exposed directly.
4.  **Authorization**: Custom permission-based system using the `[RequirePermission]` attribute on top of JWT + role-based auth.
5.  **Middleware** (`Middleware/`): Cross-cutting concerns — tenant resolution, exception handling, and session tracking.

### Multi-Tenancy
This is the defining feature of the architecture. Each subscribing business is a **tenant** with its **own separate MySQL database**.
- A central **master database** (`MasterDbContext`) holds the registry of all tenants, their plans, and subscriptions.
- On every request, `TenantValidationMiddleware` resolves the current tenant (the JWT claim has highest priority, preventing header/subdomain spoofing), stores it in a scoped `ITenantContext`, and enforces subscription expiry (writes are blocked with `402 Payment Required` after the grace period).
- `TenantConnectionResolver` then builds the connection string for that tenant's database, so `AppDbContext` always targets the correct database.

### Cross-Cutting Infrastructure
- **Real-time**: SignalR `NotificationHub` (`/notifications-hub`) for live notifications and updates.
- **Background work**: A hosted `TaskGenerationService` (daily HR tasks); Hangfire is wired for scheduled jobs (currently suspended).
- **Observability**: Serilog (console + rolling files) and OpenTelemetry (tracing + metrics via OTLP); health checks at `/health`.
- **Security**: Rate limiting, strict security headers (CSP/HSTS), response compression, audit logging.

### Data Flow
- **Request**: Client (Frontend) → Controller → Service → AppDbContext (tenant DB).
- **Response**: Database → Service → DTO → Controller → Client.

## Frontend Architecture (sportive-frontend)
The frontend is a modern SPA built with **React** and **Vite**, in a separate repository.

### Core Principles
- **Feature-Based Structure**: Components and logic are grouped by feature (e.g., POS, Admin, Inventory).
- **State Management**: Uses **TanStack Query (React Query)** for server-state management (caching, synchronization).
- **Styling**: **Tailwind CSS** for responsive and utility-first styling.
- **Localization**: **i18next** for multi-language support (Arabic & English).

### Key Modules
- **Services**: Abstract API calls using Axios.
- **Hooks**: Custom React hooks for reusable logic.
- **Components**: Divided into generic UI components and feature-specific components.
</content>
