using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Claims;
using System.Data;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using Sportive.API.Extensions;
using ClosedXML.Excel;

namespace Sportive.API.Controllers;
[ApiController]
[Route("api/purchaseinvoices")]
[RequirePermission(ModuleKeys.PurchasesMain + "," + ModuleKeys.PurchaseReturns + "," + ModuleKeys.SupplierVouchers)]
public class PurchaseReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProductService _productService;
    private readonly IInventoryService _inventory;
    private readonly ILogger<PurchaseReturnsController> _logger;
    private readonly SequenceService _seq;
    private readonly IPdfService _pdf;
    private readonly ITranslator _t;

    public PurchaseReturnsController(AppDbContext db, IServiceScopeFactory scopeFactory, IProductService productService, IInventoryService inventory, ILogger<PurchaseReturnsController> logger, SequenceService seq, IPdfService pdf, ITranslator t)
    {
        _t = t;
        _db             = db;
        _scopeFactory   = scopeFactory;
        _productService = productService;
        _inventory      = inventory;
        _logger         = logger;
        _seq            = seq;
        _pdf            = pdf;
    }

    private async Task<List<ProductUnit>> GetUnitsListAsync() => await _db.ProductUnits.AsNoTracking().ToListAsync();
    private decimal GetMultiplier(List<ProductUnit> units, string? unitStr)
    {
        if (string.IsNullOrWhiteSpace(unitStr)) return 1M;
        return units.FirstOrDefault(u => u.Symbol == unitStr || u.NameAr == unitStr || u.NameEn == unitStr)?.Multiplier ?? 1M;
    }

    [HttpGet("returns")]
    public async Task<IActionResult> GetReturns(
        [FromQuery] int? supplierId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] OrderSource? costCenter = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PurchaseReturns
            .AsNoTracking()
            .Include(r => r.Supplier)
            .Include(r => r.Invoice)
            .Include(r => r.Warehouse)
            .AsQueryable();

        if (costCenter.HasValue) q = q.Where(r => r.CostCenter == costCenter.Value);

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? isolatedBranchId = canViewAll ? branchId : User.GetBranchId();
        int? isolatedWarehouseId = canViewAll ? warehouseId : User.GetWarehouseId();

        // Warehouse is directly on PurchaseReturn
        if (isolatedWarehouseId.HasValue) 
        {
            q = q.Where(r => r.WarehouseId == isolatedWarehouseId.Value);
        }
        if (isolatedBranchId.HasValue)
        {
            // Note: If PurchaseReturns doesn't have BranchId, we filter via WarehouseId or Invoice's BranchId if available.
            // Wait, does PurchaseReturn have BranchId? I added it earlier!
            q = q.Where(r => r.BranchId == isolatedBranchId.Value);
        }

        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(r => r.ReturnDate >= fromDate.Value.Date);
        if (toDate.HasValue)     q = q.Where(r => r.ReturnDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        
        if (!string.IsNullOrEmpty(search))
            q = q.Where(r => r.ReturnNumber.Contains(search) 
                           || (r.Invoice != null && r.Invoice.InvoiceNumber.Contains(search)) 
                           || r.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        var totalVolume = await q.SumAsync(r => (decimal?)r.TotalAmount) ?? 0;

        var items = await q.OrderByDescending(r => r.ReturnDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new {
                r.Id,
                r.ReturnNumber,
                r.PurchaseInvoiceId,
                InvoiceNumber = r.Invoice != null ? r.Invoice.InvoiceNumber : "بدون فاتورة",
                r.SupplierId,
                SupplierName = r.Supplier.Name,
                r.ReturnDate,
                r.TotalAmount,
                r.SubTotal,
                r.TaxAmount,
                r.DiscountAmount,
                r.Notes,
                CostCenter = (int?)r.CostCenter,
                CostCenterLabel = r.CostCenter == OrderSource.Website ? "الموقع" : (r.CostCenter == OrderSource.POS ? "المحل" : "عام"),
                r.WarehouseId,
                WarehouseName = r.Warehouse != null ? r.Warehouse.Name : null
            }).ToListAsync();

        return Ok(new {
            items,
            totalCount = total,
            totalItems = total,
            totalVolume,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("returns/{idOrNumber}")]
    public async Task<IActionResult> GetReturnDetail(string idOrNumber)
    {
        PurchaseReturn? rtn = null;

        // 1. Try search by ID (int)
        if (int.TryParse(idOrNumber, out int id))
        {
            rtn = await _db.PurchaseReturns
                .Include(r => r.Supplier)
                .Include(r => r.Invoice)
                .Include(r => r.Warehouse)
                .Include(r => r.Items).ThenInclude(ri => ri.Product)
                .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                .FirstOrDefaultAsync(r => r.Id == id);
            
            // 2. SMART FALLBACK: If not found as Return ID, maybe it's an Invoice ID?
            if (rtn == null)
            {
                rtn = await _db.PurchaseReturns
                    .Include(r => r.Supplier)
                    .Include(r => r.Invoice)
                    .Include(r => r.Warehouse)
                    .Include(r => r.Items).ThenInclude(ri => ri.Product)
                    .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(r => r.PurchaseInvoiceId == id);

                // 2.1 SUPER SMART FALLBACK: If no return exists for this invoice, return the invoice itself as a template
                if (rtn == null)
                {
                    var inv = await _db.PurchaseInvoices
                        .Include(i => i.Supplier)
                        .Include(i => i.Items).ThenInclude(it => it.Product)
                        .Include(i => i.Items).ThenInclude(it => it.ProductVariant)
                        .FirstOrDefaultAsync(i => i.Id == id);

                    if (inv != null)
                    {
                        return Ok(new {
                            Id = 0,
                            ReturnNumber = "NEW",
                            PurchaseInvoiceId = inv.Id,
                            InvoiceNumber = inv.InvoiceNumber,
                            SupplierId = inv.SupplierId,
                            SupplierName = inv.Supplier?.Name,
                            Supplier = inv.Supplier != null ? new { inv.Supplier.Id, Name = inv.Supplier.Name } : null,
                            ReturnDate = TimeHelper.GetEgyptTime(),
                            inv.SubTotal,
                            inv.TaxAmount,
                            inv.DiscountAmount,
                            inv.TotalAmount,
                            Notes = "",
                            inv.PaymentTerms,
                            inv.CashAccountId,
                            inv.CostCenter,
                            Items = inv.Items.Select(ri => new {
                                Id = 0,
                                PurchaseInvoiceItemId = ri.Id,
                                ProductId = ri.ProductId,
                                ProductName = ri.Product?.NameAr,
                                Sku = ri.Product?.SKU ?? "",
                                ProductVariantId = ri.ProductVariantId,
                                Size = ri.ProductVariant?.Size,
                                Color = ri.ProductVariant?.ColorAr ?? ri.ProductVariant?.Color,
                                Quantity = ri.Quantity,
                                UnitCost = ri.UnitCost,
                                TotalCost = ri.TotalCost,
                                Unit = ri.Unit
                            })
                        });
                    }
                }
            }
        }

        // 3. Try search by ReturnNumber (string)
        if (rtn == null)
        {
            rtn = await _db.PurchaseReturns
                .Include(r => r.Supplier)
                .Include(r => r.Invoice)
                .Include(r => r.Warehouse)
                .Include(r => r.Items).ThenInclude(ri => ri.Product)
                .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                .FirstOrDefaultAsync(r => r.ReturnNumber == idOrNumber);
        }

        if (rtn == null) return NotFound(new { message = _t.Get("Purchases.ReturnNotFound") });

        return Ok(new {
            rtn.Id,
            rtn.ReturnNumber,
            rtn.PurchaseInvoiceId,
            InvoiceNumber = rtn.Invoice?.InvoiceNumber,
            rtn.SupplierId,
            SupplierName = rtn.Supplier?.Name,
            Supplier = rtn.Supplier != null ? new { rtn.Supplier.Id, Name = rtn.Supplier.Name } : null,
            rtn.ReturnDate,
            rtn.SubTotal,
            rtn.TaxAmount,
            rtn.DiscountAmount,
            rtn.TotalAmount,
            rtn.Notes,
            rtn.ReferenceNumber,
            rtn.PaymentTerms,
            rtn.CashAccountId,
            rtn.CostCenter,
            rtn.WarehouseId,
            WarehouseName = rtn.Warehouse?.Name,
            Items = rtn.Items.Select(ri => new {
                ri.Id,
                ri.PurchaseInvoiceItemId,
                ri.ProductId,
                ProductName = ri.Product?.NameAr,
                Sku = ri.Product?.SKU ?? "",
                ri.ProductVariantId,
                Size = ri.ProductVariant?.Size,
                Color = ri.ProductVariant?.ColorAr ?? ri.ProductVariant?.Color,
                ri.Quantity,
                ri.Unit,
                ri.UnitCost,
                ri.TotalCost
            }).ToList()
        });
    }

    [HttpGet("returns/{idOrNumber}/pdf")]
    public async Task<IActionResult> GetReturnPdf(string idOrNumber)
    {
        PurchaseReturn? rtn = null;

        if (int.TryParse(idOrNumber, out int id))
        {
            rtn = await _db.PurchaseReturns
                .Include(r => r.Supplier)
                .Include(r => r.Invoice)
                .Include(r => r.Items).ThenInclude(ri => ri.Product)
                .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rtn == null)
            {
                rtn = await _db.PurchaseReturns
                    .Include(r => r.Supplier)
                    .Include(r => r.Invoice)
                    .Include(r => r.Items).ThenInclude(ri => ri.Product)
                    .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                    .OrderByDescending(r => r.Id)
                    .FirstOrDefaultAsync(r => r.PurchaseInvoiceId == id);
            }
        }

        if (rtn == null)
        {
            rtn = await _db.PurchaseReturns
                .Include(r => r.Supplier)
                .Include(r => r.Invoice)
                .Include(r => r.Items).ThenInclude(ri => ri.Product)
                .Include(r => r.Items).ThenInclude(ri => ri.ProductVariant)
                .FirstOrDefaultAsync(r => r.ReturnNumber == idOrNumber);
        }

        if (rtn == null) return NotFound();

        var pdfBytes = await _pdf.GeneratePurchaseReturnPdfAsync(rtn);
        return File(pdfBytes, "application/pdf", $"Return-{rtn.ReturnNumber}.pdf");
    }

    [HttpPost("returns/standalone")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> CreateStandaloneReturn([FromBody] CreateStandaloneReturnDto dto)
    {
        if (dto == null || dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneReturnItemRequired") });

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        if (supplier == null)
            return BadRequest(new { message = _t.Get("Purchases.SupplierNotFound") });

        var pUnits = await GetUnitsListAsync();
        var returnNo = await _seq.NextAsync("PR");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var pReturn = new PurchaseReturn
                {
                    ReturnNumber = returnNo,
                    SupplierId = dto.SupplierId,
                    ReturnDate = dto.ReturnDate != default ? dto.ReturnDate : TimeHelper.GetEgyptTime(),
                    Notes = dto.Notes,
                    ReferenceNumber = dto.ReferenceNumber,
                    CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    CreatedAt = TimeHelper.GetEgyptTime(),
                    DiscountAmount = dto.DiscountAmount,
                    PaymentTerms = dto.PaymentTerms,
                    CashAccountId = dto.CashAccountId > 0 ? dto.CashAccountId : null,
                    CostCenter = dto.CostCenter,
                    WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null
                };

                // التحقق من صحة PurchaseInvoiceItemIds Ù‚Ø¨Ù„ Ø§Ù„Ø¥Ø¯Ø±Ø§Ø¬ (منع FK constraint error)
                var sentInvoiceItemIds = dto.Items
                    .Where(x => x.PurchaseInvoiceItemId.HasValue && x.PurchaseInvoiceItemId.Value > 0)
                    .Select(x => x.PurchaseInvoiceItemId!.Value)
                    .Distinct().ToList();

                var validInvoiceItemIds = sentInvoiceItemIds.Any()
                    ? await _db.PurchaseInvoiceItems
                        .Where(ii => sentInvoiceItemIds.Contains(ii.Id))
                        .Select(ii => ii.Id).ToListAsync()
                    : new List<int>();

                decimal subtotal = 0;
                foreach (var item in dto.Items)
                {
                    var total = item.Quantity * item.UnitCost;
                    var multiplier = GetMultiplier(pUnits, item.Unit);

                    int? validInvoiceItemId = null;
                    if (item.PurchaseInvoiceItemId.HasValue && item.PurchaseInvoiceItemId.Value > 0
                        && validInvoiceItemIds.Contains(item.PurchaseInvoiceItemId.Value))
                        validInvoiceItemId = item.PurchaseInvoiceItemId.Value;

                    pReturn.Items.Add(new PurchaseReturnItem
                    {
                        PurchaseInvoiceItemId = validInvoiceItemId,
                        ProductId = item.ProductId,
                        ProductVariantId = item.ProductVariantId,
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        UnitCost = item.UnitCost,
                        TotalCost = total,
                        CreatedAt = TimeHelper.GetEgyptTime()
                    });
                    subtotal += total;

                    if (item.ProductId.HasValue)
                    {
                        var actualQty = item.Quantity * multiplier;
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.ReturnOut,
                            -actualQty,
                            item.ProductId,
                            item.ProductVariantId,
                            returnNo,
                            "Standalone Purchase Return",
                            pReturn.CreatedByUserId,
                            item.UnitCost / (multiplier > 0 ? multiplier : 1),
                            autoSave: false,
                            warehouseId: pReturn.WarehouseId
                        );
                    }
                }

                pReturn.SubTotal    = subtotal;
                pReturn.TaxAmount   = Math.Round(subtotal * (dto.TaxPercent / 100), 2);
                pReturn.TotalAmount = Math.Round((pReturn.SubTotal + pReturn.TaxAmount) - pReturn.DiscountAmount, 2);

                // Update supplier stats
                supplier.TotalPurchases -= pReturn.TotalAmount;
                if (pReturn.PaymentTerms == PaymentTerms.Cash)
                {
                    supplier.TotalPaid -= pReturn.TotalAmount;
                }

                _db.PurchaseReturns.Add(pReturn);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                var returnId = pReturn.Id;
                var returnNum = pReturn.ReturnNumber;
                _ = Task.Run(async () => {
                    await PostReturnJournalByReturnRecordWithRetryAsync(returnId, returnNum);
                });

                return Ok(new { id = pReturn.Id, returnNumber = pReturn.ReturnNumber, totalAmount = pReturn.TotalAmount });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error in CreateStandaloneReturn - {Message} - Inner: {InnerMessage}", ex.Message, ex.InnerException?.Message);
                return StatusCode(500, new { 
                        message = _t.Get("Purchases.InternalReturnError"),
                    error = ex.Message,
                    detail = ex.InnerException?.Message 
                });
            }
        });
    }

    [HttpPut("returns/standalone/{id}")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> UpdateStandaloneReturn(int id, [FromBody] CreateStandaloneReturnDto dto)
    {
        if (dto == null || dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneReturnItemRequired") });

        var pReturn = await _db.PurchaseReturns
            .Include(r => r.Items)
            .Include(r => r.Supplier)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (pReturn == null) return NotFound();

        var pUnits = await GetUnitsListAsync();
        
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // 1. Reverse old stock movements
                foreach (var item in pReturn.Items)
                {
                    if (item.ProductId.HasValue)
                    {
                        var mult = GetMultiplier(pUnits, item.Unit);
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Adjustment,
                            item.Quantity * mult, // Add back what was returned
                            item.ProductId,
                            item.ProductVariantId,
                            pReturn.ReturnNumber,
                            $"Edit Return #{pReturn.ReturnNumber} (Reversal)",
                            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                            autoSave: false,
                            ignoreIdempotency: true,
                            warehouseId: pReturn.WarehouseId
                        );
                    }
                }

                // 2. عكس أثر المرتجع Ø¹Ù„Ù‰ المورد القديم
                var oldSupplier = await _db.Suppliers.FindAsync(pReturn.SupplierId);
                if (oldSupplier != null)
                {
                    oldSupplier.TotalPurchases += pReturn.TotalAmount;
                    if (pReturn.PaymentTerms == PaymentTerms.Cash)
                        oldSupplier.TotalPaid += pReturn.TotalAmount;
                }

                // تحميل المورد Ø§Ù„Ø¬Ø¯ÙŠØ¯ (Ù‚Ø¯ ÙŠÙƒÙˆÙ† Ù…Ø®ØªÙ„ÙØ§Ù‹)
                var newSupplier = await _db.Suppliers.FindAsync(dto.SupplierId);
                if (newSupplier == null)
                    return BadRequest(new { message = _t.Get("Purchases.SupplierNotFound") });

                // 3. تحديث بيانات Ø§Ù„Ù…Ø±ØªØ¬Ø¹ + SupplierId
                // نستخدم Supplier Navigation Property Ù„Ø¶Ù…Ø§Ù† Ø­ÙØ¸ EF Core للمورد الجديد
                pReturn.Supplier   = newSupplier;
                pReturn.SupplierId = dto.SupplierId;
                pReturn.ReturnDate = dto.ReturnDate;
                pReturn.Notes = dto.Notes;
                pReturn.ReferenceNumber = dto.ReferenceNumber;
                pReturn.DiscountAmount = dto.DiscountAmount;
                pReturn.PaymentTerms = dto.PaymentTerms;
                pReturn.CashAccountId = dto.CashAccountId > 0 ? dto.CashAccountId : null;
                pReturn.CostCenter = dto.CostCenter;
                pReturn.WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null;
                pReturn.UpdatedAt = TimeHelper.GetEgyptTime();

                // 4. ØªØ­Ø¯ÙŠØ« Ø§Ù„Ø£ØµÙ†Ø§Ù مع التحقق من FK
                _db.PurchaseReturnItems.RemoveRange(pReturn.Items);
                pReturn.Items.Clear();
                await _db.SaveChangesAsync(); // commit Ø§Ù„Ø­Ø°Ù Ù‚Ø¨Ù„ Ø§Ù„Ø¥Ø¶Ø§ÙØ© Ù„ØªØ¬Ù†Ø¨ FK conflict

                var sentIds = dto.Items
                    .Where(x => x.PurchaseInvoiceItemId.HasValue && x.PurchaseInvoiceItemId.Value > 0)
                    .Select(x => x.PurchaseInvoiceItemId!.Value)
                    .Distinct().ToList();

                var validIds = sentIds.Any()
                    ? await _db.PurchaseInvoiceItems
                        .Where(ii => sentIds.Contains(ii.Id))
                        .Select(ii => ii.Id).ToListAsync()
                    : new List<int>();

                decimal subtotal = 0;
                foreach (var item in dto.Items)
                {
                    var total = item.Quantity * item.UnitCost;
                    var multiplier = GetMultiplier(pUnits, item.Unit);

                    int? validInvoiceItemId = null;
                    if (item.PurchaseInvoiceItemId.HasValue && item.PurchaseInvoiceItemId.Value > 0
                        && validIds.Contains(item.PurchaseInvoiceItemId.Value))
                        validInvoiceItemId = item.PurchaseInvoiceItemId.Value;

                    pReturn.Items.Add(new PurchaseReturnItem
                    {
                        PurchaseInvoiceItemId = validInvoiceItemId,
                        ProductId = item.ProductId,
                        ProductVariantId = item.ProductVariantId,
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        UnitCost = item.UnitCost,
                        TotalCost = total,
                        CreatedAt = TimeHelper.GetEgyptTime()
                    });
                    subtotal += total;

                    if (item.ProductId.HasValue)
                    {
                        var actualQty = item.Quantity * multiplier;
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.ReturnOut,
                            -actualQty,
                            item.ProductId,
                            item.ProductVariantId,
                            pReturn.ReturnNumber,
                            "Updated Standalone Return",
                            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                            item.UnitCost / (multiplier > 0 ? multiplier : 1),
                            autoSave: false,
                            ignoreIdempotency: true,
                            warehouseId: pReturn.WarehouseId
                        );
                    }
                }

                pReturn.SubTotal = subtotal;
                pReturn.TaxAmount = Math.Round(subtotal * (dto.TaxPercent / 100), 2);
                pReturn.TotalAmount = Math.Round((pReturn.SubTotal + pReturn.TaxAmount) - pReturn.DiscountAmount, 2);

                // تطبيق أثر المرتجع Ø¹Ù„Ù‰ Ø§Ù„Ù…ÙˆØ±Ø¯ Ø§Ù„Ø¬Ø¯ÙŠØ¯
                newSupplier.TotalPurchases -= pReturn.TotalAmount;
                if (pReturn.PaymentTerms == PaymentTerms.Cash)
                    newSupplier.TotalPaid -= pReturn.TotalAmount;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Trigger Accounting Sync
                var returnId = pReturn.Id;
                var returnNum = pReturn.ReturnNumber;
                _ = Task.Run(async () => {
                    await PostReturnJournalByReturnRecordWithRetryAsync(returnId, returnNum);
                });

                return Ok(new { id = pReturn.Id, returnNumber = pReturn.ReturnNumber, totalAmount = pReturn.TotalAmount });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error in UpdateStandaloneReturn - {Message} - Inner: {InnerMessage}", ex.Message, ex.InnerException?.Message);
                return StatusCode(500, new { 
                        message = _t.Get("Purchases.InternalReturnUpdateError"),
                    error = ex.Message,
                    detail = ex.InnerException?.Message 
                });
            }
        });
    }

    [HttpDelete("returns/{id}")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> DeleteReturn(int id)
    {
        var pReturn = await _db.PurchaseReturns
            .Include(r => r.Items)
            .Include(r => r.Supplier)
            .Include(r => r.Invoice)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (pReturn == null) return NotFound();

        var pUnits = await GetUnitsListAsync();
        var strategy = _db.Database.CreateExecutionStrategy();
        
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // 1. Reverse Stock Movements
                foreach (var item in pReturn.Items)
                {
                    if (item.ProductId.HasValue)
                    {
                        var mult = GetMultiplier(pUnits, item.Unit);
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Adjustment,
                            item.Quantity * mult, // Add back
                            item.ProductId,
                            item.ProductVariantId,
                            pReturn.ReturnNumber,
                            $"Delete Return #{pReturn.ReturnNumber} (Reversal)",
                            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                            autoSave: false,
                            ignoreIdempotency: true,
                            warehouseId: pReturn.WarehouseId
                        );
                    }
                }

                // 2. Reverse Supplier Impact
                if (pReturn.Supplier != null)
                {
                    pReturn.Supplier.TotalPurchases += pReturn.TotalAmount;
                    if (pReturn.PaymentTerms == PaymentTerms.Cash)
                    {
                        pReturn.Supplier.TotalPaid += pReturn.TotalAmount;
                    }
                }

                // 3. Reverse Invoice Impact (if linked)
                if (pReturn.Invoice != null)
                {
                    pReturn.Invoice.ReturnedAmount -= pReturn.TotalAmount;
                    
                    // Re-calculate invoice status
                    var totalQty = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == pReturn.PurchaseInvoiceId).SumAsync(i => i.Quantity);
                    var totalReturnedQty = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == pReturn.PurchaseInvoiceId).SumAsync(i => i.ReturnedQuantity);
                    
                    // Note: Since we are deleting THIS return, we need to adjust the ReturnedQuantity of the items too
                    decimal newTotalReturnedQty = totalReturnedQty;
                    foreach(var rItem in pReturn.Items)
                    {
                        if (rItem.PurchaseInvoiceItemId.HasValue)
                        {
                            var invItem = await _db.PurchaseInvoiceItems.FindAsync(rItem.PurchaseInvoiceItemId.Value);
                            if (invItem != null)
                            {
                                var mult = GetMultiplier(pUnits, invItem.Unit);
                                decimal returnedInOriginalUnits = mult > 0 ? (rItem.Quantity / mult) : rItem.Quantity;
                                invItem.ReturnedQuantity -= returnedInOriginalUnits;
                                newTotalReturnedQty -= returnedInOriginalUnits;
                            }
                        }
                    }

                    if (newTotalReturnedQty <= 0.001M)
                    {
                        if (pReturn.Invoice.PaidAmount >= pReturn.Invoice.TotalAmount - 0.01M)
                            pReturn.Invoice.Status = PurchaseInvoiceStatus.Paid;
                        else if (pReturn.Invoice.PaidAmount > 0)
                            pReturn.Invoice.Status = PurchaseInvoiceStatus.PartPaid;
                        else
                            pReturn.Invoice.Status = PurchaseInvoiceStatus.Received;
                    }
                    else
                    {
                        pReturn.Invoice.Status = PurchaseInvoiceStatus.PartiallyReturned;
                    }

                    pReturn.Invoice.UpdatedAt = TimeHelper.GetEgyptTime();
                }

                // 4. Remove Journal Entry
                var journalEntry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == pReturn.ReturnNumber && e.Type == JournalEntryType.PurchaseReturn);
                if (journalEntry != null) _db.JournalEntries.Remove(journalEntry);

                // 5. Finalize Deletion
                _db.PurchaseReturns.Remove(pReturn);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting purchase return {Id}", id);
                return StatusCode(500, new { message = _t.Get("Purchases.InternalDeleteReturnError"), error = ex.Message });
            }
        });
    }


    [HttpPost("{id}/return")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> ReturnPurchase(int id, [FromBody] ReturnPurchaseInvoiceDto dto)
    {
        if (dto == null)
            return BadRequest(new { message = _t.Get("Common.EmptyRequest") });

        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.ReturnItemsRequired") });

        // 1. Generate Return Document Number BEFORE starting transaction to avoid deadlocks
        // (SequenceService uses its own scope/connection)
        var returnNo = await _seq.NextAsync("PR");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                _logger.LogInformation("Processing purchase return {ReturnNo} for invoice {Id}", returnNo, id);

                var inv = await _db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .Include(i => i.Items)
                    .FirstOrDefaultAsync(i => i.Id == id);

                if (inv == null) 
                    return NotFound(new { message = _t.Get("Purchases.InvoiceNotFound") });

                if (inv.Status == PurchaseInvoiceStatus.Cancelled)
                    return BadRequest(new { message = _t.Get("Purchases.CannotReturnCancelled") });

                if (inv.Status == PurchaseInvoiceStatus.Draft || (int)inv.Status == 0) 
                    return BadRequest(new { message = _t.Get("Purchases.CannotReturnDraft") });

                var pReturn = new PurchaseReturn
                {
                    ReturnNumber      = returnNo,
                    PurchaseInvoiceId = inv.Id,
                    SupplierId        = inv.SupplierId,
                    ReturnDate        = dto.ReturnDate != default ? dto.ReturnDate : TimeHelper.GetEgyptTime(),
                    Notes             = dto.Notes,
                    ReferenceNumber   = dto.ReferenceNumber,
                    CreatedByUserId   = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    CostCenter        = inv.CostCenter,  // Always inherit from the linked invoice
                    WarehouseId       = dto.WarehouseId > 0 ? dto.WarehouseId : inv.WarehouseId
                };

                decimal totalSubTotal = 0;
                var pUnits = await GetUnitsListAsync();

                foreach (var reqItem in dto.Items)
                {
                    var invItem = inv.Items.FirstOrDefault(i => i.Id == reqItem.PurchaseInvoiceItemId);
                    if (invItem == null) 
                    {
                        _logger.LogWarning("Return item {ItemId} not found in invoice {InvoiceId}", reqItem.PurchaseInvoiceItemId, id);
                        continue;
                    }

                    var multiplier = GetMultiplier(pUnits, invItem.Unit);
                    
                    // A. Validate against Invoice quantity remaining
                    decimal invQtyInPieces = invItem.Quantity * multiplier;
                    decimal returnedInPieces = invItem.ReturnedQuantity * multiplier;
                    decimal remainingInPieces = Math.Max(0, invQtyInPieces - returnedInPieces);

                    if (reqItem.Quantity > remainingInPieces + 0.001M)
                        return BadRequest(new { message = _t.Get("Purchases.ReturnQuantityExceedsRemaining", reqItem.Quantity, remainingInPieces, invItem.Description) });

                    // B. Validate against PHYSICAL STOCK
                    var physicalStock = await _inventory.GetCurrentStockAsync(invItem.ProductId, invItem.ProductVariantId);
                    if (reqItem.Quantity > physicalStock + 0.001M)
                        return BadRequest(new { message = _t.Get("Purchases.ReturnQuantityExceedsStock", invItem.Description, physicalStock) });

                    // C. Update Invoice Item
                    decimal returnedInOriginalUnits = multiplier > 0 ? (reqItem.Quantity / multiplier) : reqItem.Quantity;
                    invItem.ReturnedQuantity += returnedInOriginalUnits;

                    // D. Create Return Item
                    decimal unitCostPerPiece = multiplier > 0 ? (invItem.UnitCost / multiplier) : invItem.UnitCost;
                    var itemSubTotal = Math.Round(reqItem.Quantity * unitCostPerPiece, 2);
                    
                    pReturn.Items.Add(new PurchaseReturnItem
                    {
                        PurchaseInvoiceItemId = invItem.Id,
                        ProductId             = invItem.ProductId,
                        ProductVariantId      = invItem.ProductVariantId,
                        Quantity              = reqItem.Quantity,
                        UnitCost              = unitCostPerPiece,
                        TotalCost             = itemSubTotal
                    });

                    totalSubTotal += itemSubTotal;

                    // E. Log Inventory Movement
                    if (invItem.ProductId.HasValue)
                    {
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.ReturnOut,
                            -reqItem.Quantity, 
                            invItem.ProductId,
                            invItem.ProductVariantId,
                            returnNo,
                            $"مرتجع مشتريات (Ù Ø§ØªÙˆØ±Ø© Ø±Ù‚Ù… #{inv.InvoiceNumber})",
                            pReturn.CreatedByUserId,
                            unitCostPerPiece,
                            autoSave: false,
                            warehouseId: pReturn.WarehouseId
                        );
                    }
                }

                if (!pReturn.Items.Any() || totalSubTotal <= 0)
            return BadRequest(new { message = _t.Get("Purchases.NoValidReturnQuantities") });

                // 2. Financial Calculations
                decimal subTotal_Original = Math.Max(1, inv.SubTotal);
                decimal prorationRatio    = totalSubTotal / subTotal_Original;
                
                pReturn.SubTotal       = Math.Round(totalSubTotal, 2);
                pReturn.TaxAmount      = Math.Round(inv.TaxAmount * prorationRatio, 2);
                pReturn.DiscountAmount = Math.Round(inv.DiscountAmount * prorationRatio, 2);
                pReturn.TotalAmount    = Math.Round((pReturn.SubTotal + pReturn.TaxAmount) - pReturn.DiscountAmount, 2);

                // 3. Update Invoice & Supplier Ledger
                inv.ReturnedAmount += pReturn.TotalAmount;
                if (inv.Supplier != null)
                {
                    inv.Supplier.TotalPurchases -= pReturn.TotalAmount;
                }

                var totalQty = inv.Items.Sum(i => i.Quantity);
                var totalReturnedQty = inv.Items.Sum(i => i.ReturnedQuantity);

                if (totalReturnedQty >= totalQty - 0.001M)
                    inv.Status = PurchaseInvoiceStatus.Returned;
                else if (totalReturnedQty > 0.001M)
                    inv.Status = PurchaseInvoiceStatus.PartiallyReturned;

                inv.UpdatedAt = TimeHelper.GetEgyptTime();

                _db.PurchaseReturns.Add(pReturn);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Purchase return {ReturnNo} committed successfully.", returnNo);

                // 4. Trigger Accounting Sync (Safely in background with retries)
                var returnId = pReturn.Id;
                var returnNum = pReturn.ReturnNumber;
                _ = Task.Run(async () => {
                    await PostReturnJournalByReturnRecordWithRetryAsync(returnId, returnNum);
                });

                return Ok(new { 
                    id = inv.Id, 
                    returnId = pReturn.Id,
                    returnNumber = pReturn.ReturnNumber,
                    status = inv.Status.ToString(),
                    totalAmount = pReturn.TotalAmount
                });
            }
            catch (Exception ex)
            {
                try { await transaction.RollbackAsync(); } catch { /* Ignore */ }
                    
                _logger.LogError(ex, "Severe error in ReturnPurchase for invoice {Id}", id);
                return StatusCode(500, new { 
                        message = _t.Get("Purchases.InternalReturnError"),
                    error = ex.Message,
                    detail = ex.InnerException?.Message 
                });
            }
        });
    }

    private async Task PostReturnJournalByReturnRecordWithRetryAsync(int returnId, string returnNumber)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var pReturn = await db.PurchaseReturns
                    .Include(r => r.Supplier)
                    .Include(r => r.Invoice)
                    .Include(r => r.Items)
                    .FirstAsync(r => r.Id == returnId);

                await acc.PostPurchaseReturnAsync(pReturn);
                return; 
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] Return Journal posting attempt {Attempt}/{Max} failed for return {Number}. Retrying...", attempt, maxAttempts, returnNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] Return Journal posting permanently failed for return {Number}.", returnNumber);
            }
        }
    }


    private async Task PostPartialJournalWithRetryAsync(int invoiceId, string invoiceNumber, decimal returnedSubTotal, decimal returnedTaxAmount, decimal returnedDiscountAmount)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                var db  = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var inv = await db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .FirstAsync(i => i.Id == invoiceId);

                await acc.PostPurchaseReturnAsync(inv, returnedSubTotal, returnedTaxAmount, returnedDiscountAmount);
                return; 
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "[Accounting] Partial Return Journal posting attempt {Attempt}/{Max} failed for invoice {Number}. Retrying...", attempt, maxAttempts, invoiceNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Accounting] Partial Return Journal posting permanently failed for invoice {Number}.", invoiceNumber);
            }
        }
    }

}
