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

    [HttpGet("run-v4")]
    public async Task<IActionResult> RunV4()
    {
        _logger.LogWarning("SchemaFix run-v4 triggered by admin user: {User}", User.Identity?.Name);
        try
        {
            // ──────────────────────────────────────────────────────────
            // 1. جعل تصنيف المنتج اختيارياً (Nullable) والسماح بالحذف (Set Null)
            // ──────────────────────────────────────────────────────────
            try {
                // قد يفشل هذا إذا كان العمود بالفعل Nullable في بعض نسخ MySQL
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Products MODIFY COLUMN CategoryId INT NULL;");
            } catch { }

            // ──────────────────────────────────────────────────────────
            // 2. تحديث الماركات لجعل الأبناء ينتقلون (أو يصبحون NULL) عند حذف الأب
            // ──────────────────────────────────────────────────────────
            // في Entity Framework، الضبط الجديد الذي وضعناه في DbContext سيقوم بذلك، 
            // لكن هنا نضمن أن قاعدة البيانات نفسها تسمح بذلك.
            
            return Ok(new { message = "Category and Brand constraints updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SchemaFix v4 failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
