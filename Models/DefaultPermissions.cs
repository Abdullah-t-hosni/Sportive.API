namespace Sportive.API.Models;

public static class DefaultPermissions
{
    public static IReadOnlyList<(string ModuleKey, bool CanView, bool CanEdit)> ForRole(string role)
        => role switch
        {
            AppRoles.Admin => ModuleKeys.All.Select(m => (m, true, true)).ToList(),

            AppRoles.Manager => ModuleKeys.All
                .Where(m => m != ModuleKeys.Staff)
                .Select(m => (m, true, true))
                .Append((ModuleKeys.Staff, true, false))
                .ToList(),

            AppRoles.Cashier => new[]
            {
                (ModuleKeys.Pos,            true, true),
                (ModuleKeys.Orders,         true, true),
                (ModuleKeys.ReturnsFull,    true, true),
                (ModuleKeys.ReturnsPartial, true, true),
                (ModuleKeys.Customers,      true, false),
                (ModuleKeys.Products,       true, false),
                (ModuleKeys.Dashboard,      true, false),
            },

            AppRoles.Accountant => new[]
            {
                (ModuleKeys.AccountingMain, true, true),
                (ModuleKeys.Chart,          true, true),
                (ModuleKeys.Journal,        true, true),
                (ModuleKeys.Receipts,       true, true),
                (ModuleKeys.Payments,       true, true),
                (ModuleKeys.HrPayroll,      true, true),
                (ModuleKeys.HrAdvances,     true, true),
                (ModuleKeys.ReportsMain,    true, false),
                (ModuleKeys.Suppliers,      true, false),
                (ModuleKeys.Purchases,      true, false),
                (ModuleKeys.Dashboard,      true, false),
            },

            AppRoles.Staff => new[]
            {
                (ModuleKeys.Orders,         true, false),
                (ModuleKeys.Products,       true, false),
                (ModuleKeys.Customers,      true, false),
                (ModuleKeys.Inventory,      true, true),
                (ModuleKeys.Dashboard,      true, false),
            },

            _ => Array.Empty<(string, bool, bool)>()
        };
}
