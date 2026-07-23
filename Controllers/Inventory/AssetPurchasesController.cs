using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using System.Security.Claims;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

/// <summary>
/// موديول مشتريات الأصول الثابتة
/// </summary>
[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.AssetsMain)]
public class AssetPurchasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly ITranslator _t;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AssetPurchasesController> _logger;
    private readonly IAuditService _audit;
    private readonly AccountingCoreService _core;

    public AssetPurchasesController(AppDbContext db, SequenceService seq, ITranslator t, IServiceScopeFactory scopeFactory, ILogger<AssetPurchasesController> logger, IAuditService audit, AccountingCoreService core)
    {
        _db = db;
        _seq = seq;
        _t = t;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _audit = audit;
        _core = core;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? supplierId = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .Where(i => i.IsAssetPurchase)
            .AsQueryable();

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(i => i.InvoiceDate >= fromDate.Value.Date);
        if (toDate.HasValue)     q = q.Where(i => i.InvoiceDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? isolatedBranchId = canViewAll ? (branchId ?? User.GetBranchId()) : User.GetBranchId();
        if (!canViewAll || branchId.HasValue)
        {
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(i => i.BranchId == isolatedBranchId.Value);
            }
        }
        
        if (!string.IsNullOrEmpty(search))
            q = q.Where(i => i.InvoiceNumber.Contains(search)
                           || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(search))
                           || i.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(i => i.InvoiceDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new PurchaseInvoiceSummaryDto(
                i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.SupplierId, i.Supplier.Name,
                i.PaymentTerms.ToString(), i.IsAssetPurchase, i.Status.ToString(),
                i.InvoiceDate, i.DueDate,
                i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount,
                i.CostCenter,
                i.CostCenter == OrderSource.Website ? "الموقع" : (i.CostCenter == OrderSource.POS ? "المحل" : "عام"),
                null,
                null
            )).ToListAsync();

        return Ok(new PaginatedResult<PurchaseInvoiceSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.FixedAssetCategory)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id && i.IsAssetPurchase);

        if (inv == null) return NotFound();

        return Ok(new PurchaseInvoiceDetailDto(
            inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNumber,
            new SupplierBasicDto(inv.Supplier?.Id ?? 0, inv.Supplier?.Name ?? "---", inv.Supplier?.Phone ?? "---", inv.Supplier?.CompanyName),
            inv.PaymentTerms.ToString(), inv.Status.ToString(), inv.IsAssetPurchase,
            inv.InvoiceDate, inv.DueDate,
            inv.SubTotal, inv.TaxPercent, inv.TaxAmount, inv.IsTaxInclusive, inv.DiscountAmount, inv.TotalAmount,
            inv.PaidAmount, inv.TotalAmount - inv.PaidAmount - inv.ReturnedAmount, inv.Notes,
            inv.Items.Select(it => new PurchaseItemDto(
                it.Id, it.Description, it.ProductId, 
                null, null, 
                null, null, null,
                it.Unit, 1, it.Quantity, it.ReturnedQuantity, it.UnitCost, it.TaxRate, it.IsTaxInclusive, it.TotalCost,
                null, // ProductVariants
                it.FixedAssetCategoryId, it.FixedAssetCategory?.Name, it.AssetName, it.CreatedAssetId
            )).ToList(),
            inv.Payments.Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, inv.Supplier?.Name ?? "---", inv.InvoiceNumber,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId
            )).ToList(),
            inv.AttachmentUrl, inv.AttachmentPublicId,
            inv.CostCenter,
            inv.CashAccountId,
            inv.SupplierId,
            inv.CostCenter == OrderSource.Website ? "الموقع" : (inv.CostCenter == OrderSource.POS ? "المحل" : "عام"),
            null,
            null
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseInvoiceDto dto)
    {
        if (!dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneItemRequired") });

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
                if (supplier == null)
                    return BadRequest(new { message = _t.Get("Purchases.SupplierNotFound") });

                var invNo = await _seq.NextAsync("APO"); // Asset Purchase Order

                var invoice = new PurchaseInvoice
                {
                    InvoiceNumber         = invNo,
                    SupplierInvoiceNumber = dto.SupplierInvoiceNumber,
                    SupplierId            = dto.SupplierId,
                    PaymentTerms          = dto.PaymentTerms,
                    InvoiceDate           = dto.InvoiceDate,
                    PaymentTermDays       = dto.PaymentTermDays,
                    DueDate               = dto.PaymentTerms == PaymentTerms.Cash ? null : (dto.PaymentTermDays.HasValue ? dto.InvoiceDate.AddDays(dto.PaymentTermDays.Value) : dto.DueDate),
                    TaxPercent            = dto.TaxPercent,
                    DiscountAmount        = dto.DiscountAmount,
                    Notes                 = dto.Notes,
                    Status                = dto.PaymentTerms == PaymentTerms.Cash ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.Received,
                    AttachmentUrl         = dto.AttachmentUrl,
                    AttachmentPublicId    = dto.AttachmentPublicId,
                    CreatedAt             = TimeHelper.GetEgyptTime(),
                    VendorAccountId       = dto.VendorAccountId > 0 ? dto.VendorAccountId : null,
                    VatAccountId          = dto.VatAccountId > 0 ? dto.VatAccountId : null,
                    CashAccountId         = dto.CashAccountId > 0 ? dto.CashAccountId : null,
                    CostCenter            = dto.CostCenter,
                    IsTaxInclusive        = dto.IsTaxInclusive,
                    IsAssetPurchase       = true,
                    BranchId              = User.GetBranchId() // Will use user's branch
                };

                decimal subtotal = 0;
                decimal totalLineTax = 0;

                foreach (var item in dto.Items)
                {
                    if (!item.FixedAssetCategoryId.HasValue)
                        return BadRequest(new { message = $"Category is required for asset item: {item.Description}" });

                    var category = await _db.FixedAssetCategories.FindAsync(item.FixedAssetCategoryId.Value);
                    if (category == null)
                        return BadRequest(new { message = $"Category not found for item: {item.Description}" });

                    var lineBase = item.Quantity * item.UnitCost;
                    var lineTax = 0M;
                    if (item.TaxRate > 0)
                    {
                        if (item.IsTaxInclusive)
                        {
                            var basePrice = lineBase / (1 + (item.TaxRate / 100));
                            lineTax = lineBase - basePrice;
                            lineBase = basePrice;
                        }
                        else
                        {
                            lineTax = lineBase * (item.TaxRate / 100);
                        }
                    }

                    var invItem = new PurchaseInvoiceItem
                    {
                        Description = item.Description,
                        FixedAssetCategoryId = item.FixedAssetCategoryId,
                        AssetName = item.AssetName ?? item.Description,
                        Quantity = item.Quantity,
                        UnitCost = item.UnitCost,
                        TaxRate = item.TaxRate,
                        IsTaxInclusive = item.IsTaxInclusive,
                        TotalCost = Math.Round(item.Quantity * item.UnitCost, 2),
                        CreatedAt = TimeHelper.GetEgyptTime()
                    };

                    invoice.Items.Add(invItem);
                    subtotal += lineBase;
                    totalLineTax += lineTax;

                    // إنشاء الأصول تلقائياً
                    for (int i = 0; i < (int)item.Quantity; i++)
                    {
                        var assetNo = await _seq.NextAsync("FA");
                        var asset = new FixedAsset
                        {
                            AssetNumber = assetNo,
                            Name = item.AssetName ?? item.Description,
                            CategoryId = item.FixedAssetCategoryId.Value,
                            PurchaseDate = dto.InvoiceDate,
                            PurchaseCost = item.Quantity > 0 ? Math.Round(lineBase / item.Quantity, 2) : item.UnitCost,
                            Status = AssetStatus.Active,
                            PurchaseInvoiceId = null, // Set to temp null, will update after save
                            CreatedAt = TimeHelper.GetEgyptTime(),
                            Supplier = supplier.Name,
                            CostCenter = invoice.CostCenter,
                            UsefulLifeYears = 5, // Default
                            DepreciationMethod = DepreciationMethod.StraightLine,
                            SalvageValue = 0,
                            DepreciationStartDate = dto.InvoiceDate,
                            BranchId = invoice.BranchId
                        };
                        _db.FixedAssets.Add(asset);
                        
                        if (i == 0) invItem.CreatedAsset = asset;
                    }
                }

                invoice.SubTotal = Math.Round(subtotal, 2);
                
                decimal globalTax = 0;
                if (dto.TaxPercent > 0)
                {
                    var baseForGlobal = subtotal - dto.DiscountAmount;
                    if (dto.IsTaxInclusive)
                    {
                        var divisor = 1 + (dto.TaxPercent / 100);
                        var net = divisor > 0 ? baseForGlobal / divisor : baseForGlobal;
                        globalTax = baseForGlobal - net;
                    }
                    else
                    {
                        globalTax = baseForGlobal * (dto.TaxPercent / 100);
                    }
                }

                invoice.TaxAmount = Math.Round(totalLineTax + globalTax, 2);
                
                if (dto.IsTaxInclusive)
                    invoice.TotalAmount = Math.Round(subtotal + totalLineTax - dto.DiscountAmount, 2);
                else
                    invoice.TotalAmount = Math.Round(subtotal + globalTax + totalLineTax - dto.DiscountAmount, 2);

                supplier.TotalPurchases += invoice.TotalAmount;

                if (dto.PaymentTerms == PaymentTerms.Cash)
                {
                    invoice.PaidAmount = invoice.TotalAmount;
                    supplier.TotalPaid += invoice.TotalAmount;

                    var pNo = await _seq.NextAsync("SP");
                    invoice.Payments.Add(new SupplierPayment
                    {
                        PaymentNumber = pNo,
                        SupplierId    = supplier.Id,
                        Amount        = invoice.TotalAmount,
                        PaymentDate   = invoice.InvoiceDate,
                        PaymentMethod = PaymentMethod_Purchase.Cash,
                        AccountName   = "الخزينة (آلي)",
                        Notes         = $"سداد تلقائي لفاتورة أصول {invNo}",
                        CreatedAt     = TimeHelper.GetEgyptTime(),
                        CostCenter    = invoice.CostCenter
                    });
                }

                _db.PurchaseInvoices.Add(invoice);
                await _db.SaveChangesAsync();

                // تحديث الـ InvoiceId في الأصول المنشأة
                var createdAssets = _db.ChangeTracker.Entries<FixedAsset>()
                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                    .Select(e => e.Entity)
                    .Where(a => a.PurchaseInvoiceId == null)
                    .ToList();
                
                foreach(var a in createdAssets) a.PurchaseInvoiceId = invoice.Id;
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();
                try { await _audit.LogAsync("CreateAssetPurchase", "PurchaseInvoice", invoice.Id.ToString(), $"Created asset purchase #{invoice.InvoiceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

                _ = PostJournalWithRetryAsync(invoice.Id, invoice.InvoiceNumber);

                return Ok(new { id = invoice.Id, invoiceNumber = invoice.InvoiceNumber });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Asset purchase invoice creation failed.");
                return StatusCode(500, new { message = "Error creating asset purchase", error = ex.Message });
            }
        });
    }

    private async Task PostJournalWithRetryAsync(int invoiceId, string invoiceNumber)
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
                    .Include(i => i.Payments)
                    .FirstAsync(i => i.Id == invoiceId);

                await acc.PostPurchaseInvoiceAsync(inv);

                if (inv.PaymentTerms == PaymentTerms.Cash && inv.Payments.Any())
                {
                    var payment = inv.Payments.First();
                    await acc.PostSupplierPaymentAsync(payment);
                }

                return;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Journal posting attempt {At} failed for asset invoice {No}", attempt, invoiceNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseInvoiceDto dto)
    {
        if (!dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneItemRequired") });

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var invoice = await _db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .Include(i => i.Items)
                    .Include(i => i.Payments)
                    .FirstOrDefaultAsync(i => i.Id == id && i.IsAssetPurchase);

                if (invoice == null)
                    return NotFound(new { message = "Invoice not found" });
                    
                await _core.CheckDateLockAsync(invoice.InvoiceDate, User);
                if (dto.InvoiceDate.Date != invoice.InvoiceDate.Date)
                    await _core.CheckDateLockAsync(dto.InvoiceDate, User);

                // Validation: Check if any associated asset is depreciated or disposed
                var associatedAssets = await _db.FixedAssets
                    .Where(a => a.PurchaseInvoiceId == id)
                    .ToListAsync();
                    
                if (associatedAssets.Any(a => a.AccumulatedDepreciation > 0 || a.Status == AssetStatus.Disposed))
                {
                    return BadRequest(new { message = _t.Get("Assets.CannotEditDepreciatedInvoice", "لا يمكن تعديل الفاتورة لأنه تم إهلاك أو استبعاد أحد الأصول المرتبطة بها.") });
                }

                var supplier = invoice.Supplier;
                if (dto.SupplierId.HasValue && dto.SupplierId.Value != invoice.SupplierId)
                {
                    // Revert old supplier balances
                    supplier.TotalPurchases -= invoice.TotalAmount;
                    if (invoice.PaymentTerms == PaymentTerms.Cash)
                        supplier.TotalPaid -= invoice.PaidAmount;
                        
                    supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId.Value);
                    if (supplier == null)
                        return BadRequest(new { message = _t.Get("Purchases.SupplierNotFound") });
                    invoice.SupplierId = supplier.Id;
                }
                else
                {
                    supplier.TotalPurchases -= invoice.TotalAmount;
                    if (invoice.PaymentTerms == PaymentTerms.Cash)
                        supplier.TotalPaid -= invoice.PaidAmount;
                }

                // Delete old invoice items and old payments first (they will be re-added below)
                _db.PurchaseInvoiceItems.RemoveRange(invoice.Items);
                
                if (invoice.Payments.Any())
                {
                    _db.SupplierPayments.RemoveRange(invoice.Payments);
                }
                await _db.SaveChangesAsync(); // Clear items to prevent FK constraint issues

                // NOTE: We do NOT delete FixedAssets here. They are updated in-place below
                // to preserve asset numbers and history.

                invoice.Items.Clear();
                invoice.Payments.Clear();

                // Apply new values
                invoice.SupplierInvoiceNumber = dto.SupplierInvoiceNumber;
                invoice.PaymentTerms = dto.PaymentTerms;
                invoice.InvoiceDate = dto.InvoiceDate;
                invoice.DueDate = dto.PaymentTerms == PaymentTerms.Cash ? null : dto.DueDate;
                invoice.TaxPercent = dto.TaxPercent;
                invoice.DiscountAmount = dto.DiscountAmount;
                invoice.Notes = dto.Notes;
                invoice.Status = dto.PaymentTerms == PaymentTerms.Cash ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.Received;
                invoice.AttachmentUrl = dto.AttachmentUrl ?? invoice.AttachmentUrl;
                invoice.AttachmentPublicId = dto.AttachmentPublicId ?? invoice.AttachmentPublicId;
                invoice.UpdatedAt = TimeHelper.GetEgyptTime();
                invoice.VendorAccountId = dto.VendorAccountId > 0 ? dto.VendorAccountId : null;
                invoice.InventoryAccountId = dto.InventoryAccountId > 0 ? dto.InventoryAccountId : null;
                invoice.VatAccountId = dto.VatAccountId > 0 ? dto.VatAccountId : null;
                invoice.CashAccountId = dto.CashAccountId > 0 ? dto.CashAccountId : null;
                invoice.CostCenter = dto.CostCenter;
                invoice.IsTaxInclusive = dto.IsTaxInclusive;
                invoice.WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null;

                decimal subtotal = 0;
                decimal totalLineTax = 0;

                // Update existing assets in-place instead of recreating them
                // This preserves asset numbers and avoids duplicate entries in the fixed asset register
                var existingAssets = await _db.FixedAssets
                    .Where(a => a.PurchaseInvoiceId == id)
                    .OrderBy(a => a.Id)
                    .ToListAsync();
                
                int assetIndex = 0;

                foreach (var item in dto.Items)
                {
                    if (!item.FixedAssetCategoryId.HasValue)
                        return BadRequest(new { message = _t.Get("Assets.CategoryRequired", "يجب تحديد فئة الأصل لكل عنصر.") });

                    var lineBase = item.Quantity * item.UnitCost;
                    var lineTax = 0M;
                    if (item.TaxRate > 0)
                    {
                        if (item.IsTaxInclusive)
                        {
                            var basePrice = lineBase / (1 + (item.TaxRate / 100));
                            lineTax = lineBase - basePrice;
                            lineBase = basePrice;
                        }
                        else
                        {
                            lineTax = lineBase * (item.TaxRate / 100);
                        }
                    }

                    var invItem = new PurchaseInvoiceItem
                    {
                        Description = item.Description,
                        FixedAssetCategoryId = item.FixedAssetCategoryId,
                        AssetName = item.AssetName ?? item.Description,
                        Quantity = item.Quantity,
                        UnitCost = item.UnitCost,
                        TaxRate = item.TaxRate,
                        IsTaxInclusive = item.IsTaxInclusive,
                        TotalCost = Math.Round(item.Quantity * item.UnitCost, 2),
                        CreatedAt = TimeHelper.GetEgyptTime()
                    };
                    invoice.Items.Add(invItem);

                    for (int i = 0; i < (int)item.Quantity; i++)
                    {
                        FixedAsset asset;
                        if (assetIndex < existingAssets.Count)
                        {
                            // Update existing asset in-place
                            asset = existingAssets[assetIndex];
                            asset.Name = item.AssetName ?? item.Description;
                            asset.CategoryId = item.FixedAssetCategoryId.Value;
                            asset.PurchaseDate = dto.InvoiceDate;
                            asset.PurchaseCost = item.Quantity > 0 ? Math.Round(lineBase / item.Quantity, 2) : item.UnitCost;
                            asset.CostCenter = invoice.CostCenter;
                            asset.BranchId = invoice.BranchId;
                            
                            assetIndex++;
                        }
                        else
                        {
                            // Only create new assets if quantity was increased
                            var assetNo = await _seq.NextAsync("FA");
                            asset = new FixedAsset
                            {
                                AssetNumber = assetNo,
                                Name = item.AssetName ?? item.Description,
                                CategoryId = item.FixedAssetCategoryId.Value,
                                PurchaseDate = dto.InvoiceDate,
                                PurchaseCost = item.Quantity > 0 ? Math.Round(lineBase / item.Quantity, 2) : item.UnitCost,
                                Status = AssetStatus.Active,
                                PurchaseInvoiceId = invoice.Id,
                                CreatedAt = TimeHelper.GetEgyptTime(),
                                CostCenter = invoice.CostCenter,
                                UsefulLifeYears = 5,
                                DepreciationMethod = DepreciationMethod.StraightLine,
                                SalvageValue = 0,
                                DepreciationStartDate = dto.InvoiceDate,
                                BranchId = invoice.BranchId
                            };
                            _db.FixedAssets.Add(asset);
                        }

                        // Set the CreatedAsset reference for the first item so foreign keys map correctly
                        if (i == 0) invItem.CreatedAsset = asset;
                    }

                    subtotal += lineBase;
                    totalLineTax += lineTax;
                }

                // Remove extra assets if overall quantity was decreased
                if (assetIndex < existingAssets.Count)
                {
                    var toRemove = existingAssets.Skip(assetIndex).ToList();
                    _db.FixedAssets.RemoveRange(toRemove);
                }

                invoice.SubTotal = Math.Round(subtotal, 2);

                decimal globalTax = 0;
                if (dto.TaxPercent > 0)
                {
                    var baseForGlobal = subtotal - dto.DiscountAmount;
                    if (dto.IsTaxInclusive)
                    {
                        var divisor = 1 + (dto.TaxPercent / 100);
                        var net = divisor > 0 ? baseForGlobal / divisor : baseForGlobal;
                        globalTax = baseForGlobal - net;
                    }
                    else
                    {
                        globalTax = baseForGlobal * (dto.TaxPercent / 100);
                    }
                }

                invoice.TaxAmount = Math.Round(totalLineTax + globalTax, 2);

                if (dto.IsTaxInclusive)
                    invoice.TotalAmount = Math.Round(subtotal + totalLineTax - dto.DiscountAmount, 2);
                else
                    invoice.TotalAmount = Math.Round(subtotal + globalTax + totalLineTax - dto.DiscountAmount, 2);

                supplier.TotalPurchases += invoice.TotalAmount;

                if (dto.PaymentTerms == PaymentTerms.Cash)
                {
                    invoice.PaidAmount = invoice.TotalAmount;
                    supplier.TotalPaid += invoice.TotalAmount;

                    var pNo = await _seq.NextAsync("SP");
                    invoice.Payments.Add(new SupplierPayment
                    {
                        PaymentNumber = pNo,
                        SupplierId    = supplier.Id,
                        Amount        = invoice.TotalAmount,
                        PaymentDate   = invoice.InvoiceDate,
                        PaymentMethod = PaymentMethod_Purchase.Cash,
                        AccountName   = "الخزينة (آلي)",
                        Notes         = $"سداد تلقائي لفاتورة أصول {invoice.InvoiceNumber}",
                        CreatedAt     = TimeHelper.GetEgyptTime(),
                        CostCenter    = invoice.CostCenter,
                        PurchaseInvoiceId = invoice.Id
                    });
                }
                else 
                {
                    invoice.PaidAmount = 0;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                try { await _audit.LogAsync("UpdateAssetPurchase", "PurchaseInvoice", invoice.Id.ToString(), $"Updated asset purchase #{invoice.InvoiceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

                _ = PostJournalWithRetryAsync(invoice.Id, invoice.InvoiceNumber);

                return Ok(new { id = invoice.Id, invoiceNumber = invoice.InvoiceNumber });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Asset purchase invoice update failed.");
                return StatusCode(500, new { message = "Error updating asset purchase", error = ex.Message });
            }
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var invoice = await _db.PurchaseInvoices
                    .Include(i => i.Supplier)
                    .Include(i => i.Items)
                    .Include(i => i.Payments)
                    .FirstOrDefaultAsync(i => i.Id == id && i.IsAssetPurchase);

                if (invoice == null)
                    return NotFound(new { message = "Invoice not found" });

                await _core.CheckDateLockAsync(invoice.InvoiceDate, User);

                // Validation: Check if any associated asset is depreciated or disposed
                var associatedAssets = await _db.FixedAssets
                    .Where(a => a.PurchaseInvoiceId == id)
                    .ToListAsync();

                if (associatedAssets.Any(a => a.AccumulatedDepreciation > 0 || a.Status == AssetStatus.Disposed))
                {
                    return BadRequest(new { message = _t.Get("Assets.CannotDeleteDepreciatedInvoice", "لا يمكن حذف الفاتورة لأنه تم إهلاك أو استبعاد أحد الأصول المرتبطة بها.") });
                }

                // Revert balances
                var supplier = invoice.Supplier;
                supplier.TotalPurchases -= invoice.TotalAmount;
                if (invoice.PaymentTerms == PaymentTerms.Cash)
                    supplier.TotalPaid -= invoice.PaidAmount;

                // Delete related records: Items first to prevent FK constraint
                _db.PurchaseInvoiceItems.RemoveRange(invoice.Items);
                
                var paymentNumbers = invoice.Payments.Select(p => p.PaymentNumber).ToList();
                _db.SupplierPayments.RemoveRange(invoice.Payments);
                await _db.SaveChangesAsync(); // Commit items deletion

                _db.FixedAssets.RemoveRange(associatedAssets);

                _db.PurchaseInvoices.Remove(invoice);

                // Delete Journal Entries manually (Invoice and Payments)
                var invoiceJe = await _db.JournalEntries.Include(e => e.Lines)
                    .FirstOrDefaultAsync(e => e.Reference == invoice.InvoiceNumber && e.Type == JournalEntryType.PurchaseInvoice);
                if (invoiceJe != null)
                {
                    _db.JournalLines.RemoveRange(invoiceJe.Lines);
                    _db.JournalEntries.Remove(invoiceJe);
                }

                foreach (var pNum in paymentNumbers)
                {
                    var paymentJe = await _db.JournalEntries.Include(e => e.Lines)
                        .FirstOrDefaultAsync(e => e.Reference == pNum && e.Type == JournalEntryType.PaymentVoucher);
                    if (paymentJe != null)
                    {
                        _db.JournalLines.RemoveRange(paymentJe.Lines);
                        _db.JournalEntries.Remove(paymentJe);
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                try { await _audit.LogAsync("DeleteAssetPurchase", "PurchaseInvoice", id.ToString(), $"Deleted asset purchase #{invoice.InvoiceNumber}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

                return Ok(new { message = "Deleted successfully" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Asset purchase invoice deletion failed.");
                return StatusCode(500, new { message = "Error deleting asset purchase", error = ex.Message });
            }
        });
    }
}
