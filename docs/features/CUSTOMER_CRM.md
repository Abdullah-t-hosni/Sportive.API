# CRM & Customer Management Feature

## Overview
Manages the relationship between the business and its customers, including profiles, history, and loyalty.

## Key Features
- **Customer Profiles**: Storing contact info, preferences, and purchase history.
- **Customer Categories**: Grouping customers (e.g., Retail, Wholesale, VIP) for specialized pricing or discounts.
- **Wishlists & Reviews**: Allowing customers to save items for later and provide feedback.
- **Coupons & Discounts**: Managing marketing campaigns and promotional codes.

## Backend Implementation (`Controllers/Customers`, `Services/Customers`)
- **Controllers**:
    - `CustomersController`: CRUD for customer data.
    - `CustomerCategoryController`: Management of customer groups / tiers.
    - `WishlistController` & `ReviewsController`: Customer engagement features.
    - (Coupons live in the Orders module's `CouponsController`.)
- **Privacy**: Customer PII is stored **encrypted**; lookups (phone/email) are done via search hashes.
- **Integration**: Customers are linked to orders and carts; `CustomerService` auto-creates a profile on register/login and re-tiers customers based on purchase totals.

## Frontend UI
- **Customer Directory**: Searchable list of all registered customers.
- **Profile View**: Detailed timeline of customer interactions and orders.
- **Marketing Tools**: Interface for creating and tracking coupon performance.
