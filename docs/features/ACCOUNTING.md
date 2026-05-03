# Accounting & Finance Feature

## Overview
A comprehensive accounting module that integrates sales data directly into financial ledgers, ensuring real-time financial tracking.

## Key Sub-modules
- **General Ledger**: Automatic recording of all transactions.
- **Accounts Payable/Receivable**: Tracking debts to suppliers and from customers.
- **Supplier Payments**: Managing outgoing payments and balancing supplier accounts.
- **Financial Reports**: Balance Sheets, Profit & Loss, and Income Statements.
- **Fixed Assets**: Tracking long-term assets and depreciation.

## Backend Implementation
- **Controllers**:
    - `AccountingControllers`: Core ledger and transaction management.
    - `FinancialReportsController`: Aggregates data for high-level financial reporting.
    - `SupplierPaymentsController`: Specific logic for vendor settlements.
- **Architecture**: Transactions are processed through a centralized accounting service to ensure atomicity.

## Frontend Features
- **Ledger View**: Filterable list of all journal entries.
- **Reporting Dashboard**: Visual representation of financial health.
- **Payment Vouchers**: Forms for recording manual payments.

## Workflow
1. A sale is completed in the POS.
2. The system automatically generates a Journal Entry.
3. The Cash/Bank account is debited, and Sales Revenue is credited.
4. Inventory Asset is credited, and Cost of Goods Sold (COGS) is debited.
