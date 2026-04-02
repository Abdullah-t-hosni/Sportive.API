using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

/// <summary>
/// 🔒 محمي بالكامل — Admin فقط
/// يُستخدم لتصحيح مخطط قاعدة البيانات يدوياً عند الحاجة
/// يُفضَّل تشغيل Migrations بدلاً من هذا الـ Controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]  // ✅ FIX: كان بدون Authorize — الآن Admin فقط
public class SchemaFixController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SchemaFixController> _logger;

    public SchemaFixController(AppDbContext db, ILogger<SchemaFixController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("run-v3")]
    public async Task<IActionResult> Run()
    {
        _logger.LogWarning("SchemaFix run-v3 triggered by admin user: {User}", User.Identity?.Name);
        try
        {
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS VendorAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS InventoryAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS ExpenseAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS VatAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS CashAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Suppliers ADD COLUMN IF NOT EXISTS MainAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("UPDATE Accounts SET AllowPosting = 1 WHERE IsDeleted = 0;");

            return Ok(new { message = "Schema updated successfully (V3.1 - Account Posting Enabled)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SchemaFix failed");
            // ✅ FIX: لا نكشف stack trace للعميل حتى لو Admin
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
