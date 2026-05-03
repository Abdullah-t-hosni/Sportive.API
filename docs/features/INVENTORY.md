# Inventory Management Feature

## Overview
The Inventory module is the core of the Sportive system, managing products, stock levels, variants, and adjustments.

## Core Components
- **Products**: Management of items with details like SKU, Barcode, Brand, and Category.
- **Variants (Sizes/Colors)**: Support for product variations using `SizeGroups`.
- **Inventory Audits**: Workflow for periodic stock counting and variance reporting.
- **Adjustments**: Manual increases or decreases in stock with reason codes.
- **Opening Balances**: Setting initial stock levels for new installations.

## Backend Logic
- **Controllers**: 
    - `ProductsController`: CRUD for products.
    - `InventoryAuditsController`: Handles the auditing process.
    - `InventoryIntelligenceController`: Provides analytics on stock movement and low stock alerts.
- **Services**: Logic for calculating available stock, processing adjustments, and generating barcodes.

## Frontend UI
- **Catalog View**: A high-performance grid for browsing products.
- **Audit Interface**: A spreadsheet-style layout for rapid data entry during stock takes.
- **Barcode Integration**: Native support for scanning to search or increment quantities.

## Data Relationships
- `Product` belongs to `Category` and `Brand`.
- `Product` has many `InventoryMovements`.
- `InventoryAudit` tracks `AuditItems` against current `Stock`.
