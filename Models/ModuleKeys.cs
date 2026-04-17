namespace Sportive.API.Models;

public static class ModuleKeys
{
    public const string Dashboard  = "dashboard";
    public const string Orders     = "orders";
    public const string Products   = "products";
    public const string Hr         = "hr";
    public const string Payroll    = "payroll";
    public const string Reports    = "reports";
    public const string Staff      = "staff";
    public const string Accounting = "accounting";
    public const string Pos        = "pos";
    public const string Customers  = "customers";
    public const string Inventory  = "inventory";
    public const string Purchases  = "purchases";

    public static readonly string[] All = {
        Dashboard, Orders, Products, Hr, Payroll,
        Reports, Staff, Accounting, Pos, Customers, Inventory, Purchases
    };
}
