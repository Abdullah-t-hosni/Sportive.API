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
        var posOrders = await _db.Orders
            .Where(o => !string.IsNullOrEmpty(o.SalesPersonId) && 
                       (o.Status == OrderStatus.Pending || o.PaymentStatus == PaymentStatus.Pending))
            .ToListAsync();

        foreach (var order in posOrders)
        {
            order.Status = OrderStatus.Delivered;
            order.PaymentStatus = PaymentStatus.Paid;
            if (order.ActualDeliveryDate == null) 
                order.ActualDeliveryDate = order.CreatedAt;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = posOrders.Count, message = "تم تحديث كافة طلبات الـ POS السابقة بنجاح" });
    }

    /// <summary>
    /// مسح كافة بيانات العملاء وحساباتهم نهائياً (البدء من الصفر)
    /// </summary>
    [HttpPost("wipe-customers")]
    public async Task<IActionResult> WipeCustomers()
    {
        // 1. تحديد كافة مستخدمي "العملاء" فقط (تجنب حذف الأدمن)
        var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
        if (customerRole == null) return BadRequest(new { message = "Role 'Customer' not found" });

        var customerUserIds = await _db.UserRoles
            .Where(ur => ur.RoleId == customerRole.Id)
            .Select(ur => ur.UserId)
            .ToListAsync();

        // 2. مسح البيانات المرتبطة (العناوين، السلال، المفضلة)
        // ملاحظة: الطلبات لا تحذف لأنها مرتبطة بالحسابات المالية
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
        
        // التأكد من عدم حذف الأدمن (زيادة في الأمان)
        usersToDelete = usersToDelete.Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com").ToList();
        
        _db.Users.RemoveRange(usersToDelete);

        try 
        {
            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = $"تم مسح {customers.Count} عميل و {usersToDelete.Count} حساب مستخدم بنجاح" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = "حدث خطأ أثناء المسح: " + ex.Message });
        }
    }
}
