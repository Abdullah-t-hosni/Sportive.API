using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

/// <summary>
/// 🔒 محمي بالكامل — Admin فقط
/// يُستخدم لتصحيح مخطط قاعدة البيانات يدوياً عند الحاجة
/// يُفضَّل تشغيل Migrations بدلاً من هذا الـ Controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Maintenance, requireEdit: true)]  // âœ… FIX: ÙƒØ§Ù† Ø¨Ø¯ÙˆÙ† Authorize â€” Ø§Ù„Ø¢Ù† Admin ÙÙ‚Ø·
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

            // 2. Ù…Ø­Ø§ÙˆÙ„Ø© Ø¥Ø¶Ø§ÙØ© Ø§Ù„Ù€ FK ÙŠØ¯ÙˆÙŠØ§Ù‹ (Ù„ÙƒÙŠ ÙŠØ¬Ø¯Ù‡Ø§ Ø§Ù„Ù€ EF ÙˆÙŠØ­Ø°ÙÙ‡Ø§ ÙÙŠ Ø§Ù„Ø®Ø·ÙˆØ© Ø§Ù„Ù‚Ø§Ø¯Ù…Ø© Ù…Ù† Migration)
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

    [HttpGet("run-v10")]
    public async Task<IActionResult> RunV10()
    {
        _logger.LogWarning("SchemaFix run-v10 (Inventory Audits Tables) triggered.");
        try
        {
            var cmds = new[] {
                @"CREATE TABLE IF NOT EXISTS InventoryAudits (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    Title VARCHAR(255) NOT NULL,
                    AuditDate DATETIME NOT NULL,
                    Description TEXT,
                    CreatedByUserId VARCHAR(255),
                    Status INT NOT NULL DEFAULT 1,
                    TotalExpectedValue DECIMAL(18,2) NOT NULL DEFAULT 0,
                    TotalActualValue DECIMAL(18,2) NOT NULL DEFAULT 0,
                    JournalEntryId INT,
                    CreatedAt DATETIME NOT NULL,
                    UpdatedAt DATETIME NULL
                );",
                @"CREATE TABLE IF NOT EXISTS InventoryAuditItems (
                    Id INT AUTO_INCREMENT PRIMARY KEY,
                    InventoryAuditId INT NOT NULL,
                    ProductId INT,
                    ProductVariantId INT,
                    ExpectedQuantity INT NOT NULL DEFAULT 0,
                    ActualQuantity INT NOT NULL DEFAULT 0,
                    UnitCost DECIMAL(18,2) NOT NULL DEFAULT 0,
                    Note TEXT,
                    CreatedAt DATETIME NOT NULL,
                    UpdatedAt DATETIME NULL,
                    FOREIGN KEY (InventoryAuditId) REFERENCES InventoryAudits(Id) ON DELETE CASCADE
                );",
                // Ensure InventoryMovements has RemainingStock (it was added recently in logic but maybe not in DB)
                @"ALTER TABLE InventoryMovements ADD COLUMN IF NOT EXISTS RemainingStock INT NOT NULL DEFAULT 0;"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex) { _logger.LogWarning("Cmd failed: {Err}", ex.Message); }
            }

            return Ok(new { message = "Inventory Audit tables checked/created." });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v11")]
    public async Task<IActionResult> RunV11()
    {
        _logger.LogWarning("SchemaFix run-v11 (Category Hierarchy Type Fix) triggered.");
        try
        {
            var allCats = await _db.Categories.ToListAsync();
            int fixedCount = 0;

            // 1. Identify and Fix Roots by name or ID
            foreach (var cat in allCats)
            {
                CategoryType? targetType = null;
                bool shouldBeRoot = false;

                if (cat.Id == 1 || cat.NameAr == "رجالي" || cat.NameEn == "Men") { targetType = CategoryType.Men; shouldBeRoot = true; }
                else if (cat.Id == 2 || cat.NameAr == "حريمي" || cat.NameEn == "Women") { targetType = CategoryType.Women; shouldBeRoot = true; }
                else if (cat.Id == 3 || cat.NameAr == "أطفال" || cat.NameEn == "Kids") { targetType = CategoryType.Kids; shouldBeRoot = true; }
                else if (cat.Id == 4 || cat.NameAr == "أدوات ومعدات" || cat.NameEn == "Equipment") { targetType = CategoryType.Equipment; shouldBeRoot = true; }
                else if (cat.Id == 5 || cat.NameAr == "أحذية" || cat.NameEn == "Shoes") { targetType = CategoryType.Shoes; shouldBeRoot = true; }

                if (shouldBeRoot)
                {
                    if (cat.ParentId != null) { cat.ParentId = null; fixedCount++; }
                    if (targetType.HasValue && cat.Type != targetType.Value) { cat.Type = targetType.Value; fixedCount++; }
                }
            }

            // 2. Synchronize descendants with their roots
            var roots = allCats.Where(c => c.ParentId == null).ToList();
            foreach (var root in roots)
            {
                fixedCount += FixDescendantsInternal(root.Id, root.Type, allCats);
            }

            if (fixedCount > 0) await _db.SaveChangesAsync();
            return Ok(new { message = "Category hierarchy types synchronized.", fixedCount });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    private int FixDescendantsInternal(int parentId, CategoryType correctType, List<Category> all)
    {
        int count = 0;
        var children = all.Where(c => c.ParentId == parentId).ToList();
        foreach (var child in children)
        {
            if (child.Type != correctType)
            {
                child.Type = correctType;
                count++;
            }
            count += FixDescendantsInternal(child.Id, correctType, all);
        }
        return count;
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

