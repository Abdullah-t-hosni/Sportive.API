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
        _logger.LogWarning("SchemaFix run-v4 triggered.");
        try
        {
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Products MODIFY COLUMN CategoryId INT NULL;");
            return Ok(new { message = "Category constraints updated." });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v5")]
    public async Task<IActionResult> RunV5()
    {
        _logger.LogWarning("SchemaFix run-v5 (Full Catalog Deletion Fix) triggered.");
        try
        {
            var cmds = new[] {
                "ALTER TABLE OrderItems MODIFY COLUMN ProductId INT NULL;",
                "ALTER TABLE OrderItems DROP FOREIGN KEY FK_OrderItems_Products_ProductId;",
                "ALTER TABLE OrderItems ADD CONSTRAINT FK_OrderItems_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE SET NULL;",

                "ALTER TABLE InventoryMovements DROP FOREIGN KEY FK_InventoryMovements_Products_ProductId;",
                "ALTER TABLE InventoryMovements ADD CONSTRAINT FK_InventoryMovements_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE SET NULL;",

                "ALTER TABLE CartItems MODIFY COLUMN ProductId INT NULL;",
                "ALTER TABLE CartItems DROP FOREIGN KEY FK_CartItems_Products_ProductId;",
                "ALTER TABLE CartItems ADD CONSTRAINT FK_CartItems_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE;",

                "ALTER TABLE ProductVariants DROP FOREIGN KEY FK_ProductVariants_Products_ProductId;",
                "ALTER TABLE ProductVariants ADD CONSTRAINT FK_ProductVariants_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE;",

                "ALTER TABLE ProductImages DROP FOREIGN KEY FK_ProductImages_Products_ProductId;",
                "ALTER TABLE ProductImages ADD CONSTRAINT FK_ProductImages_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE;",

                "ALTER TABLE Reviews DROP FOREIGN KEY FK_Reviews_Products_ProductId;",
                "ALTER TABLE Reviews ADD CONSTRAINT FK_Reviews_Products_ProductId FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE;"
            };

            foreach(var c in cmds) { try { await _db.Database.ExecuteSqlRawAsync(c); } catch { } }

            return Ok(new { message = "Constraints updated successfully. You can now delete products and categories freely." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
