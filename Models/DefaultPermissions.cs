namespace Sportive.API.Models;

public static class DefaultPermissions
{
    public static IReadOnlyList<string> ForRole(string role)
    {
        var list = new List<string>();
        IEnumerable<(string, bool, bool)> oldFormat = role switch
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
                (ModuleKeys.Cashier,        true, true),
                (ModuleKeys.Performance,    true, true),
                (ModuleKeys.Orders,         true, true),
                (ModuleKeys.ReturnsFull,    true, true),
                (ModuleKeys.ReturnsPartial, true, true),
                (ModuleKeys.Customers,      true, false),
                (ModuleKeys.Products,       true, false),
                (ModuleKeys.Dashboard,      true, false),
                (ModuleKeys.Ai,             true, true),
            },

            AppRoles.Accountant => new[]
            {
                (ModuleKeys.AccountingMain, true, true),
                (ModuleKeys.Chart,          true, true),
                (ModuleKeys.Journal,        true, true),
                (ModuleKeys.Receipts,       true, true),
                (ModuleKeys.Payments,       true, true),
                (ModuleKeys.Installments,   true, true),
                (ModuleKeys.HrPayroll,      true, true),
                (ModuleKeys.HrAdvances,     true, true),
                (ModuleKeys.ReportsMain,    true, false),
                (ModuleKeys.Suppliers,      true, false),
                (ModuleKeys.Purchases,      true, false),
                (ModuleKeys.Dashboard,      true, false),
                (ModuleKeys.Ai,             true, true),
            },

            AppRoles.Staff => new[]
            {
                (ModuleKeys.OrdersMain,     true, false),
                (ModuleKeys.Products,       true, false),
                (ModuleKeys.Customers,      true, false),
                (ModuleKeys.Inventory,      true, true),
                (ModuleKeys.Dashboard,      true, false),
                (ModuleKeys.Ai,             true, true),
            },

            _ => Array.Empty<(string, bool, bool)>()
        };
        foreach (var item in oldFormat) {
            if (item.Item2) list.Add(item.Item1);
            if (item.Item3) list.Add(item.Item1 + ".edit");
        }
        return list;
    }
}
