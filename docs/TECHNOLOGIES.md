# Technologies & Stack

## Backend (Sportive.API)
- **Framework**: .NET 9.0 (ASP.NET Core)
- **Language**: C#
- **Database**: MySQL 8.x (managed via Entity Framework Core)
- **ORM**: Entity Framework Core (EF Core)
- **Authentication**: JWT (JSON Web Tokens) with ASP.NET Core Identity
- **Real-time**: SignalR (for notifications and live updates)
- **API Documentation**: Swagger/OpenAPI
- **Validation**: FluentValidation
- **Caching**: In-Memory & Redis (optional)
- **Exporting**: EPPlus (Excel), QuestPDF/iTextSharp (PDF)

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
- **Deployment**: Docker support provided via Dockerfile.
- **Database Hosting**: Supports local MySQL or remote hosts (e.g., Hostinger).
- **Versioning**: Git
