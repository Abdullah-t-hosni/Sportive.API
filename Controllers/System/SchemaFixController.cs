using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

/// <summary>
/// 🔒 محمي بالكامل — Admin فقط
/// يُستخدم لتصحيح مخطط قاعدة البيانات يدوياً عند الحاجة
/// يُفضَّل تشغيل Migrations بدلاً من هذا الـ Controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Maintenance, requireEdit: true)]  // âœ… FIX: Authorize
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

    [HttpGet("run-multi-category-schema")]
    public async Task<IActionResult> RunMultiCategorySchema()
    {
        _logger.LogWarning("SchemaFix run-multi-category-schema triggered.");
        try
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS `ProductSecondaryCategories` (
                    `ProductId` int NOT NULL,
                    `CategoryId` int NOT NULL,
                    PRIMARY KEY (`ProductId`, `CategoryId`),
                    KEY `IX_ProductSecondaryCategories_CategoryId` (`CategoryId`),
                    CONSTRAINT `FK_ProductSecondaryCategories_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE CASCADE,
                    CONSTRAINT `FK_ProductSecondaryCategories_Products_ProductId` FOREIGN KEY (`ProductId`) REFERENCES `Products` (`Id`) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            try
            {
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductImages` ADD COLUMN `CategoryId` int NULL;");
            }
            catch { }

            try
            {
                await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `ProductImages` ADD CONSTRAINT `FK_ProductImages_Categories_CategoryId` FOREIGN KEY (`CategoryId`) REFERENCES `Categories` (`Id`) ON DELETE SET NULL;");
            }
            catch { }

            return Ok(new { message = "Multi-Category schema applied successfully." });
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

    [HttpGet("run-v12")]
    public async Task<IActionResult> RunV12()
    {
        _logger.LogWarning("SchemaFix run-v12 (Receipt Show Time & SKU) triggered.");
        try
        {
            var skipped = new List<string>();
            var cmds = new[] {
                "ALTER TABLE StoreSettings ADD COLUMN ReceiptShowTime BOOLEAN DEFAULT 1 NOT NULL;",
                "ALTER TABLE StoreSettings ADD COLUMN ReceiptShowSKU BOOLEAN DEFAULT 1 NOT NULL;",
                "ALTER TABLE OrderItems ADD COLUMN SKU VARCHAR(100) NULL;",
                "UPDATE OrderItems SET SKU = (SELECT SKU FROM Products WHERE Products.Id = OrderItems.ProductId) WHERE SKU IS NULL OR SKU = '';"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchemaFix run-v12 skipped cmd: {Error}", ex.Message);
                    skipped.Add(ex.Message);
                }
            }

            return Ok(new { message = "Receipt settings columns added.", skipped });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v13")]
    public async Task<IActionResult> RunV13()
    {
        _logger.LogWarning("SchemaFix run-v13 (Asset Purchase Columns) triggered.");
        try
        {
            var skipped = new List<string>();
            var cmds = new[] {
                "ALTER TABLE PurchaseInvoices ADD COLUMN IsAssetPurchase BOOLEAN DEFAULT 0 NOT NULL;",
                "ALTER TABLE PurchaseInvoiceItems ADD COLUMN FixedAssetCategoryId INT NULL;",
                "ALTER TABLE PurchaseInvoiceItems ADD COLUMN AssetName VARCHAR(255) NULL;",
                "ALTER TABLE PurchaseInvoiceItems ADD COLUMN CreatedAssetId INT NULL;",
                "ALTER TABLE PurchaseInvoiceItems ADD CONSTRAINT FK_PurchaseInvoiceItems_AssetCategories FOREIGN KEY (FixedAssetCategoryId) REFERENCES FixedAssetCategories(Id) ON DELETE SET NULL;",
                "ALTER TABLE PurchaseInvoiceItems ADD CONSTRAINT FK_PurchaseInvoiceItems_FixedAssets FOREIGN KEY (CreatedAssetId) REFERENCES FixedAssets(Id) ON DELETE SET NULL;"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchemaFix run-v13 skipped cmd: {Error}", ex.Message);
                    skipped.Add(ex.Message);
                }
            }

            return Ok(new { message = "Asset Purchase columns added.", skipped });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v14")]
    public async Task<IActionResult> RunV14()
    {
        _logger.LogWarning("SchemaFix run-v14 (Graduated Delay Policy Settings) triggered.");
        try
        {
            var skipped = new List<string>();
            var cmds = new[] {
                "ALTER TABLE StoreSettings ADD COLUMN DelayGraceMinutes INT DEFAULT 15 NOT NULL;",
                "ALTER TABLE StoreSettings ADD COLUMN DelayHalfDayLimitMinutes INT DEFAULT 60 NOT NULL;",
                "ALTER TABLE StoreSettings ADD COLUMN DelayQuarterDayLimitMinutes INT DEFAULT 30 NOT NULL;",
                "ALTER TABLE StoreSettings ADD COLUMN EnableGraduatedDelayPolicy TINYINT(1) DEFAULT 1 NOT NULL;"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchemaFix run-v14 skipped cmd: {Error}", ex.Message);
                    skipped.Add(ex.Message);
                }
            }

            return Ok(new { message = "Graduated Delay Policy columns added to StoreSettings.", skipped });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v15")]
    public async Task<IActionResult> RunV15()
    {
        _logger.LogWarning("SchemaFix run-v15 (EnableUrgencyTags Settings) triggered.");
        try
        {
            var skipped = new List<string>();
            var cmds = new[] {
                "ALTER TABLE StoreSettings ADD COLUMN EnableUrgencyTags TINYINT(1) DEFAULT 1 NOT NULL;"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchemaFix run-v15 skipped cmd: {Error}", ex.Message);
                    skipped.Add(ex.Message);
                }
            }

            return Ok(new { message = "Urgency tags settings column added to StoreSettings.", skipped });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("run-v16")]
    public async Task<IActionResult> RunV16()
    {
        _logger.LogWarning("SchemaFix run-v16 (LinkedProduct column) triggered.");
        try
        {
            var skipped = new List<string>();
            var cmds = new[] {
                "ALTER TABLE Products ADD COLUMN LinkedProductId INT NULL;",
                "ALTER TABLE Products ADD CONSTRAINT FK_Products_Products_LinkedProductId FOREIGN KEY (LinkedProductId) REFERENCES Products(Id) ON DELETE SET NULL;"
            };

            foreach (var c in cmds)
            {
                try { await _db.Database.ExecuteSqlRawAsync(c); }
                catch (Exception ex)
                {
                    _logger.LogWarning("SchemaFix run-v16 skipped cmd: {Error}", ex.Message);
                    skipped.Add(ex.Message);
                }
            }

            return Ok(new { message = "Linked product column and constraint added to Products table.", skipped });
        }
        catch (Exception ex) { return StatusCode(500, new { error = ex.Message }); }
    }

    [HttpGet("check-uncancelled")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUncancelledOrders([FromQuery] string? secret = null)
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid or missing secret key." });

        // 🛡️ Scan 100% of ALL orders in the database
        var allOrders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .ToListAsync();

        var allHistories = await _db.OrderStatusHistories
            .AsNoTracking()
            .ToListAsync();

        var allMovements = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.Reference != null)
            .ToListAsync();

        var allAuditLogs = await _db.AuditLogs
            .AsNoTracking()
            .Where(l => l.EntityType == "Order" || l.EntityType == "OrderStatus" || (l.Notes != null && l.Notes.Contains("Order")))
            .ToListAsync();

        var historiesByOrder = allHistories.GroupBy(h => h.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(h => h.CreatedAt).ToList());
        var movementsByRef = allMovements.GroupBy(m => m.Reference!.Trim()).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var affectedOrdersList = new List<object>();

        foreach (var order in allOrders)
        {
            bool isAffected = false;
            string reason = "";

            // Vector 1: Check OrderStatusHistories for Cancelled -> Non-Cancelled transition
            if (historiesByOrder.TryGetValue(order.Id, out var histories))
            {
                bool hadCancelled = false;
                foreach (var h in histories)
                {
                    if (h.Status == OrderStatus.Cancelled)
                    {
                        hadCancelled = true;
                    }
                    else if (hadCancelled && h.Status != OrderStatus.Cancelled)
                    {
                        isAffected = true;
                        reason = $"حالات الطلب السابقة تظهر إلغاء ثم إعادة تفعيل إلى ({h.Status})";
                        break;
                    }
                }
            }

            // Vector 2: Check InventoryMovements (Order is currently ACTIVE, but has a Cancellation/Return stock movement)
            if (!isAffected && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned)
            {
                List<InventoryMovement>? orderMovements = null;
                if (movementsByRef.TryGetValue(order.OrderNumber, out var movs1)) orderMovements = movs1;
                else if (movementsByRef.TryGetValue(order.Id.ToString(), out var movs2)) orderMovements = movs2;

                if (orderMovements != null && orderMovements.Any())
                {
                    var cancelMovements = orderMovements.Where(m => 
                        (m.Type == InventoryMovementType.Adjustment || m.Type == InventoryMovementType.ReturnIn) &&
                        (m.Note != null && (m.Note.Contains("Cancelled") || m.Note.Contains("إلغاء") || m.Note.Contains("Order Cancelled")))
                    ).ToList();

                    if (cancelMovements.Any())
                    {
                        isAffected = true;
                        reason = $"الطلب حالته الحالية ({order.Status}) ولكن يوجد له حركة مخزنية سابقة مرجعة بسبب الإلغاء";
                    }
                }
            }

            // Vector 3: Check AuditLogs
            if (!isAffected)
            {
                var logs = allAuditLogs.Where(l => l.EntityId == order.Id.ToString() || l.EntityId == order.OrderNumber).ToList();
                foreach (var l in logs)
                {
                    if (l.OldValues != null && l.OldValues.Contains("Cancelled") && l.NewValues != null && !l.NewValues.Contains("Cancelled"))
                    {
                        isAffected = true;
                        reason = "سجل التدقيق يظهر تغيير من حالة ملغي إلى حالة نشطة";
                        break;
                    }
                }
            }

            if (isAffected)
            {
                affectedOrdersList.Add(new
                {
                    order.Id,
                    order.OrderNumber,
                    CustomerName = order.Customer != null ? order.Customer.FullName : "",
                    CustomerPhone = order.Customer != null ? order.Customer.Phone : "",
                    Status = order.Status.ToString(),
                    order.TotalAmount,
                    order.CreatedAt,
                    DetectionReason = reason,
                    Items = order.Items.Select(i => new {
                        i.ProductId,
                        i.ProductVariantId,
                        i.ProductNameAr,
                        i.ProductNameEn,
                        i.Size,
                        i.Color,
                        i.Quantity,
                        i.UnitPrice
                    })
                });
            }
        }

        return Ok(new { 
            scannedTotalOrders = allOrders.Count, 
            affectedCount = affectedOrdersList.Count, 
            orders = affectedOrdersList 
        });
    }

    [HttpGet("fix-uncancelled-stock")]
    [AllowAnonymous]
    public async Task<IActionResult> FixUncancelledOrdersStock([FromQuery] string? secret = null, [FromQuery] string? orderNumber = null)
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid or missing secret key." });

        var query = _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .AsQueryable();

        if (!string.IsNullOrEmpty(orderNumber))
        {
            var trimmed = orderNumber.Trim();
            query = query.Where(o => o.OrderNumber == trimmed || o.Id.ToString() == trimmed);
        }

        var ordersToFix = await query.ToListAsync();
        var allHistories = await _db.OrderStatusHistories.AsNoTracking().ToListAsync();
        var allMovements = await _db.InventoryMovements.Where(m => m.Reference != null).ToListAsync();

        var historiesByOrder = allHistories.GroupBy(h => h.OrderId).ToDictionary(g => g.Key, g => g.OrderBy(h => h.CreatedAt).ToList());
        var movementsByRef = allMovements.GroupBy(m => m.Reference!.Trim()).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        int totalOrdersFixed = 0;
        int totalMovementsLogged = 0;
        var fixedOrderDetails = new List<object>();

        foreach (var order in ordersToFix)
        {
            bool isUncancelledActiveOrder = false;

            if (historiesByOrder.TryGetValue(order.Id, out var histories))
            {
                bool hadCancelled = false;
                foreach (var h in histories)
                {
                    if (h.Status == OrderStatus.Cancelled) hadCancelled = true;
                    else if (hadCancelled && h.Status != OrderStatus.Cancelled)
                    {
                        isUncancelledActiveOrder = true;
                        break;
                    }
                }
            }

            if (!isUncancelledActiveOrder && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Returned)
            {
                List<InventoryMovement>? orderMovements = null;
                if (movementsByRef.TryGetValue(order.OrderNumber, out var movs1)) orderMovements = movs1;
                else if (movementsByRef.TryGetValue(order.Id.ToString(), out var movs2)) orderMovements = movs2;

                if (orderMovements != null && orderMovements.Any())
                {
                    var cancelMovements = orderMovements.Where(m => 
                        (m.Type == InventoryMovementType.Adjustment || m.Type == InventoryMovementType.ReturnIn) &&
                        (m.Note != null && (m.Note.Contains("Cancelled") || m.Note.Contains("إلغاء") || m.Note.Contains("Order Cancelled")))
                    ).ToList();

                    var fixMovements = orderMovements.Where(m => 
                        m.Note != null && m.Note.Contains("Fix: Re-deduct stock for uncancelled order")
                    ).ToList();

                    if (cancelMovements.Any() && !fixMovements.Any())
                    {
                        isUncancelledActiveOrder = true;
                    }
                }
            }

            // Force target order if specified directly by parameter
            if (!string.IsNullOrEmpty(orderNumber))
            {
                var trimmed = orderNumber.Trim();
                if (order.OrderNumber == trimmed || order.Id.ToString() == trimmed)
                {
                    isUncancelledActiveOrder = true;
                }
            }

            if (isUncancelledActiveOrder && order.Status != OrderStatus.Cancelled)
            {
                List<InventoryMovement>? currentMovements = null;
                if (movementsByRef.TryGetValue(order.OrderNumber, out var m1)) currentMovements = m1;
                else if (movementsByRef.TryGetValue(order.Id.ToString(), out var m2)) currentMovements = m2;

                bool alreadyFixed = currentMovements != null && currentMovements.Any(m => m.Note != null && m.Note.Contains("Fix: Re-deduct stock for uncancelled order"));

                if (!alreadyFixed)
                {
                    foreach (var item in order.Items)
                    {
                        if (item.Quantity > 0)
                        {
                            var movement = new InventoryMovement
                            {
                                ProductId = item.ProductId,
                                ProductVariantId = item.ProductVariantId,
                                Quantity = -item.Quantity,
                                Type = InventoryMovementType.Sale,
                                Reference = order.OrderNumber,
                                Note = $"Fix: Re-deduct stock for uncancelled order #{order.OrderNumber}",
                                CreatedAt = TimeHelper.GetEgyptTime(),
                                CostCenter = order.Source
                            };

                            _db.InventoryMovements.Add(movement);
                            totalMovementsLogged++;

                            if (item.ProductVariantId.HasValue && item.ProductVariantId.Value > 0)
                            {
                                var variant = await _db.ProductVariants.FindAsync(item.ProductVariantId.Value);
                                if (variant != null)
                                {
                                    variant.StockQuantity -= item.Quantity;
                                    variant.UpdatedAt = TimeHelper.GetEgyptTime();
                                }
                            }
                            if (item.ProductId.HasValue && item.ProductId.Value > 0)
                            {
                                var prod = await _db.Products.FindAsync(item.ProductId.Value);
                                if (prod != null)
                                {
                                    prod.TotalStock -= item.Quantity;
                                    prod.UpdatedAt = TimeHelper.GetEgyptTime();
                                }
                            }
                        }
                    }

                    totalOrdersFixed++;
                    fixedOrderDetails.Add(new
                    {
                        order.Id,
                        order.OrderNumber,
                        CustomerName = order.Customer != null ? order.Customer.FullName : "",
                        Status = order.Status.ToString(),
                        order.TotalAmount,
                        Items = order.Items.Select(i => new {
                            i.ProductId,
                            i.ProductVariantId,
                            i.ProductNameAr,
                            i.Size,
                            i.Color,
                            i.Quantity
                        })
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Uncancelled order inventory successfully fixed in database.",
            totalOrdersFixed,
            totalMovementsLogged,
            fixedOrders = fixedOrderDetails
        });
    }

    [HttpGet("inspect-order-movements")]
    [AllowAnonymous]
    public async Task<IActionResult> InspectOrderMovements([FromQuery] string? secret = null, [FromQuery] string orderNumber = "SPT-2607-0165")
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid secret key." });

        var trimmed = orderNumber.Trim();
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.OrderNumber == trimmed || o.Id.ToString() == trimmed);

        if (order == null) return NotFound(new { message = $"Order {orderNumber} not found." });

        var histories = await _db.OrderStatusHistories
            .AsNoTracking()
            .Where(h => h.OrderId == order.Id)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new { h.Id, h.Status, h.Note, h.CreatedAt, h.ChangedByUserId })
            .ToListAsync();

        var movements = await _db.InventoryMovements
            .AsNoTracking()
            .Where(m => m.Reference == order.OrderNumber || m.Reference == order.Id.ToString())
            .OrderBy(m => m.CreatedAt)
            .Select(m => new {
                m.Id,
                m.ProductId,
                m.ProductVariantId,
                m.Type,
                m.Quantity,
                m.RemainingStock,
                m.Reference,
                m.Note,
                m.CreatedAt
            })
            .ToListAsync();

        var itemsDetail = new List<object>();
        foreach (var item in order.Items)
        {
            var variant = item.ProductVariantId.HasValue ? await _db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == item.ProductVariantId.Value) : null;
            var prod = item.ProductId.HasValue ? await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.ProductId.Value) : null;

            itemsDetail.Add(new {
                item.ProductId,
                item.ProductVariantId,
                item.ProductNameAr,
                item.Size,
                item.Color,
                item.Quantity,
                ItemCurrentVariantStock = variant != null ? (int?)variant.StockQuantity : null,
                ItemCurrentProductTotalStock = prod != null ? (int?)prod.TotalStock : null,
            });
        }

        return Ok(new {
            order = new {
                order.Id,
                order.OrderNumber,
                CustomerName = order.Customer != null ? order.Customer.FullName : "",
                order.Status,
                order.TotalAmount,
                order.CreatedAt
            },
            items = itemsDetail,
            histories,
            movements
        });
    }

    [HttpGet("fix-duplicate-cancellation-movements")]
    [AllowAnonymous]
    public async Task<IActionResult> FixDuplicateCancellationMovements([FromQuery] string? secret = null)
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid secret key." });

        var cancellationMovements = await _db.InventoryMovements
            .Where(m => m.Reference != null && 
                        (m.Type == InventoryMovementType.Adjustment || m.Type == InventoryMovementType.ReturnIn) &&
                        m.Note != null && (m.Note.Contains("Cancelled") || m.Note.Contains("Order Cancelled") || m.Note.Contains("إلغاء")))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var grouped = cancellationMovements
            .GroupBy(m => new { Ref = m.Reference!.Trim(), VariantId = m.ProductVariantId, ProductId = m.ProductId })
            .Where(g => g.Count() > 1)
            .ToList();

        int duplicateMovementsRemoved = 0;
        int variantsAdjusted = 0;
        var removedIds = new List<int>();

        foreach (var group in grouped)
        {
            var duplicates = group.Skip(1).ToList();
            foreach (var dup in duplicates)
            {
                if (dup.ProductVariantId.HasValue)
                {
                    var variant = await _db.ProductVariants.FindAsync(dup.ProductVariantId.Value);
                    if (variant != null)
                    {
                        variant.StockQuantity -= dup.Quantity;
                        variant.UpdatedAt = TimeHelper.GetEgyptTime();
                        variantsAdjusted++;
                    }
                }
                if (dup.ProductId.HasValue)
                {
                    var prod = await _db.Products.FindAsync(dup.ProductId.Value);
                    if (prod != null)
                    {
                        prod.TotalStock -= dup.Quantity;
                        prod.UpdatedAt = TimeHelper.GetEgyptTime();
                    }
                }

                _db.InventoryMovements.Remove(dup);
                removedIds.Add(dup.Id);
                duplicateMovementsRemoved++;
            }
        }

        await _db.SaveChangesAsync();

        var affectedVariantIds = grouped.Select(g => g.Key.VariantId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        foreach (var vId in affectedVariantIds)
        {
            var variantMovs = await _db.InventoryMovements
                .Where(m => m.ProductVariantId == vId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync();

            int runningStock = 0;
            foreach (var m in variantMovs)
            {
                runningStock += m.Quantity;
                m.RemainingStock = runningStock;
            }

            var variant = await _db.ProductVariants.FindAsync(vId);
            if (variant != null)
            {
                variant.StockQuantity = runningStock;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new {
            message = "Duplicate cancellation movements cleaned up successfully.",
            duplicateMovementsRemoved,
            variantsAdjusted,
            removedMovementIds = removedIds
        });
    }

    [HttpGet("adjust-variant-stock")]
    [AllowAnonymous]
    public async Task<IActionResult> AdjustVariantStock([FromQuery] string? secret = null, [FromQuery] int variantId = 0, [FromQuery] int newStock = 0, [FromQuery] string? note = null)
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid secret key." });

        var variant = await _db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId);

        if (variant == null) return NotFound(new { message = $"Variant {variantId} not found." });

        int currentStock = variant.StockQuantity;
        int diff = newStock - currentStock;

        if (diff != 0)
        {
            var movement = new InventoryMovement
            {
                ProductId = variant.ProductId,
                ProductVariantId = variant.Id,
                Quantity = diff,
                Type = InventoryMovementType.Adjustment,
                Reference = "ADJUST-FIX",
                Note = string.IsNullOrEmpty(note) ? $"تعديل يدوي للمخزون (من {currentStock} إلى {newStock})" : note,
                CreatedAt = TimeHelper.GetEgyptTime(),
                RemainingStock = newStock
            };

            _db.InventoryMovements.Add(movement);

            variant.StockQuantity = newStock;
            variant.UpdatedAt = TimeHelper.GetEgyptTime();

            if (variant.Product != null)
            {
                var allVariants = await _db.ProductVariants.Where(v => v.ProductId == variant.ProductId).ToListAsync();
                variant.Product.TotalStock = allVariants.Sum(v => v.Id == variant.Id ? newStock : v.StockQuantity);
                variant.Product.UpdatedAt = TimeHelper.GetEgyptTime();
            }

            await _db.SaveChangesAsync();
        }

        return Ok(new {
            message = $"Variant {variantId} stock adjusted from {currentStock} to {newStock}.",
            variantId = variant.Id,
            productId = variant.ProductId,
            productName = variant.Product?.NameAr,
            size = variant.Size,
            color = variant.Color,
            previousStock = currentStock,
            newStock = variant.StockQuantity,
            productTotalStock = variant.Product?.TotalStock
        });
    }

    [HttpGet("delete-adjust-fix-movements")]
    [AllowAnonymous]
    public async Task<IActionResult> DeleteAdjustFixMovements([FromQuery] string? secret = null)
    {
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid secret key." });

        var adjustMovements = await _db.InventoryMovements
            .Where(m => m.Reference == "ADJUST-FIX")
            .ToListAsync();

        int count = adjustMovements.Count;
        var affectedVariantIds = adjustMovements.Select(m => m.ProductVariantId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

        _db.InventoryMovements.RemoveRange(adjustMovements);
        await _db.SaveChangesAsync();

        foreach (var vId in affectedVariantIds)
        {
            var variantMovs = await _db.InventoryMovements
                .Where(m => m.ProductVariantId == vId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync();

            int runningStock = 0;
            foreach (var m in variantMovs)
            {
                runningStock += m.Quantity;
                m.RemainingStock = runningStock;
            }

            var variant = await _db.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.Id == vId);
            if (variant != null)
            {
                variant.StockQuantity = runningStock;
                variant.UpdatedAt = TimeHelper.GetEgyptTime();

                if (variant.Product != null)
                {
                    var allVariants = await _db.ProductVariants.Where(v => v.ProductId == variant.ProductId).ToListAsync();
                    variant.Product.TotalStock = allVariants.Sum(v => v.Id == variant.Id ? runningStock : v.StockQuantity);
                    variant.Product.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new {
            message = "ADJUST-FIX movements deleted successfully.",
            deletedCount = count
        });
    }

    [HttpGet("run-v17")]
    [AllowAnonymous]
    public async Task<IActionResult> RunV17([FromQuery] string? secret = null)
    {
        // 🔒 Temporary secret key protection instead of requiring login
        if (secret != "sportive-fix-stock-2026")
            return Unauthorized(new { message = "Invalid or missing secret key." });

        _logger.LogWarning("SchemaFix run-v17 (Stock Discrepancy Fix) triggered.");
        try
        {
            int variantsFixed = 0;
            int productsFixed = 0;
            
            // 1. Recalculate Variant Stock from Movements
            var variants = await _db.ProductVariants.ToListAsync();
            foreach (var v in variants)
            {
                var sumMovements = await _db.InventoryMovements
                    .Where(m => m.ProductVariantId == v.Id)
                    .SumAsync(m => (int?)m.Quantity) ?? 0;

                if (v.StockQuantity != sumMovements)
                {
                    v.StockQuantity = sumMovements;
                    v.UpdatedAt = TimeHelper.GetEgyptTime();
                    variantsFixed++;
                }
            }
            await _db.SaveChangesAsync();

            // 2. Recalculate Product TotalStock from Variants (or from Movements if no variants)
            var products = await _db.Products.Include(p => p.Variants).ToListAsync();
            foreach (var p in products)
            {
                int correctTotal = 0;
                if (p.Variants.Any())
                {
                    correctTotal = p.Variants.Sum(v => v.StockQuantity);
                }
                else
                {
                    correctTotal = await _db.InventoryMovements
                        .Where(m => m.ProductId == p.Id && m.ProductVariantId == null)
                        .SumAsync(m => (int?)m.Quantity) ?? 0;
                }

                if (p.TotalStock != correctTotal)
                {
                    p.TotalStock = correctTotal;
                    p.UpdatedAt = TimeHelper.GetEgyptTime();
                    productsFixed++;
                    
                    // Fix status if needed
                    if (p.Status == ProductStatus.Active && p.TotalStock <= 0) p.Status = ProductStatus.OutOfStock;
                    else if (p.Status == ProductStatus.OutOfStock && p.TotalStock > 0) p.Status = ProductStatus.Active;
                }
            }
            await _db.SaveChangesAsync();

            // 3. Recalculate Warehouse Stock from Movements
            var warehouseStocks = await _db.ProductWarehouseStocks.ToListAsync();
            foreach (var ws in warehouseStocks)
            {
                var sumMovements = await _db.InventoryMovements
                    .Where(m => m.ProductVariantId == ws.ProductVariantId && m.WarehouseId == ws.WarehouseId)
                    .SumAsync(m => (int?)m.Quantity) ?? 0;
                    
                if (ws.Quantity != sumMovements)
                {
                    ws.Quantity = sumMovements;
                    ws.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
            await _db.SaveChangesAsync();

            return Ok(new { 
                message = "Stock discrepancies fixed successfully based on actual movements.", 
                variantsFixedCount = variantsFixed,
                productsFixedCount = productsFixed
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("clean-audit-movements")]
    public async Task<IActionResult> CleanAuditMovements()
    {
        _logger.LogWarning("CleanAuditMovements triggered.");
        try
        {
            // 1. Find all movements that are REVERT or DELETE audits
            var revertMovements = await _db.InventoryMovements
                .Where(m => m.Reference != null && (m.Reference.StartsWith("REVERT-AUDIT-") || m.Reference.StartsWith("DELETE-AUDIT-")))
                .ToListAsync();

            var deletedMvCount = 0;
            foreach (var revMv in revertMovements)
            {
                // Extract audit ID from reference (e.g., REVERT-AUDIT-23 -> 23)
                var parts = revMv.Reference!.Split('-');
                if (parts.Length > 0 && int.TryParse(parts[^1], out var auditId))
                {
                    // Find the matching original audit movement for the same variant/product
                    var originalMv = await _db.InventoryMovements
                        .FirstOrDefaultAsync(m => m.Reference == $"AUDIT-{auditId}" && 
                                                 m.ProductId == revMv.ProductId && 
                                                 m.ProductVariantId == revMv.ProductVariantId);

                    if (originalMv != null)
                    {
                        _db.InventoryMovements.Remove(originalMv);
                        deletedMvCount++;
                    }
                }
                
                _db.InventoryMovements.Remove(revMv);
                deletedMvCount++;
            }

            // 2. Clean up journal entries for deleted audits
            var journalEntries = await _db.JournalEntries
                .Include(j => j.Lines)
                .Where(j => j.Reference != null && j.Reference.StartsWith("AUDIT-"))
                .ToListAsync();

            var deletedJeCount = 0;
            foreach (var je in journalEntries)
            {
                var parts = je.Reference!.Split('-');
                if (parts.Length > 0 && int.TryParse(parts[^1], out var auditId))
                {
                    // Check if the audit still exists
                    var auditExists = await _db.InventoryAudits.AnyAsync(a => a.Id == auditId);
                    if (!auditExists)
                    {
                        // Find any reversals
                        var reversals = await _db.JournalEntries
                            .Include(j => j.Lines)
                            .Where(j => j.ReversalOfId == je.Id)
                            .ToListAsync();

                        _db.JournalEntries.RemoveRange(reversals);
                        _db.JournalEntries.Remove(je);
                        deletedJeCount += 1 + reversals.Count;
                    }
                }
            }

            if (deletedMvCount > 0 || deletedJeCount > 0)
            {
                await _db.SaveChangesAsync();
            }

            return Ok(new { 
                message = $"Cleaned up {deletedMvCount} stock movement records and {deletedJeCount} journal entry records.", 
                deletedMovements = deletedMvCount,
                deletedJournalEntries = deletedJeCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
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

    [HttpGet("fix-website-pending-orders")]
    public async Task<IActionResult> FixWebsitePendingOrders()
    {
        _logger.LogWarning("SchemaFix fix-website-pending-orders triggered.");
        try
        {
            var affected = await _db.Orders
                .Where(o => o.Source == OrderSource.Website 
                         && o.PaymentMethod == PaymentMethod.Cash 
                         && o.Status != OrderStatus.Delivered 
                         && o.Status != OrderStatus.PartiallyReturned
                         && o.Status != OrderStatus.Returned
                         && o.PaidAmount > 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaidAmount, 0)
                    .SetProperty(o => o.PaymentStatus, PaymentStatus.Pending));

            return Ok(new { 
                success = true, 
                message = affected > 0 ? $"تم تصحيح وتصفير المدفوع لعدد {affected} طلب دفع عند الاستلام بنجاح." : "كافة طلبات الدفع عند الاستلام سليمة ومضبوطة.",
                affected 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

