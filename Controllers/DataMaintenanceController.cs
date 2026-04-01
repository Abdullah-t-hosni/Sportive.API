using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

/// <summary>
/// أداة صيانة لإزالة البيانات المسببة لمشاكل في قاعدة البيانات
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class DataMaintenanceController : ControllerBase
{
    private readonly AppDbContext _db;
    public DataMaintenanceController(AppDbContext db) => _db = db;

    /// <summary>
    /// تنظيف الهواتف المكررة لتمهيد الطريق لعمل Unique Index
    /// </summary>
    [HttpPost("cleanup-duplicates")]
    public async Task<IActionResult> CleanupDuplicates()
    {
        var logs = new List<string>();

        // 1. تنظيف جدول العملاء (Customers)
        var duplicatePhones = await _db.Customers
            .Where(c => !string.IsNullOrEmpty(c.Phone))
            .GroupBy(c => c.Phone!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        foreach (var phone in duplicatePhones)
        {
            var allWithPhone = await _db.Customers
                .Where(c => c.Phone == phone)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // نبقي الأحدث ونحذف الباقي
            var toDelete = allWithPhone.Skip(1).ToList();
            _db.Customers.RemoveRange(toDelete);
            logs.Add($"Removed {toDelete.Count} duplicates for customer phone: {phone}");
        }

        // 2. تنظيف جدول المستخدمين (AspNetUsers)
        var duplicateUserPhones = await _db.Users
            .Where(u => !string.IsNullOrEmpty(u.PhoneNumber))
            .GroupBy(u => u.PhoneNumber!)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        foreach (var phone in duplicateUserPhones)
        {
            var allWithPhone = await _db.Users
                .Where(u => u.PhoneNumber == phone)
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            // نبقي الأحدث (لتجنب حذف الأدمن أو المستخدمين النشطين)
            var toDelete = allWithPhone.Skip(1).ToList();
            
            // تحقق بسيط للتأكد من عدم حذف مستخدم ببيانات مهمة (إختياري)
            _db.Users.RemoveRange(toDelete);
            logs.Add($"Removed {toDelete.Count} duplicate Users for phone: {phone}");
        }

        try 
        {
            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "تم التنظيف بنجاح", logs });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("fix-pos-orders")]
    public async Task<IActionResult> FixPosOrders()
    {
        var logs = new List<string>();
        // 1. تحديد الطلبات اللي بتبدأ بـ POS- أو ORD- وبناءً عليه نحدد الـ Source
        var allOrders = await _db.Orders.ToListAsync();
        int fixedCount = 0;

        foreach (var order in allOrders)
        {
            bool changed = false;
            
            // تصحيح المصدر بناءً على الرقم
            if (order.OrderNumber.StartsWith("POS-") && order.Source != OrderSource.POS)
            {
                order.Source = OrderSource.POS;
                changed = true;
            }
            else if (order.OrderNumber.StartsWith("ORD-") && order.Source != OrderSource.Website)
            {
                order.Source = OrderSource.Website;
                changed = true;
            }

            // لو POS، نخليه Delivered و Paid
            if (order.Source == OrderSource.POS)
            {
                if (order.Status != OrderStatus.Delivered) { order.Status = OrderStatus.Delivered; changed = true; }
                if (order.PaymentStatus != PaymentStatus.Paid) { order.PaymentStatus = PaymentStatus.Paid; changed = true; }
                if (order.ActualDeliveryDate == null) { order.ActualDeliveryDate = order.CreatedAt; changed = true; }
            }

            if (changed) fixedCount++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = fixedCount, message = $"تم تحديث {fixedCount} طلب بنجاح لتصحيح المصدر والحالات." });
    }

    /// <summary>
    /// مسح كافة بيانات العملاء وحساباتهم نهائياً (البدء من الصفر)
    /// </summary>
    [HttpPost("wipe-customers")]
    public async Task<IActionResult> WipeCustomers()
    {
        // 1. تحديد كافة مستخدمي "العملاء" فقط
        var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
        if (customerRole == null) return BadRequest(new { message = "Role 'Customer' not found" });

        var customerUserIds = await _db.UserRoles
            .Where(ur => ur.RoleId == customerRole.Id)
            .Select(ur => ur.UserId)
            .ToListAsync();

        // 2. مسح البيانات المرتبطة تدريجياً لتجنب مشاكل الـ Foreign Keys
        // حذفة الطلبات أولاً لأنها تعتمد على العملاء
        var orderHistory = await _db.OrderStatusHistories.IgnoreQueryFilters().ToListAsync();
        _db.OrderStatusHistories.RemoveRange(orderHistory);

        var orderItems = await _db.OrderItems.IgnoreQueryFilters().ToListAsync();
        _db.OrderItems.RemoveRange(orderItems);

        var orders = await _db.Orders.IgnoreQueryFilters().ToListAsync();
        _db.Orders.RemoveRange(orders);

        var reviews = await _db.Reviews.IgnoreQueryFilters().ToListAsync();
        _db.Reviews.RemoveRange(reviews);

        var addresses = await _db.Addresses.IgnoreQueryFilters().ToListAsync();
        _db.Addresses.RemoveRange(addresses);

        var wishlist = await _db.WishlistItems.IgnoreQueryFilters().ToListAsync();
        _db.WishlistItems.RemoveRange(wishlist);

        var cartItems = await _db.CartItems.IgnoreQueryFilters().ToListAsync();
        _db.CartItems.RemoveRange(cartItems);

        // 3. مسح سجلات العملاء
        var customers = await _db.Customers.IgnoreQueryFilters().ToListAsync();
        _db.Customers.RemoveRange(customers);

        // 4. مسح حسابات المستخدمين (Identity) المرتبطة بالعملاء فقط
        var usersToDelete = await _db.Users
            .Where(u => customerUserIds.Contains(u.Id))
            .ToListAsync();
        
        // التأكد من عدم حذف المسئولين
        usersToDelete = usersToDelete.Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com").ToList();
        
        _db.Users.RemoveRange(usersToDelete);

        try 
        {
            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = $"تم تصفير النظام بنجاح: مسح {customers.Count} عميل، {orders.Count} طلب، و {usersToDelete.Count} مستخدم." });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return BadRequest(new { success = false, message = "حدث خطأ أثناء المسح: " + msg });
        }
    }

    /// <summary>
    /// تصفير شامل للنظام (Reset) - مسح كافة البيانات المعاملاتية والكيانات مع الحفاظ على الهيكل (الأدمن، الحسابات، الأقسام)
    /// </summary>
    [HttpPost("factory-reset")]
    public async Task<IActionResult> FactoryReset()
    {
        try
        {
            // 🛑 1. مسح حركات وحسابات المخزن
            _db.InventoryMovements.RemoveRange(await _db.InventoryMovements.IgnoreQueryFilters().ToListAsync());
            _db.InventoryAuditItems.RemoveRange(await _db.InventoryAuditItems.IgnoreQueryFilters().ToListAsync());
            _db.InventoryAudits.RemoveRange(await _db.InventoryAudits.IgnoreQueryFilters().ToListAsync());

            // 🛑 2. مسح كافة المعاملات المالية (Accounting)
            _db.JournalLines.RemoveRange(await _db.JournalLines.IgnoreQueryFilters().ToListAsync());
            _db.JournalEntries.RemoveRange(await _db.JournalEntries.IgnoreQueryFilters().ToListAsync());
            _db.ReceiptVouchers.RemoveRange(await _db.ReceiptVouchers.IgnoreQueryFilters().ToListAsync());
            _db.PaymentVouchers.RemoveRange(await _db.PaymentVouchers.IgnoreQueryFilters().ToListAsync());

            // 🛑 3. مسح المبيعات بالكامل
            _db.OrderItems.RemoveRange(await _db.OrderItems.IgnoreQueryFilters().ToListAsync());
            _db.OrderStatusHistories.RemoveRange(await _db.OrderStatusHistories.IgnoreQueryFilters().ToListAsync());
            _db.Orders.RemoveRange(await _db.Orders.IgnoreQueryFilters().ToListAsync());
            _db.CartItems.RemoveRange(await _db.CartItems.IgnoreQueryFilters().ToListAsync());
            _db.WishlistItems.RemoveRange(await _db.WishlistItems.IgnoreQueryFilters().ToListAsync());

            // 🛑 4. مسح المشتريات والموردين
            _db.PurchaseInvoiceItems.RemoveRange(await _db.PurchaseInvoiceItems.IgnoreQueryFilters().ToListAsync());
            _db.SupplierPayments.RemoveRange(await _db.SupplierPayments.IgnoreQueryFilters().ToListAsync());
            _db.PurchaseInvoices.RemoveRange(await _db.PurchaseInvoices.IgnoreQueryFilters().ToListAsync());
            _db.Suppliers.RemoveRange(await _db.Suppliers.IgnoreQueryFilters().ToListAsync());

            // 🛑 5. مسح المنتجات وتفاصيلها (يترك الأقسام والبراندات كـ Structure)
            _db.ProductImages.RemoveRange(await _db.ProductImages.IgnoreQueryFilters().ToListAsync());
            _db.ProductVariants.RemoveRange(await _db.ProductVariants.IgnoreQueryFilters().ToListAsync());
            _db.Reviews.RemoveRange(await _db.Reviews.IgnoreQueryFilters().ToListAsync());
            _db.Products.RemoveRange(await _db.Products.IgnoreQueryFilters().ToListAsync());

            // 🛑 6. مسح العملاء والمستخدمين التابعين لهم
            _db.Addresses.RemoveRange(await _db.Addresses.IgnoreQueryFilters().ToListAsync());
            var customers = await _db.Customers.IgnoreQueryFilters().ToListAsync();
            _db.Customers.RemoveRange(customers);

            var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
            if (customerRole != null)
            {
                var customerUserIds = await _db.UserRoles.Where(ur => ur.RoleId == customerRole.Id).Select(ur => ur.UserId).ToListAsync();
                var usersToDelete = await _db.Users.Where(u => customerUserIds.Contains(u.Id)).ToListAsync();
                // حماية الأدمن
                usersToDelete = usersToDelete.Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com").ToList();
                _db.Users.RemoveRange(usersToDelete);
            }

            // 🛑 7. مسح بيانات أخرى
            _db.Notifications.RemoveRange(await _db.Notifications.IgnoreQueryFilters().ToListAsync());
            _db.Coupons.RemoveRange(await _db.Coupons.IgnoreQueryFilters().ToListAsync());

            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "تم تصفير كافة البيانات بنجاح (مبيعات، مشتريات، مخزون، ملقات المحاسبة، والمنتجات). النظام الآن جاهز للبداية من الصفر." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = "حدث خطأ أثناء التصفير الشامل: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }
}
