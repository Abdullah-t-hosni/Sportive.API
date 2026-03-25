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
}
