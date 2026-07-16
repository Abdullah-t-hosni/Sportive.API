using System.Security.Claims;
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
[Route("api/[controller]")]
[Authorize]
public class PurchaseInvoicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProductService _productService;
    private readonly IInventoryService _inventory;
    private readonly ILogger<PurchaseInvoicesController> _logger;
    private readonly SequenceService _seq;
    private readonly IPdfService _pdf;
    private readonly ITranslator _t;
    private readonly IAuditService _auditLog;

    public PurchaseInvoicesController(AppDbContext db, IServiceScopeFactory scopeFactory, IProductService productService, IInventoryService inventory, ILogger<PurchaseInvoicesController> logger, SequenceService seq, IPdfService pdf, ITranslator t, IAuditService auditLog)
    {
        _t = t;
        _auditLog = auditLog;
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

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int?    supplierId = null,
        [FromQuery] string? status     = null,
        [FromQuery] string? search     = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] OrderSource? costCenter = null,
        [FromQuery] int? branchId = null,
        [FromQuery] int? warehouseId = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PurchaseInvoices
            .AsNoTracking()
            .Include(i => i.Supplier)
            .Include(i => i.Warehouse)
            .Where(i => !i.IsAssetPurchase)
            .AsQueryable();

        if (costCenter.HasValue) q = q.Where(i => i.CostCenter == costCenter.Value);

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        int? isolatedBranchId = canViewAll ? branchId : User.GetBranchId();
        int? isolatedWarehouseId = canViewAll ? warehouseId : User.GetWarehouseId();

        // Warehouse is directly on PurchaseInvoice
        if (isolatedWarehouseId.HasValue) 
        {
            q = q.Where(i => i.WarehouseId == isolatedWarehouseId.Value);
        }
        if (isolatedBranchId.HasValue)
        {
            q = q.Where(i => i.BranchId == isolatedBranchId.Value);
        }

        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
        if (fromDate.HasValue)   q = q.Where(i => i.InvoiceDate >= fromDate.Value.Date);
        if (toDate.HasValue)     q = q.Where(i => i.InvoiceDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrEmpty(status))
        {
            var stList = status.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var parsedStatuses = new List<PurchaseInvoiceStatus>();
            foreach (var s in stList)
            {
                if (Enum.TryParse<PurchaseInvoiceStatus>(s.Trim(), out var st))
                    parsedStatuses.Add(st);
            }
            if (parsedStatuses.Count > 0)
                q = q.Where(i => parsedStatuses.Contains(i.Status));
        }
        if (!string.IsNullOrEmpty(search))
            q = q.Where(i => i.InvoiceNumber.Contains(search)
                           || (i.SupplierInvoiceNumber != null && i.SupplierInvoiceNumber.Contains(search))
                           || i.Supplier.Name.Contains(search));

        var total = await q.CountAsync();
        
        // Optimize: Calculate summaries in parallel or efficiently
        var totalVolume    = await q.SumAsync(i => (decimal?)i.TotalAmount) ?? 0;
        var totalRemaining = await q.SumAsync(i => (decimal?)(i.TotalAmount - i.PaidAmount - i.ReturnedAmount)) ?? 0;
        var totalQuantity  = await q.SelectMany(i => i.Items).SumAsync(it => (decimal?)it.Quantity) ?? 0;

        var items = await q.OrderByDescending(i => i.InvoiceDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(i => new PurchaseInvoiceSummaryDto(
                i.Id, i.InvoiceNumber, i.SupplierInvoiceNumber, i.SupplierId, i.Supplier.Name,
                i.PaymentTerms.ToString(), i.IsAssetPurchase, i.Status.ToString(),
                i.InvoiceDate, i.DueDate,
                i.TotalAmount, i.PaidAmount, i.TotalAmount - i.PaidAmount - i.ReturnedAmount,
                i.CostCenter,
                i.CostCenter == OrderSource.Website ? "الموقع" : (i.CostCenter == OrderSource.POS ? "المحل" : "عام"),
                i.WarehouseId,
                i.Warehouse != null ? i.Warehouse.Name : null
            )).ToListAsync();

        return Ok(new PaginatedResult<PurchaseInvoiceSummaryDto>(
            items, total, page, pageSize, (int)Math.Ceiling((double)total / pageSize),
            totalVolume, totalRemaining, totalQuantity, total
        ));
    }

    [HttpGet("{id}")]

    public async Task<IActionResult> GetById(int id)
    {
        var pUnits = await GetUnitsListAsync();
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Warehouse)
            .Include(i => i.Items).ThenInclude(it => it.Product!).ThenInclude(p => p.Variants)
            .Include(i => i.Items).ThenInclude(it => it.ProductVariant)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        return Ok(new PurchaseInvoiceDetailDto(
            inv.Id, inv.InvoiceNumber, inv.SupplierInvoiceNumber,
            new SupplierBasicDto(inv.Supplier!.Id, inv.Supplier.Name, inv.Supplier.Phone, inv.Supplier.CompanyName),
            inv.PaymentTerms.ToString(), inv.Status.ToString(), inv.IsAssetPurchase,
            inv.InvoiceDate, inv.DueDate,
            inv.SubTotal, inv.TaxPercent, inv.TaxAmount, inv.IsTaxInclusive, inv.DiscountAmount, inv.TotalAmount,
            inv.PaidAmount, inv.TotalAmount - inv.PaidAmount - inv.ReturnedAmount, inv.Notes,
            inv.Items.Select(it => new PurchaseItemDto(
                it.Id, it.Description, it.ProductId, 
                it.Product?.SKU, it.Product?.NameAr, 
                it.ProductVariantId, it.ProductVariant?.Size, it.ProductVariant?.ColorAr ?? it.ProductVariant?.Color,
                it.Unit, GetMultiplier(pUnits, it.Unit), it.Quantity, it.ReturnedQuantity, it.UnitCost, it.TaxRate, it.IsTaxInclusive, it.TotalCost,
                it.Product?.Variants?.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId)).ToList()
            )).ToList(),
            inv.Payments.Select(p => new SupplierPaymentSummaryDto(
                p.Id, p.PaymentNumber, inv.Supplier.Name, inv.InvoiceNumber,
                p.PaymentDate, p.Amount, p.PaymentMethod.ToString(), p.AccountName, p.Notes,
                p.AttachmentUrl, p.AttachmentPublicId
            )).ToList(),
            inv.AttachmentUrl, inv.AttachmentPublicId,
            inv.CostCenter,
            inv.CashAccountId,
            inv.SupplierId,
            inv.CostCenter == OrderSource.Website ? "الموقع" : (inv.CostCenter == OrderSource.POS ? "المحل" : "عام"),
            inv.WarehouseId,
            inv.Warehouse?.Name
        ));

    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePurchaseInvoiceDto dto)
    {
        if (!dto.Items.Any())
            return BadRequest(new { message = _t.Get("Purchases.MinOneItemRequired") });

        foreach (var item in dto.Items)
        {
            if (item.ProductId.HasValue && !item.ProductVariantId.HasValue)
            {
                var hasVariants = await _db.ProductVariants.AnyAsync(v => v.ProductId == item.ProductId.Value);
                if (hasVariants)
                {
                    return BadRequest(new { message = _t.Get("Purchases.SizeColorRequired", item.Description) });
                }
            }
        }

        var pUnits = await GetUnitsListAsync();

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
                if (supplier == null)
                    return BadRequest(new { message = _t.Get("Purchases.SupplierNotFound") });

                var invNo = await _seq.NextAsync("PO");

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
                    InventoryAccountId    = dto.InventoryAccountId > 0 ? dto.InventoryAccountId : null,
                    ExpenseAccountId      = dto.ExpenseAccountId > 0 ? dto.ExpenseAccountId : null,
                    VatAccountId          = dto.VatAccountId > 0 ? dto.VatAccountId : null,
                    CashAccountId         = dto.CashAccountId > 0 ? dto.CashAccountId : null,
                    CostCenter            = dto.CostCenter,
                    IsTaxInclusive        = dto.IsTaxInclusive,
                    WarehouseId           = dto.WarehouseId > 0 ? dto.WarehouseId : null
                };

                var warnings = new List<string>();
                decimal subtotal = 0;
                decimal totalLineTax = 0;
                foreach (var item in dto.Items)
                {
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

                    invoice.Items.Add(new PurchaseInvoiceItem
                    {
                        Description = item.Description,
                        ProductId   = item.ProductId,
                        ProductVariantId = item.ProductVariantId,
                        Unit        = item.Unit,
                        Quantity    = item.Quantity,
                        UnitCost    = item.UnitCost,
                        TaxRate     = item.TaxRate,
                        IsTaxInclusive = item.IsTaxInclusive,
                        TotalCost   = Math.Round(item.Quantity * item.UnitCost, 2),
                        CreatedAt   = TimeHelper.GetEgyptTime()
                    });
                    subtotal += lineBase;
                    totalLineTax += lineTax;

                    if (item.ProductId.HasValue)
                    {
                        var multiplier = GetMultiplier(pUnits, item.Unit);
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Purchase, 
                            item.Quantity * multiplier, 
                            item.ProductId, 
                            item.ProductVariantId, 
                            invNo, 
                            "Purchase Invoice receipt", 
                            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                            item.UnitCost,
                            invoice.CostCenter,
                            autoSave: false,
                            warehouseId: invoice.WarehouseId
                        );
                        
                        var product = await _db.Products.FindAsync(item.ProductId.Value);
                        if (product != null)
                        {
                            var newCost = multiplier > 0 ? Math.Round(item.UnitCost / multiplier, 2) : item.UnitCost;
                        if (product.CostPrice.HasValue && product.CostPrice.Value > 0 && newCost > product.CostPrice.Value) {
                                var diff = newCost - product.CostPrice.Value;
                                var pct  = Math.Round((diff / product.CostPrice.Value) * 100, 1);
                                warnings.Add(_t.Get("Purchases.PriceIncreaseWarning", product.NameAr, pct, product.CostPrice.Value, newCost));
                            }
                            product.CostPrice = newCost;
                            product.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }
                }

                invoice.SubTotal    = Math.Round(subtotal, 2);
                
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

                invoice.TaxAmount   = Math.Round(totalLineTax + globalTax, 2);
                
                if (dto.IsTaxInclusive)
                {
                    invoice.TotalAmount = Math.Round(subtotal - dto.DiscountAmount, 2);
                }
                else
                {
                    invoice.TotalAmount = Math.Round(subtotal + globalTax + totalLineTax - dto.DiscountAmount, 2);
                }
                supplier.TotalPurchases += invoice.TotalAmount;

                // ─── Payment Handling (Multi-Method) ───
                decimal totalPaid = 0;
                if (dto.Payments != null && dto.Payments.Any())
                {
                    foreach (var p in dto.Payments)
                    {
                        if (p.Amount <= 0) continue;
                        
                        var pNo = await _seq.NextAsync("SP");
                        invoice.Payments.Add(new SupplierPayment
                        {
                            PaymentNumber = pNo,
                            SupplierId    = supplier.Id,
                            Amount        = p.Amount,
                            PaymentDate   = invoice.InvoiceDate,
                            PaymentMethod = p.Method,
                            CashAccountId = p.CashAccountId > 0 ? p.CashAccountId : null,
                            AccountName   = p.Notes ?? "سداد فاتورة",
                            Notes         = p.Notes ?? $"سداد فاتورة {invNo}",
                            CreatedAt     = TimeHelper.GetEgyptTime(),
                            CostCenter    = invoice.CostCenter
                        });
                        totalPaid += p.Amount;
                    }
                }
                else if (dto.PaymentTerms == PaymentTerms.Cash)
                {
                    // Backward compatibility / Simple Cash mode
                    decimal cashToPay = Math.Max(0, invoice.TotalAmount - dto.DeductAdvanceAmount);
                    if (cashToPay > 0)
                    {
                        totalPaid = cashToPay;
                        var pNo = await _seq.NextAsync("SP");
                        invoice.Payments.Add(new SupplierPayment
                        {
                            PaymentNumber = pNo,
                            SupplierId    = supplier.Id,
                            Amount        = cashToPay,
                            PaymentDate   = invoice.InvoiceDate,
                            PaymentMethod = PaymentMethod_Purchase.Cash,
                            AccountName   = "الخزينة (آلي)",
                            CashAccountId = dto.CashAccountId > 0 ? dto.CashAccountId : null,
                            Notes         = $"سداد تلقائي لفاتورة {invNo}",
                            CreatedAt     = TimeHelper.GetEgyptTime(),
                            CostCenter    = invoice.CostCenter
                        });
                    }
                }

                // ─── Advance Payment Deduction ───
                decimal advanceDeducted = 0;
                var newlySplitAdvancePayments = new List<SupplierPayment>();
                var advanceJournalEntriesToRemove = new List<JournalEntry>();

                if (dto.DeductAdvanceAmount > 0)
                {
                    decimal remainingToDeduct = dto.DeductAdvanceAmount;
                    var advancePayments = await _db.SupplierPayments
                        .Where(p => p.SupplierId == supplier.Id && p.PurchaseInvoiceId == null && p.Amount > 0)
                        .OrderBy(p => p.PaymentDate)
                        .ToListAsync();

                    foreach (var ap in advancePayments)
                    {
                        if (remainingToDeduct <= 0) break;

                        // Identify the old journal entry to delete it and repost it linked to the invoice
                        var oldJE = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == ap.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
                        if (oldJE != null) advanceJournalEntriesToRemove.Add(oldJE);

                        if (ap.Amount <= remainingToDeduct)
                        {
                            // Fully consumed
                            ap.PurchaseInvoiceId = invoice.Id; // Will be set after invoice save, but we link it to the object
                            invoice.Payments.Add(ap); 
                            advanceDeducted += ap.Amount;
                            remainingToDeduct -= ap.Amount;
                        }
                        else
                        {
                            // Partially consumed: split it
                            decimal remainder = ap.Amount - remainingToDeduct;
                            
                            // Consumed part becomes linked
                            ap.Amount = remainingToDeduct;
                            invoice.Payments.Add(ap);
                            
                            advanceDeducted += remainingToDeduct;
                            remainingToDeduct = 0;

                            // Remainder becomes a new unlinked advance payment
                            var pNoSplit = await _seq.NextAsync("SP");
                            var splitPayment = new SupplierPayment
                            {
                                PaymentNumber = pNoSplit,
                                SupplierId = ap.SupplierId,
                                Amount = remainder,
                                PaymentDate = ap.PaymentDate,
                                PaymentMethod = ap.PaymentMethod,
                                CashAccountId = ap.CashAccountId,
                                AccountName = ap.AccountName,
                                Notes = ap.Notes + " (Split remainder)",
                                CreatedAt = ap.CreatedAt, // retain original creation time
                                CostCenter = ap.CostCenter,
                                AttachmentUrl = ap.AttachmentUrl,
                                AttachmentPublicId = ap.AttachmentPublicId,
                                CreatedByUserId = ap.CreatedByUserId
                            };
                            _db.SupplierPayments.Add(splitPayment);
                            newlySplitAdvancePayments.Add(splitPayment);
                        }
                    }

                    if (advanceJournalEntriesToRemove.Any())
                    {
                        _db.JournalEntries.RemoveRange(advanceJournalEntriesToRemove);
                    }
                }

                invoice.PaidAmount = Math.Round(totalPaid + advanceDeducted, 2);
                supplier.TotalPaid += Math.Round(totalPaid, 2);

                // Update Status based on payment
                if (invoice.PaidAmount >= invoice.TotalAmount - 0.01M)
                    invoice.Status = PurchaseInvoiceStatus.Paid;
                else if (invoice.PaidAmount > 0)
                    invoice.Status = PurchaseInvoiceStatus.PartPaid;
                else
                    invoice.Status = PurchaseInvoiceStatus.Received;


                _db.PurchaseInvoices.Add(invoice);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _ = PostJournalWithRetryAsync(invoice.Id, invoice.InvoiceNumber, isReturn: false);

                if (newlySplitAdvancePayments.Any())
                {
                    var newlySplitIds = newlySplitAdvancePayments.Select(p => p.Id).ToList();
                    _ = Task.Run(async () => {
                        try {
                            using var scope = _scopeFactory.CreateScope();
                            var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                            var innerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var payments = await innerDb.SupplierPayments.Where(p => newlySplitIds.Contains(p.Id)).ToListAsync();
                            foreach (var sp in payments) { await acc.PostSupplierPaymentAsync(sp); }
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Failed to post journal for newly split advance payments.");
                        }
                    });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userName = User.FindFirstValue(ClaimTypes.Name);
                try { await _auditLog.LogChangeAsync<PurchaseInvoice>("CreatePurchaseInvoice", "PurchaseInvoice", invoice.Id.ToString(), null, invoice, userId, userName); } catch { }

                return CreatedAtAction(nameof(GetById), new { id = invoice.Id }, new { id = invoice.Id, invoiceNumber = invoice.InvoiceNumber, warnings });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                var fullMsg = ex.InnerException != null ? $"{ex.Message} | {ex.InnerException.Message}" : ex.Message;
                _logger.LogError(ex, "Purchase invoice creation failed. Trace: {Message}", fullMsg);
                return StatusCode(500, new { message = _t.Get("Purchases.CreationError"), details = fullMsg });
            }
        });
    }

    [HttpPut("{id}")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePurchaseInvoiceDto dto)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () => 
        {
            using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try 
            {
                var oldInvoice = await _db.PurchaseInvoices.AsNoTracking().Include(i => i.Items).Include(i => i.Supplier).FirstOrDefaultAsync(i => i.Id == id);
                var inv = await _db.PurchaseInvoices
                    .Include(i => i.Items)
                    .Include(i => i.Supplier)
                    .Include(i => i.Payments)
                    .FirstOrDefaultAsync(i => i.Id == id);

                if (inv == null) return NotFound();

                // 🚨 SECURITY CHECK: Only Admins/SuperAdmins can edit invoices that are already posted/received
                bool isAdmin = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.SuperAdmin);
                if (inv.Status != PurchaseInvoiceStatus.Draft && !isAdmin)
                {
                    return BadRequest(new { message = _t.Get("Purchases.OnlyAdminsCanEditPosted") });
                }

                if (dto.Items != null)
                {
                    foreach (var item in dto.Items)
                    {
                        if (item.ProductId.HasValue && !item.ProductVariantId.HasValue)
                        {
                            var hasVariants = await _db.ProductVariants.AnyAsync(v => v.ProductId == item.ProductId.Value);
                            if (hasVariants)
                            {
                                return BadRequest(new { message = _t.Get("Purchases.SizeColorRequiredForEdit", item.Description) });
                            }
                        }
                    }
                }

                var pUnits = await GetUnitsListAsync();

                var oldStockMap = inv.Items
                    .Where(x => x.ProductId.HasValue)
                    .GroupBy(x => new { x.ProductId, x.ProductVariantId })
                    .ToDictionary(g => g.Key, g => g.Sum(x => (int)Math.Round(x.Quantity * GetMultiplier(pUnits, x.Unit))));

                inv.Supplier.TotalPurchases -= inv.TotalAmount;

                // Use the global DTO properties
                inv.PaymentTerms = dto.PaymentTerms;
                inv.InvoiceDate = dto.InvoiceDate;
                inv.DueDate = dto.DueDate;
                inv.TaxPercent = dto.TaxPercent;
                inv.DiscountAmount = dto.DiscountAmount;
                inv.Notes = dto.Notes;
                inv.CostCenter = dto.CostCenter;
                if (dto.SupplierInvoiceNumber != null) inv.SupplierInvoiceNumber = dto.SupplierInvoiceNumber;
                if (dto.CashAccountId.HasValue && dto.CashAccountId.Value > 0) inv.CashAccountId = dto.CashAccountId.Value;
                if (dto.VendorAccountId.HasValue && dto.VendorAccountId.Value > 0) inv.VendorAccountId = dto.VendorAccountId.Value;
                if (dto.InventoryAccountId.HasValue && dto.InventoryAccountId.Value > 0) inv.InventoryAccountId = dto.InventoryAccountId.Value;
                if (dto.VatAccountId.HasValue && dto.VatAccountId.Value > 0) inv.VatAccountId = dto.VatAccountId.Value;
                inv.IsTaxInclusive = dto.IsTaxInclusive;
                inv.WarehouseId = dto.WarehouseId > 0 ? dto.WarehouseId : null;

                _db.PurchaseInvoiceItems.RemoveRange(inv.Items);
                inv.Items.Clear();

                decimal subtotal = 0;
                decimal totalLineTax = 0;
                if (dto.Items != null)
                {
                    foreach (var item in dto.Items)
                    {
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

                        inv.Items.Add(new PurchaseInvoiceItem
                        {
                            ProductId = item.ProductId,
                            ProductVariantId = item.ProductVariantId,
                            Description = item.Description,
                            Unit = item.Unit ?? "Unit",
                            Quantity = item.Quantity,
                            UnitCost = item.UnitCost,
                            TaxRate = item.TaxRate,
                            IsTaxInclusive = item.IsTaxInclusive,
                            TotalCost = Math.Round(item.Quantity * item.UnitCost, 2)
                        });
                        subtotal += lineBase;
                        totalLineTax += lineTax;
                    }
                }

                inv.SubTotal = Math.Round(subtotal, 2);
                
                decimal globalTax = 0;
                if (dto.TaxPercent > 0)
                {
                    var baseForGlobal = subtotal - inv.DiscountAmount;
                    if (inv.IsTaxInclusive)
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

                inv.TaxAmount = Math.Round(totalLineTax + globalTax, 2);
                
                if (inv.IsTaxInclusive)
                {
                    inv.TotalAmount = Math.Round(subtotal - inv.DiscountAmount, 2);
                }
                else
                {
                    inv.TotalAmount = Math.Round(subtotal + globalTax + totalLineTax - inv.DiscountAmount, 2);
                }
                
                // ─── Reset/Clear Existing Payments (to avoid double-payment & duplicates on edit) ───
                 var newlyUnlinkedPayments = new List<SupplierPayment>();
                 var oldPayments = inv.Payments.ToList();
                 foreach (var op in oldPayments)
                 {
                     // Remove journal entries associated with the payment
                     var pEntries = await _db.JournalEntries
                         .Where(e => e.Reference == op.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher)
                         .ToListAsync();
                     if (pEntries.Any()) _db.JournalEntries.RemoveRange(pEntries);

                     if (op.CreatedAt < inv.CreatedAt.AddSeconds(-2))
                     {
                         // Unlink advance payment
                         op.PurchaseInvoiceId = null;
                         newlyUnlinkedPayments.Add(op);
                     }
                     else
                     {
                         // Revert supplier total paid only for invoice-specific payments
                         inv.Supplier.TotalPaid -= op.Amount;
                         // Delete invoice-specific payment
                         _db.SupplierPayments.Remove(op);
                     }
                     inv.Payments.Remove(op);
                 }
                 inv.PaidAmount = 0;

                 // --- Sync Payment ---
                 decimal totalNewPaid = 0;
                 if (inv.PaymentTerms == PaymentTerms.Cash)
                 {
                     decimal cashToPay = Math.Max(0, inv.TotalAmount - dto.DeductAdvanceAmount);
                     totalNewPaid = cashToPay;

                     if (cashToPay > 0)
                     {
                         var pNo = await _seq.NextAsync("SP");
                         var newPm = new SupplierPayment
                         {
                             PaymentNumber = pNo,
                             SupplierId = inv.SupplierId,
                             Amount = cashToPay,
                             PaymentDate = inv.InvoiceDate,
                             PaymentMethod = PaymentMethod_Purchase.Cash,
                             AccountName = "الخزينة (آلي)",
                             Notes = $"سداد تلقائي لفاتورة {inv.InvoiceNumber}",
                             CreatedAt = TimeHelper.GetEgyptTime(),
                             CostCenter = inv.CostCenter,
                             CashAccountId = inv.CashAccountId > 0 ? inv.CashAccountId : null
                         };
                         inv.Payments.Add(newPm);
                     }
                 }
                 else 
                 {
                     // ─── Process new Payments (Credit mode) ───
                     if (dto.Payments != null && dto.Payments.Any())
                     {
                         foreach (var pmDto in dto.Payments)
                         {
                             if (pmDto.Amount <= 0) continue;
                             var pNo = await _seq.NextAsync("SP");
                             var newPm = new SupplierPayment
                             {
                                 PaymentNumber = pNo,
                                 SupplierId = inv.SupplierId,
                                 PurchaseInvoiceId = inv.Id,
                                 Amount = pmDto.Amount,
                                 PaymentDate = inv.InvoiceDate,
                                 PaymentMethod = pmDto.Method,
                                 CashAccountId = pmDto.CashAccountId > 0 ? pmDto.CashAccountId : null,
                                 Notes = pmDto.Notes ?? $"دفعة لفاتورة {inv.InvoiceNumber}",
                                 CreatedAt = TimeHelper.GetEgyptTime(),
                                 CostCenter = inv.CostCenter
                             };
                             inv.Payments.Add(newPm);
                             totalNewPaid += pmDto.Amount;
                         }
                     }
                 }

                 inv.PaidAmount = totalNewPaid;
                 inv.Supplier.TotalPaid += totalNewPaid;

                 // ─── Process Advance Deduction (Both Cash and Credit) ───
                 decimal advanceDeducted = 0;
                 if (dto.DeductAdvanceAmount > 0)
                 {
                     decimal remainingToDeduct = dto.DeductAdvanceAmount;
                     var advancePayments = await _db.SupplierPayments
                         .Where(p => p.SupplierId == inv.SupplierId && p.PurchaseInvoiceId == null && p.Amount > 0)
                         .OrderBy(p => p.PaymentDate)
                         .ToListAsync();

                     foreach (var ap in advancePayments)
                     {
                         if (remainingToDeduct <= 0) break;

                         var oldJE = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Reference == ap.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher);
                         if (oldJE != null) _db.JournalEntries.Remove(oldJE);

                         if (ap.Amount <= remainingToDeduct)
                         {
                             ap.PurchaseInvoiceId = inv.Id;
                             advanceDeducted += ap.Amount;
                             remainingToDeduct -= ap.Amount;
                         }
                         else
                         {
                             decimal remainder = ap.Amount - remainingToDeduct;
                             ap.Amount = remainingToDeduct;
                             ap.PurchaseInvoiceId = inv.Id;
                             advanceDeducted += remainingToDeduct;
                             remainingToDeduct = 0;

                             var pNoSplit = await _seq.NextAsync("SP");
                             _db.SupplierPayments.Add(new SupplierPayment
                             {
                                 PaymentNumber = pNoSplit,
                                 SupplierId = ap.SupplierId,
                                 Amount = remainder,
                                 PaymentDate = ap.PaymentDate,
                                 PaymentMethod = ap.PaymentMethod,
                                 CashAccountId = ap.CashAccountId,
                                 AccountName = ap.AccountName,
                                 Notes = ap.Notes + " (Split remainder)",
                                 CreatedAt = ap.CreatedAt,
                                 CostCenter = ap.CostCenter,
                             });
                         }
                     }
                     inv.PaidAmount += advanceDeducted;
                 }

                 // If it's credit but was partially paid before, check if it's now fully paid or partially
                 if (inv.PaidAmount >= inv.TotalAmount - 0.001M)
                     inv.Status = PurchaseInvoiceStatus.Paid;
                 else if (inv.PaidAmount > 0)
                     inv.Status = PurchaseInvoiceStatus.PartPaid;
                 else
                     inv.Status = PurchaseInvoiceStatus.Received;

                // ——— Auto-update and Alert on Price Changes ———
                var warnings = new List<string>();
                foreach (var item in inv.Items.Where(i => i.ProductId.HasValue))
                {
                    var product = await _db.Products.FindAsync(item.ProductId!.Value);
                    if (product != null)
                    {
                        var multiplier = GetMultiplier(pUnits, item.Unit);
                        var newCost = multiplier > 0 ? Math.Round(item.UnitCost / multiplier, 2) : item.UnitCost;
                        if (product.CostPrice.HasValue && product.CostPrice.Value > 0 && newCost > product.CostPrice.Value) {
                            var diff = newCost - product.CostPrice.Value;
                            var pct  = Math.Round((diff / product.CostPrice.Value) * 100, 1);
                            warnings.Add(_t.Get("Purchases.PriceIncreaseWarning", product.NameAr, pct, product.CostPrice.Value, newCost));
                        }
                        product.CostPrice = newCost;
                        product.UpdatedAt = TimeHelper.GetEgyptTime();
                    }
                }

                await _db.SaveChangesAsync();

                var newStockMap = inv.Items
                    .Where(x => x.ProductId.HasValue)
                    .GroupBy(x => new { x.ProductId, x.ProductVariantId })
                    .ToDictionary(g => g.Key, g => g.Sum(x => (int)Math.Round(x.Quantity * GetMultiplier(pUnits, x.Unit))));

                var keys = oldStockMap.Keys.Union(newStockMap.Keys).Distinct();
                foreach (var key in keys)
                {
                    var oldQty = oldStockMap.GetValueOrDefault(key, 0);
                    var newQty = newStockMap.GetValueOrDefault(key, 0);
                    var diff = newQty - oldQty;
                    if (diff != 0)
                    {
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.Adjustment, 
                            diff, 
                            key.ProductId, 
                            key.ProductVariantId, 
                            inv.InvoiceNumber, 
                            $"Edit Inv #{inv.InvoiceNumber}", 
                            User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                            0, // unitCost fallback
                            inv.CostCenter,
                            autoSave: false,
                            ignoreIdempotency: true,
                            warehouseId: inv.WarehouseId
                        );
                    }
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                _ = PostJournalWithRetryAsync(id, inv.InvoiceNumber, isReturn: false);

                if (newlyUnlinkedPayments.Any())
                {
                    var unlinkedIds = newlyUnlinkedPayments.Select(p => p.Id).ToList();
                    _ = Task.Run(async () => {
                        try {
                            using var scope = _scopeFactory.CreateScope();
                            var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                            var innerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                            var payments = await innerDb.SupplierPayments.Where(p => unlinkedIds.Contains(p.Id)).ToListAsync();
                            foreach (var sp in payments) { await acc.PostSupplierPaymentAsync(sp); }
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Failed to post journal for newly unlinked advance payments on update.");
                        }
                    });
                }

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userName = User.FindFirstValue(ClaimTypes.Name);
                try { await _auditLog.LogChangeAsync<PurchaseInvoice>("UpdatePurchaseInvoice", "PurchaseInvoice", inv.Id.ToString(), oldInvoice, inv, userId, userName); } catch { }

                return Ok(new { id = inv.Id, invoiceNumber = inv.InvoiceNumber, warnings });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Update Purchase Invoice failed: {Message}", ex.Message);
                return StatusCode(500, new { message = _t.Get("Purchases.UpdateError"), details = ex.Message });
            }
        });
    }

    [AcceptVerbs("PATCH", "PUT")]
    [Route("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdatePurchaseStatusDto dto)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Payments)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        var oldStatus = inv.Status;
        if (oldStatus == dto.Status) return Ok(new { id = inv.Id, status = inv.Status.ToString() });
        
        var oldInvoice = await _db.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);

        // ——— 1. Handle Stock Reversal (If moving FROM received TO inactive) ———
        bool wasInStock = (oldStatus == PurchaseInvoiceStatus.Received || oldStatus == PurchaseInvoiceStatus.Paid || oldStatus == PurchaseInvoiceStatus.PartPaid);
        bool willBeOut  = (dto.Status == PurchaseInvoiceStatus.Cancelled || dto.Status == PurchaseInvoiceStatus.Returned);

        var pUnits = await GetUnitsListAsync();

        if (wasInStock && willBeOut)
        {
            foreach (var item in inv.Items)
            {
                if (item.ProductId.HasValue)
                {
                    var actualQty = (int)Math.Round(item.Quantity * GetMultiplier(pUnits, item.Unit));
                    await _inventory.LogMovementAsync(
                        dto.Status == PurchaseInvoiceStatus.Returned ? InventoryMovementType.ReturnOut : InventoryMovementType.Adjustment,
                        -actualQty, // Reverse the previous increase
                        item.ProductId,
                        item.ProductVariantId,
                        inv.InvoiceNumber,
                        $"Purchase status changed: {oldStatus} -> {dto.Status}",
                        User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        item.UnitCost,
                        inv.CostCenter,
                        ignoreIdempotency: true,
                        warehouseId: inv.WarehouseId
                    );
                }
            }
        }
        // Case: If moving FROM Draft/Cancelled TO Received/Paid (Re-activating)
        else if (!wasInStock && (dto.Status == PurchaseInvoiceStatus.Received || dto.Status == PurchaseInvoiceStatus.Paid))
        {
             foreach (var item in inv.Items)
             {
                if (item.ProductId.HasValue)
                {
                    var actualQty = (int)Math.Round(item.Quantity * GetMultiplier(pUnits, item.Unit));
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.Purchase,
                        actualQty,
                        item.ProductId,
                        item.ProductVariantId,
                        inv.InvoiceNumber,
                        $"Purchase status activated: {dto.Status}",
                        User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                        item.UnitCost,
                        inv.CostCenter,
                        ignoreIdempotency: true,
                        warehouseId: inv.WarehouseId
                    );
                }
             }
        }

        // ——— 2. Handle Accounting & Totals ———
        if (dto.Status == PurchaseInvoiceStatus.Cancelled)
        {
            // Reverse Supplier Totals (Net remaining of the invoice)
            inv.Supplier.TotalPurchases -= (inv.TotalAmount - inv.ReturnedAmount);
            decimal standardPaidCancel = inv.Payments.Where(p => p.CreatedAt >= inv.CreatedAt.AddSeconds(-2)).Sum(p => p.Amount);
            inv.Supplier.TotalPaid -= standardPaidCancel;

            var journalEntry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference == inv.InvoiceNumber);
            if (journalEntry != null) { 
                _db.JournalEntries.Remove(journalEntry);
            }

            var newlyUnlinkedPaymentsCancel = new List<SupplierPayment>();
            foreach (var p in inv.Payments.ToList())
            {
                var pEntries = await _db.JournalEntries.Where(e => e.Reference == p.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher).ToListAsync();
                if (pEntries.Any()) _db.JournalEntries.RemoveRange(pEntries);

                if (p.CreatedAt < inv.CreatedAt.AddSeconds(-2))
                {
                    p.PurchaseInvoiceId = null;
                    newlyUnlinkedPaymentsCancel.Add(p);
                }
                else
                {
                    _db.SupplierPayments.Remove(p);
                }
            }

            if (newlyUnlinkedPaymentsCancel.Any())
            {
                var unlinkedIds = newlyUnlinkedPaymentsCancel.Select(p => p.Id).ToList();
                _ = Task.Run(async () => {
                    try {
                        using var scope = _scopeFactory.CreateScope();
                        var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                        var innerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var payments = await innerDb.SupplierPayments.Where(p => unlinkedIds.Contains(p.Id)).ToListAsync();
                        foreach (var sp in payments) { await acc.PostSupplierPaymentAsync(sp); }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Failed to post journal for newly unlinked advance payments on cancellation.");
                    }
                });
            }
        }

        if (dto.Status == PurchaseInvoiceStatus.Returned)
        {
            _ = PostJournalWithRetryAsync(id, inv.InvoiceNumber, isReturn: true);
        }

        inv.Status = dto.Status;
        inv.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.FindFirstValue(ClaimTypes.Name);
        try { await _auditLog.LogChangeAsync<PurchaseInvoice>("UpdatePurchaseInvoiceStatus", "PurchaseInvoice", id.ToString(), oldInvoice, inv, userId, userName); } catch { }

        return Ok(new { id = inv.Id, status = inv.Status.ToString() });
    }

    [HttpDelete("{id}")]
    [RequirePermission(ModuleKeys.PurchasesMain, requireEdit: true)]
    public async Task<IActionResult> Delete(int id)
    {
        var inv = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items).ThenInclude(it => it.Product)
            .Include(i => i.Items).ThenInclude(it => it.ProductVariant)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null) return NotFound();

        // 🛡️ SECURITY GUARD: Only Admin/SuperAdmin can delete posted invoices
        if (inv.Status != PurchaseInvoiceStatus.Draft && (int)inv.Status != 0)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }
        }

        // 1. التحقق من وجود مرتجعات (Check for Returns)
        var hasReturns = await _db.PurchaseReturns.AnyAsync(r => r.PurchaseInvoiceId == id);
        if (hasReturns || inv.ReturnedAmount > 0)
        {
            return BadRequest(new { message = _t.Get("Purchases.CannotDeleteWithReturns") });
        }

        // 2. التحقق مما إذا كان قد تم بيع أي جزء من الفاتورة (Check if items were sold)
        var pUnits = await GetUnitsListAsync();
        var forceMap = new Dictionary<int, bool>();
        bool shouldAdjustStock = inv.Status == PurchaseInvoiceStatus.Received 
            || inv.Status == PurchaseInvoiceStatus.Paid 
            || inv.Status == PurchaseInvoiceStatus.PartPaid 
            || inv.Status == PurchaseInvoiceStatus.Overdue
            || inv.Status == PurchaseInvoiceStatus.PartiallyReturned;

        if (shouldAdjustStock)
        {
            foreach (var item in inv.Items)
            {
                if (item.ProductId.HasValue)
                {
                    var mult = GetMultiplier(pUnits, item.Unit);
                    var qtyInPieces = item.Quantity * mult;
                    
                    int currentStock;
                    if (item.ProductVariantId.HasValue && inv.WarehouseId.HasValue)
                    {
                        currentStock = await _db.ProductWarehouseStocks
                            .Where(pws => pws.ProductVariantId == item.ProductVariantId.Value && pws.WarehouseId == inv.WarehouseId.Value)
                            .Select(pws => pws.Quantity)
                            .FirstOrDefaultAsync();
                    }
                    else
                    {
                        currentStock = await _inventory.GetCurrentStockAsync(item.ProductId, item.ProductVariantId);
                    }

                    if (currentStock < qtyInPieces)
                    {
                        // Check if subsequent outgoing movements have occurred since the invoice was created/movement logged
                        bool hasSubsequentOutgoing = false;
                        var purchaseMovement = await _db.InventoryMovements
                            .Where(m => m.Type == InventoryMovementType.Purchase 
                                     && m.Reference == inv.InvoiceNumber 
                                     && m.ProductId == item.ProductId 
                                     && m.ProductVariantId == item.ProductVariantId)
                            .FirstOrDefaultAsync();

                        DateTime cutoffTime = purchaseMovement?.CreatedAt ?? inv.CreatedAt;
                        var subsequentMovements = await _db.InventoryMovements
                            .Where(m => m.ProductId == item.ProductId 
                                     && m.ProductVariantId == item.ProductVariantId 
                                     && m.CreatedAt > cutoffTime 
                                     && (m.Type == InventoryMovementType.Sale 
                                      || m.Type == InventoryMovementType.TransferOut 
                                      || m.Type == InventoryMovementType.ReturnOut))
                            .ToListAsync();

                        foreach (var m in subsequentMovements)
                        {
                            if (inv.WarehouseId.HasValue && m.WarehouseId != inv.WarehouseId.Value)
                            {
                                continue;
                            }

                            if (m.Type == InventoryMovementType.Sale)
                            {
                                var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderNumber == m.Reference);
                                if (order != null && order.Status != OrderStatus.Cancelled)
                                {
                                    hasSubsequentOutgoing = true;
                                    break;
                                }
                            }
                            else if (m.Type == InventoryMovementType.TransferOut)
                            {
                                var transfer = await _db.StockTransfers.FirstOrDefaultAsync(t => t.TransferNumber == m.Reference);
                                if (transfer != null && transfer.Status != StockTransferStatus.Cancelled)
                                {
                                    hasSubsequentOutgoing = true;
                                    break;
                                }
                            }
                            else if (m.Type == InventoryMovementType.ReturnOut)
                            {
                                var returnObj = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.ReturnNumber == m.Reference);
                                if (returnObj != null)
                                {
                                    hasSubsequentOutgoing = true;
                                    break;
                                }
                            }
                        }

                        if (hasSubsequentOutgoing)
                        {
                            var productName = item.Product?.NameAr ?? item.Description;
                            return BadRequest(new { message = _t.Get("Purchases.CannotDeleteInvoiceUsedInSales", productName, qtyInPieces, currentStock) });
                        }
                        else
                        {
                            forceMap[item.Id] = true;
                        }
                    }
                    else
                    {
                        forceMap[item.Id] = false;
                    }
                }
            }
        }

        inv.Supplier.TotalPurchases -= (inv.TotalAmount - inv.ReturnedAmount);
        decimal standardPaidDelete = inv.Payments.Where(p => p.CreatedAt >= inv.CreatedAt.AddSeconds(-2)).Sum(p => p.Amount);
        inv.Supplier.TotalPaid -= standardPaidDelete;

        if (shouldAdjustStock)
        {
            foreach (var item in inv.Items)
            {
                if (item.ProductId.HasValue)
                {
                    var mult = GetMultiplier(pUnits, item.Unit);
                    bool forceDelete = forceMap.GetValueOrDefault(item.Id, false);
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.Adjustment, 
                        -(item.Quantity * mult), 
                        item.ProductId, 
                        item.ProductVariantId, 
                        inv.InvoiceNumber, 
                        "Purchase Invoice Deleted", 
                        User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, 
                        ignoreIdempotency: true, 
                        force: forceDelete,
                        warehouseId: inv.WarehouseId
                    );
                }
            }
        }

        // 3. الشامل: حذف جميع القيود المحاسبية المرتبطة بالفاتورة ومدفوعاتها
        var newlyUnlinkedPaymentsDelete = new List<SupplierPayment>();
        foreach (var p in inv.Payments.ToList())
        {
            var pEntries = await _db.JournalEntries.Where(e => e.Reference == p.PaymentNumber && e.Type == JournalEntryType.PaymentVoucher).ToListAsync();
            if (pEntries.Any()) _db.JournalEntries.RemoveRange(pEntries);
            
            if (p.CreatedAt < inv.CreatedAt.AddSeconds(-2))
            {
                p.PurchaseInvoiceId = null;
                newlyUnlinkedPaymentsDelete.Add(p);
            }
            else
            {
                _db.SupplierPayments.Remove(p);
            }
        }

        if (newlyUnlinkedPaymentsDelete.Any())
        {
            var unlinkedIds = newlyUnlinkedPaymentsDelete.Select(p => p.Id).ToList();
            _ = Task.Run(async () => {
                try {
                    using var scope = _scopeFactory.CreateScope();
                    var acc = scope.ServiceProvider.GetRequiredService<IAccountingService>();
                    var innerDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var payments = await innerDb.SupplierPayments.Where(p => unlinkedIds.Contains(p.Id)).ToListAsync();
                    foreach (var sp in payments) { await acc.PostSupplierPaymentAsync(sp); }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to post journal for newly unlinked advance payments on deletion.");
                }
            });
        }

        var invoiceEntries = await _db.JournalEntries.Where(e => (e.Type == JournalEntryType.PurchaseInvoice || e.Type == JournalEntryType.OpeningBalance) && e.Reference == inv.InvoiceNumber).ToListAsync();
        if (invoiceEntries.Any()) _db.JournalEntries.RemoveRange(invoiceEntries);

        var invoiceNumber = inv.InvoiceNumber;
        _db.PurchaseInvoices.Remove(inv);
        await _db.SaveChangesAsync();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.FindFirstValue(ClaimTypes.Name);
        try { await _auditLog.LogChangeAsync<PurchaseInvoice>("DeletePurchaseInvoice", "PurchaseInvoice", id.ToString(), inv, null, userId, userName); } catch { }

        return NoContent();
    }

    /// <summary>
    /// Posts the accounting journal for a purchase invoice in the background.
    // GET /api/purchaseinvoices/{id}/pdf
    // =================================================================================
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var invoice = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();

        var pdfBytes = await _pdf.GeneratePurchaseInvoicePdfAsync(invoice);
        return File(pdfBytes, "application/pdf", $"purchase-{invoice.InvoiceNumber}.pdf");
    }

    /// Retries up to 3 times with exponential back-off before giving up and logging an error.
    /// </summary>
    private async Task PostJournalWithRetryAsync(int invoiceId, string invoiceNumber, bool isReturn)
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

                if (isReturn)
                    await acc.PostPurchaseReturnAsync(inv);
                else
                {
                    await acc.PostPurchaseInvoiceAsync(inv);
                    
                    // ✅ NEW: Post all associated payments to clear liability
                    if (inv.Payments != null && inv.Payments.Any())
                    {
                        foreach (var p in inv.Payments)
                        {
                            await acc.PostSupplierPaymentAsync(p);
                        }
                    }
                }

                return; // success
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "[Accounting] Journal posting attempt {Attempt}/{Max} failed for invoice {Number}. Retrying...",
                    attempt, maxAttempts, invoiceNumber);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Accounting] Journal posting permanently failed for invoice {Number} after {Max} attempts.",
                    invoiceNumber, maxAttempts);
            }
        }
    }
}


