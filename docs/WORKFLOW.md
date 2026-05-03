# System Workflow & Integration

## Communication
The Frontend (React) communicates with the Backend (ASP.NET Core) exclusively via a RESTful JSON API.

## Typical Request Workflow
1. **Authentication**: 
    - User logs in via `AuthController`.
    - Backend returns a JWT (access token) and Refresh Token.
    - Frontend stores the JWT in memory (or secure storage) and include it in the `Authorization: Bearer <token>` header for subsequent requests.
2. **Data Fetching**:
    - Frontend uses **TanStack Query** hooks.
    - Hooks call **Axios** services which target specific API endpoints.
    - Results are cached on the client to reduce redundant network calls.
3. **Real-time Updates**:
    - For notifications or live stock updates, the system uses **SignalR**.
    - Frontend establishes a connection to a Hub on the backend.

## Localization Workflow
- The system is fully bilingual (Arabic/English).
- **Frontend**: Uses `i18next`. Translation files are located in `src/locales/`. 
- **Backend**: Returns localized error messages or data based on the `Accept-Language` header.
- **Database**: Some fields (like names) may have dual-language support or be stored in a way that allows translation.

## Printing Workflow
- The system uses browser-native printing.
- CSS `@media print` rules are used to hide UI elements (navbars, buttons) and style the document for paper (A4 or thermal receipts).
- Templates are built as standard React components that only render during the print process.

## Error Handling
- **Backend**: Centralized middleware catches all exceptions and returns a standardized JSON error object.
- **Frontend**: Axios interceptors detect 401 (Unauthorized) or 500 (Server Error) and trigger appropriate UI alerts (Toasts) or redirects.
