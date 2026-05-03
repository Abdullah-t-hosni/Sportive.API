# POS & Order Management Feature

## Overview
The Point of Sale (POS) system provides a fast, touch-friendly interface for handling sales, returns, and order tracking.

## Core Workflow
1. **Cart Management**: Adding products via search or barcode scanning.
2. **Checkout**: Processing payments (Cash, Card, Installments, or Mixed).
3. **Receipt Generation**: Printing localized receipts with optional SKU and time visibility.
4. **Order Tracking**: Monitoring order status (Pending, Shipped, Delivered, Returned).

## Backend Components
- **Controllers**:
    - `OrdersController`: Main endpoint for order creation and management.
    - `TrackController`: Handles order status updates and history.
    - `CartController`: Persists customer carts across sessions.
- **Services**: Logic for applying coupons, calculating taxes, and updating inventory levels upon sale.

## Frontend UI
- **POS Interface**: Optimized for speed with keyboard shortcuts and barcode scanner support.
- **Order History**: Searchable list of past orders with drill-down capabilities.
- **Returns Workflow**: Structured process for handling partial or full returns.

## Key Logic
- **Stock Validation**: Prevents selling items that are out of stock.
- **Customer Integration**: Link orders to existing customers or create "Walk-in" sales.
- **Permissions**: Only authorized staff can perform sensitive actions like giving discounts or deleting items from a cart.
