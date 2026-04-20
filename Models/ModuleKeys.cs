namespace Sportive.API.Models;

public static class ModuleKeys
{
    public const string Dashboard = "dashboard";
    public const string Pos = "pos";

    // Sales
    public const string OrdersMain = "orders-main";
    public const string Orders = "orders";
    public const string ReturnsFull = "returns-full";
    public const string ReturnsPartial = "returns-partial";
    public const string Customers = "customers";
    public const string CustomerReceipts = "customer-receipts";
    public const string Reviews = "reviews";

    // Products
    public const string ProductsGroup = "products-group";
    public const string Products = "products";
    public const string Categories = "categories";
    public const string Brands = "brands";
    public const string Units = "units";
    public const string Barcode = "barcode";

    // Promotions
    public const string PromotionsGroup = "promotions-group";
    public const string Promotions = "promotions";
    public const string Coupons = "coupons";
    public const string Discounts = "discounts";

    // Inventory
    public const string InventoryGroup = "inventory-group";
    public const string Inventory = "inventory";
    public const string InventoryOpening = "inventory-opening";
    public const string Import = "import";
    public const string InventoryCount = "inventory-count";

    // Purchases
    public const string PurchasesGroup = "purchases-group";
    public const string PurchasesMain = "purchases-main";
    public const string Suppliers = "suppliers";
    public const string Purchases = "purchases";
    public const string PurchaseReturns = "purchase-returns";
    public const string SupplierVouchers = "vouchers.payments";

    // Accounting
    public const string AccountingGroup = "accounting-group";
    public const string AccountingMain = "accounting-main";
    public const string Chart = "chart";
    public const string Mapping = "mapping";
    public const string Journal = "journal";
    public const string Receipts = "receipts";
    public const string Payments = "payments";

    // Assets
    public const string AssetsGroup = "assets-group";
    public const string AssetsMain = "fixed-assets-main";
    public const string Assets = "assets";
    public const string AssetCategories = "asset-categories";
    public const string AssetDepBatch = "asset-dep-batch";
    public const string AssetDisposals = "asset-disposals";

    // HR
    public const string Hr = "hr";
    public const string HrEmployees = "hr-employees";
    public const string HrEmpList = "hr-emp-list";
    public const string HrVouchers = "hr-vouchers";
    public const string HrPayroll = "hr-payroll";
    public const string HrAdvances = "hr-advances";

    // System
    public const string System = "system";
    public const string ReportsMain = "reports-main";
    public const string Staff = "staff";
    public const string Settings = "settings";
    public const string Maintenance = "maintenance";
    public const string Diagnostics = "diagnostics";
    public const string Backup = "backup";

    public static readonly string[] All = {
        Dashboard, Pos,
        OrdersMain, Orders, ReturnsFull, ReturnsPartial, Customers, CustomerReceipts, Reviews,
        ProductsGroup, Products, Categories, Brands, Units, Barcode,
        PromotionsGroup, Promotions, Coupons, Discounts,
        InventoryGroup, Inventory, InventoryOpening, Import, InventoryCount,
        PurchasesGroup, PurchasesMain, Suppliers, Purchases, PurchaseReturns, SupplierVouchers,
        AccountingGroup, AccountingMain, Chart, Mapping, Journal, Receipts, Payments,
        AssetsGroup, AssetsMain, Assets, AssetCategories, AssetDepBatch, AssetDisposals,
        Hr, HrEmployees, HrEmpList, HrVouchers, HrPayroll, HrAdvances,
        System, ReportsMain, Staff, Settings, Maintenance, Diagnostics, Backup
    };
}
