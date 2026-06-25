# Accounting & Finance Feature

## Overview
A comprehensive accounting module that integrates sales data directly into financial ledgers, ensuring real-time financial tracking.

## Key Sub-modules
- **General Ledger**: Automatic recording of all transactions.
- **Accounts Payable/Receivable**: Tracking debts to suppliers and from customers.
- **Supplier Payments**: Managing outgoing payments and balancing supplier accounts.
- **Financial Reports**: Balance Sheets, Profit & Loss, and Income Statements.
- **Fixed Assets**: Tracking long-term assets and depreciation.

## Backend Implementation (`Controllers/Accounting`, `Services/Accounting`)
- **Controllers**:
    - `AccountsController`: Chart of accounts.
    - `JournalEntriesController`: Core ledger / journal entry management.
    - `FinancialReportsController`: Aggregates data for high-level financial reporting (P&L, balance sheet).
    - `ReceiptVouchersController` / `PaymentVouchersController`: Recording incoming and outgoing payments.
    - `POSReportController` / `POSShiftClosuresController`: Cashier shift reconciliation.
    - `ProfitabilityController`: Margin and profitability analysis.
- **Services**: `SalesAccountingService` and `PurchaseAccountingService` build balanced journal entries; `AccountingCoreService` posts and validates them against a mapped chart of accounts.
- **Architecture**: Sales accounting is posted **inside the same transaction** as order creation, so an order and its ledger entry are 1:1 (both commit or both roll back). Supplier settlements live in the Inventory module's `SupplierPaymentsController`.

## Frontend Features
- **Ledger View**: Filterable list of all journal entries.
- **Reporting Dashboard**: Visual representation of financial health.
- **Payment Vouchers**: Forms for recording manual payments.

## Workflow
1. A sale is completed in the POS.
2. The system automatically generates a Journal Entry.
3. The Cash/Bank account is debited, and Sales Revenue is credited.
4. Inventory Asset is credited, and Cost of Goods Sold (COGS) is debited.
