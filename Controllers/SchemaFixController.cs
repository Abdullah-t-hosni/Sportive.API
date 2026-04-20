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

            var skipped = new List<string>();
            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    // ALTER TABLE failures are expected when constraints already exist
                    _logger.LogWarning("SchemaFix run-v5 skipped cmd (already applied?): {Error}", ex.Message);
                    skipped.Add(ex.Message[..Math.Min(80, ex.Message.Length)]);
                }
            }

            return Ok(new { message = "Constraints updated.", skipped });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("run-v6")]
    public async Task<IActionResult> RunV6()
    {
        _logger.LogWarning("SchemaFix run-v6 (Orphaned Movement Cleanup) triggered.");
        try
        {
            // تنظيف الحركات التي تشير إلى أشكال منتجات (Variants) تم حذفها
            var orphanedCount = await _db.Database.ExecuteSqlRawAsync(@"
                UPDATE InventoryMovements 
                SET ProductVariantId = NULL 
                WHERE ProductVariantId IS NOT NULL 
                AND ProductVariantId NOT IN (SELECT Id FROM ProductVariants);
            ");

            return Ok(new { 
                message = "Cleaned up orphaned movements successfully.", 
                orphanedCount 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("run-v7")]
    public async Task<IActionResult> RunV7()
    {
        _logger.LogWarning("SchemaFix run-v7 (Emergency FK Fix) triggered.");
        try {
            // 1. تنظيف البيانات أولاً لضمان إمكانية إنشاء الربط
            await _db.Database.ExecuteSqlRawAsync(@"
                UPDATE InventoryMovements SET ProductVariantId = NULL 
                WHERE ProductVariantId IS NOT NULL AND ProductVariantId NOT IN (SELECT Id FROM ProductVariants);");

            // 2. محاولة إضافة الـ FK يدوياً (لكي يجدها الـ EF ويحذفها في الخطوة القادمة من Migration)
            try {
                await _db.Database.ExecuteSqlRawAsync(@"
                    ALTER TABLE InventoryMovements ADD CONSTRAINT FK_InventoryMovements_ProductVariants_ProductVariantId
                    FOREIGN KEY (ProductVariantId) REFERENCES ProductVariants(Id) ON DELETE SET NULL;");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("run-v7 FK already exists or skipped: {Error}", ex.Message[..Math.Min(80, ex.Message.Length)]);
            }

            return Ok(new { message = "Emergency fix applied successfully. Please try 'dotnet ef database update' again." });
        } catch (Exception ex) {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("run-v8")]
    public async Task<IActionResult> RunV8()
    {
        _logger.LogWarning("SchemaFix run-v8 (Product Slugs) triggered.");
        try
        {
            // 1. Add Slug column if not exists
            try { 
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Products ADD COLUMN Slug VARCHAR(255) DEFAULT '' NOT NULL;"); 
            } catch (Exception ex) { _logger.LogInformation("Slug column already exists or error: {Err}", ex.Message); }

            // 2. Generate slugs for all products that have empty slugs
            var products = await _db.Products.Where(p => string.IsNullOrEmpty(p.Slug)).ToListAsync();
            foreach (var p in products)
            {
                var baseSlug = GenerateSlug(p.NameEn ?? p.NameAr);
                p.Slug = baseSlug + "-" + p.Id;
            }
            await _db.SaveChangesAsync();

            return Ok(new { message = "Slugs generated for all products.", count = products.Count });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v9")]
    public async Task<IActionResult> RunV9()
    {
        _logger.LogWarning("SchemaFix run-v9 (Fixed Asset CostCenter) triggered.");
        try
        {
            try { 
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE FixedAssetCategories ADD COLUMN CostCenter INT NULL;"); 
            } catch (Exception ex) { _logger.LogInformation("CostCenter col already in Categories: {Err}", ex.Message); }

            try { 
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE FixedAssets ADD COLUMN CostCenter INT NULL;"); 
            } catch (Exception ex) { _logger.LogInformation("CostCenter col already in FixedAssets: {Err}", ex.Message); }

            return Ok(new { message = "Fixed Asset CostCenter columns added." });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    private string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString().Substring(0, 8);
        var s = name.ToLower().Trim();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\u0600-\u06FF\s-]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "-").Trim('-');
        return s;
    }
}
