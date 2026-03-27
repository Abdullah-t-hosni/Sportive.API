// ══════════════════════════════════════════════════════
// Sportive — نظام الصلاحيات
// ══════════════════════════════════════════════════════
//
// الأدوار:
//   Admin      — مدير كامل: كل حاجة
//   Manager    — مدير فرع: كل حاجة ما عدا إعدادات النظام
//   Cashier    — كاشير: POS فقط
//   Accountant — محاسب: التقارير المالية فقط
//   Staff      — موظف: الطلبات + المنتجات (عرض فقط)
//   Customer   — عميل الموقع
//
// ══════════════════════════════════════════════════════

namespace Sportive.API.Models;

public static class AppRoles
{
    public const string Admin      = "Admin";
    public const string Manager    = "Manager";
    public const string Cashier    = "Cashier";
    public const string Accountant = "Accountant";
    public const string Staff      = "Staff";
    public const string Customer   = "Customer";

    // Groups للـ Authorize attributes
    public const string AdminOrManager    = "Admin,Manager";
    public const string AdminOrManagerOrStaff = "Admin,Manager,Staff";
    public const string AllStaff          = "Admin,Manager,Cashier,Accountant,Staff";
    public const string PosAccess         = "Admin,Manager,Cashier";
    public const string ReportsAccess     = "Admin,Manager,Accountant";
    public const string OrdersAccess      = "Admin,Manager,Staff,Cashier";

    public static readonly string[] All = {
        Admin, Manager, Cashier, Accountant, Staff, Customer
    };

    public static readonly string[] StaffRoles = {
        Admin, Manager, Cashier, Accountant, Staff
    };
}
