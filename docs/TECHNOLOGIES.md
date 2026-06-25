# Technologies & Stack

## Backend (Sportive.API)
- **Framework**: .NET 9.0 (ASP.NET Core)
- **Language**: C#
- **Database**: MySQL 8.x
- **ORM**: Entity Framework Core (Pomelo MySQL provider)
- **Multi-tenancy**: Database-per-tenant, with a separate master registry database
- **Authentication**: JWT (JSON Web Tokens) with ASP.NET Core Identity
- **Authorization**: Roles + custom fine-grained `[RequirePermission]` module permissions
- **Real-time**: SignalR (notifications and live updates)
- **Background jobs**: `IHostedService` (daily HR task generator) + Hangfire (scheduled jobs)
- **API Documentation**: Swagger / OpenAPI
- **Validation**: FluentValidation
- **Caching**: In-Memory by default, Redis optional
- **Logging & Tracing**: Serilog (console + rolling files), OpenTelemetry (OTLP)
- **Exporting**: ClosedXML (Excel), QuestPDF (PDF)
- **Media**: Cloudinary + local static file uploads
- **Compression**: Brotli / Gzip response compression

## Integrations
- **Paymob** — online payment processing
- **Egyptian Tax Authority (ETA) / ZATCA** — e-invoicing
- **WhatsApp Cloud API** — customer messaging
- **ZK biometric devices** — attendance clock (`iclock` protocol)
- **SMTP** — transactional & backup emails

## Frontend (sportive-frontend)
- **Framework**: React 18+
- **Build Tool**: Vite
- **Language**: TypeScript
- **Styling**: Tailwind CSS, Headless UI, Lucide Icons
- **State Management**: TanStack Query (React Query)
- **Forms**: React Hook Form
- **Localization**: i18next
- **Routing**: React Router DOM
- **HTTP Client**: Axios
- **Printing**: Direct browser printing with custom CSS media queries

## Infrastructure
- **Deployment**: Docker (`Dockerfile`, image based on `aspnet:9.0`, listens on port 8080, includes `mysql-client` for backups).
- **Database Hosting**: Supports local MySQL or remote hosts (e.g., Hostinger).
- **Versioning**: Git
</content>
