# Project Architecture

## Overview
Sportive is a comprehensive POS (Point of Sale), Inventory Management, and Accounting system designed for retail businesses. It follows a decoupled architecture with a C# .NET Backend and a React Frontend.

## Backend Architecture (Sportive.API)
The backend is built using **ASP.NET Core 9.0** following a clean, modular controller-service pattern.

### Layers
1.  **Controllers**: Entry points for HTTP requests. They handle request validation and delegate business logic to services.
2.  **Services**: Contain the core business logic. They interact with repositories or the database context directly for data operations.
3.  **Data/Models**: 
    - `ApplicationDbContext`: The Entity Framework Core context.
    - `Models`: Database entities representing the schema.
    - `DTOs` (Data Transfer Objects): Used for transferring data between the API and clients, ensuring internal models are not exposed directly.
4.  **Authorization**: Custom permission-based system using attributes like `[RequirePermission]`.
5.  **Middleware**: Handles cross-cutting concerns like Exception Handling, Logging, and Authentication.

### Data Flow
- **Request**: Client (Frontend) -> Controller -> Service -> Database.
- **Response**: Database -> Service -> DTO -> Controller -> Client.

## Frontend Architecture (sportive-frontend)
The frontend is a modern SPA built with **React** and **Vite**.

### Core Principles
- **Feature-Based Structure**: Components and logic are grouped by feature (e.g., POS, Admin, Inventory).
- **State Management**: Uses **TanStack Query (React Query)** for server-state management (caching, synchronization).
- **Styling**: **Tailwind CSS** for responsive and utility-first styling.
- **Localization**: **i18next** for multi-language support (Arabic & English).

### Key Modules
- **Services**: Abstract API calls using Axios.
- **Hooks**: Custom React hooks for reusable logic.
- **Components**: Divided into generic UI components and feature-specific components.
