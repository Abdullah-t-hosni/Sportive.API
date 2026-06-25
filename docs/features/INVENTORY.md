# Inventory Management Feature

## Overview
The Inventory module is the core of the Sportive system, managing products, stock levels, variants, and adjustments.

## Core Components
- **Products**: Management of items with details like SKU, Barcode, Brand, and Category.
- **Variants (Sizes/Colors)**: Support for product variations using `SizeGroups`.
- **Inventory Audits**: Workflow for periodic stock counting and variance reporting.
- **Adjustments**: Manual increases or decreases in stock with reason codes.
- **Opening Balances**: Setting initial stock levels for new installations.

## Scope
This module is broad. Beyond core stock control it also covers **purchasing** (suppliers, purchase invoices/returns, supplier payments), **warehouses & stock transfers**, **fixed assets & depreciation**, and **promotions** (product discounts, special offers).

## Backend Logic (`Controllers/Inventory`, `Services/Inventory`)
- **Catalog**: `ProductsController`, `ProductUnitsController`, `CategoriesController`, `BrandController`, `SizeGroupsController`, `ColorGroupsController`, `BarcodeController`.
- **Stock**: `InventoryAdjustmentsController`, `InventoryAuditsController`, `InventoryOpeningBalanceController`, `StockTransfersController`, `InventoryIntelligenceController` (low-stock alerts & movement analytics).
- **Purchasing**: `SuppliersController`, `PurchaseInvoicesController`, `PurchaseReturnsController`, `SupplierPaymentsController`, `AssetPurchasesController`.
- **Assets & Promotions**: `FixedAssetsController`, `ProductDiscountsController`, `SpecialOffersController`.
- **Services**: Logic for calculating available stock, logging inventory movements (per warehouse), processing adjustments, and generating barcodes.

## Frontend UI
- **Catalog View**: A high-performance grid for browsing products.
- **Audit Interface**: A spreadsheet-style layout for rapid data entry during stock takes.
- **Barcode Integration**: Native support for scanning to search or increment quantities.

## Data Relationships
- `Product` belongs to `Category` and `Brand`.
- `Product` has many `InventoryMovements`.
- `InventoryAudit` tracks `AuditItems` against current `Stock`.
