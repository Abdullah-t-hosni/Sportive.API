# Reports & Analytics Feature

## Overview
The reporting engine provides deep insights into business performance through various operational and financial reports.

## Types of Reports
1. **Sales Reports**: Daily, monthly, and yearly sales summaries.
2. **Product Movement**: Tracking how specific items move through the system.
3. **Inventory Reports**: Stock value, low stock alerts, and aging inventory.
4. **Financial Reports**: Profit & Loss, Balance Sheet, General Ledger.
5. **Operational Reports**: Cashier performance, return rates, and coupon usage.

## Backend Implementation
- **Controllers**:
    - `OperationalReportsController`: Handles the majority of business-level reports.
    - `FinancialReportsController`: Focuses on accounting-specific data.
    - `ExportController`: Provides Excel/PDF export functionality.
- **Efficiency**: Uses optimized SQL queries and DTOs to handle large datasets.

## Frontend UI
- **Unified Reporting Dashboard**: A central hub for all report types.
- **High-Density Filters**: Grid-based filter system for date ranges, categories, and sources.
- **Data Visualization**: (Future/Planned) Charts and graphs for key KPIs.

## Exporting
- **Excel**: Using `EPPlus` to generate multi-sheet workbooks.
- **PDF**: Standardized templates for professional document sharing.
