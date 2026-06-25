# HR & Staff Management Feature

## Overview
Manages the organizational structure, employees, and their access rights within the system.

## Key Features
- **Staff Profiles**: Personal and professional details of employees.
- **Role-Based Access Control (RBAC)**: Fine-grained permissions (e.g., `CanManageInventory`, `CanViewReports`).
- **User Authentication**: Secure login using JWT.
- **Performance Tracking**: Monitoring cashier sales and operational efficiency.

## Backend Components (`Controllers/HR`, `Services/HR`)
- **People**: `EmployeesController`, `StaffController`, `DepartmentsController`, `ResponsibilityTypesController`.
- **Attendance**: `EmployeeAttendancesController`, `IclockController` (ZK biometric clock devices), `SelfServiceController` (self punch-in/out).
- **Payroll & pay items**: `PayrollController`, `EmployeeBonusesController`, `EmployeeDeductionsController`, `EmployeeAdvancesController`.
- **Commissions**: `EmployeeCommissionsController`, `CommissionGroupsController`, `CommissionSchemesController`, `CashierPerformanceController`.
- **Tasks**: `EmployeeTasksController`, `TaskBlueprintsController`, backed by the hosted `TaskGenerationService` that creates daily tasks from blueprints.
- (Auth, user accounts, and role assignment live in `Controllers/System`: `AuthController`, `UsersController`.)

## Permissions System
The system uses a custom `[RequirePermission]` attribute on controller actions. 
- Permissions are cached (TTL 15m) to improve performance.
- Roles like `Admin` and `Manager` often have bypasses for certain checks.

## Frontend UI
- **Staff Management Page**: Form for adding/editing staff and assigning roles.
- **Permissions Grid**: A matrix for enabling/disabling specific privileges per role or user.
