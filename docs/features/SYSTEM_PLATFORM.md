# System & Platform Feature

## Overview
The System module holds the cross-cutting, platform-level capabilities that the business modules rely on — identity, multi-tenancy, configuration, and operational tooling. It is the largest controller group in the project.

## Key Areas
- **Identity & Access**: Authentication, user accounts, and role/permission assignment.
- **Multi-Tenancy & Billing**: Tenant provisioning, plans, and subscriptions.
- **Organization**: Branches and warehouses that scope data across the other modules.
- **Configuration**: Store settings, welcome messages, and feature flags.
- **Operations**: Backups, audit logs, security events, data maintenance, and notifications.
- **Integrations**: WhatsApp messaging and AI-assisted features.

## Backend Components (`Controllers/System`, `Services/System`)
- **Auth & users**: `AuthController`, `UsersController`, `SessionsController`.
- **Tenancy & billing**: `TenantsController`, `TenantManagementController`, `PlansController`, `SubscriptionsController`. Tenant resolution is handled by `TenantResolver` / `TenantConnectionResolver` and `TenantValidationMiddleware`.
- **Organization & settings**: `BranchesController`, `WarehousesController`, `SettingsController`, `WelcomeMessageController`.
- **Operations & data**: `BackupController`, `AuditLogsController`, `NotificationsController`, `DataMaintenanceController`, `BackfillController`, `SchemaFixController`, `MappingSeederController`, `AnalyticsController`, `TrackController`.
- **Integrations**: `WaMeController` (WhatsApp), `AiController`, `ImagesController` (Cloudinary / uploads).

## How It Connects
- Every request passes through tenant resolution before reaching a controller, so all business data is automatically scoped to the current tenant.
- Branches and warehouses defined here are referenced by Orders, Inventory, and Accounting.
- Account mappings seeded via `MappingSeederController` drive the Accounting module's journal entries.

## Real-time
Live notifications are delivered over SignalR through `NotificationHub` (`/notifications-hub`), surfaced to users via `NotificationsController`.
</content>
