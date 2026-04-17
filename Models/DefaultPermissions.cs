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
                (ModuleKeys.Pos,       true, true),
                (ModuleKeys.Orders,    true, true),
                (ModuleKeys.Customers, true, false),
                (ModuleKeys.Products,  true, false),
                (ModuleKeys.Dashboard, true, false),
            },

            AppRoles.Accountant => new[]
            {
                (ModuleKeys.Accounting, true, true),
                (ModuleKeys.Payroll,    true, true),
                (ModuleKeys.Hr,         true, false),
                (ModuleKeys.Reports,    true, false),
                (ModuleKeys.Purchases,  true, false),
                (ModuleKeys.Dashboard,  true, false),
            },

            AppRoles.Staff => new[]
            {
                (ModuleKeys.Orders,    true, false),
                (ModuleKeys.Products,  true, false),
                (ModuleKeys.Customers, true, false),
                (ModuleKeys.Dashboard, true, false),
            },

            _ => Array.Empty<(string, bool, bool)>()
        };
}
