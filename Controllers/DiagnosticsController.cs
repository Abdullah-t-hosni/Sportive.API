using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Sportive.API.Interfaces.IDataMaintenanceService _maintenance;

    public DiagnosticsController(AppDbContext db, Sportive.API.Interfaces.IDataMaintenanceService maintenance)
    {
        _db = db;
        _maintenance = maintenance;
    }

    [HttpGet("trigger-recalculate-stock")]
    public async Task<IActionResult> TriggerRecalculateStock()
    {
        var (success, message) = await _maintenance.RecalculateStockAsync();
        return Ok(new { success, message });
    }

    [HttpGet("inspect-skus")]
    public async Task<IActionResult> InspectSkus()
    {
        var skus = new[] { "2212", "2214", "2074" };
        var variants = await _db.ProductVariants
            .Include(v => v.Product)
            .Where(v => skus.Contains(v.Product.SKU) || skus.Contains(v.Product.Id.ToString()))
            .Select(v => new {
                v.Id,
                v.ProductId,
                ProductSku = v.Product.SKU,
                ProductName = v.Product.NameAr,
                VariantSize = v.Size,
                VariantColor = v.Color,
                VariantColorAr = v.ColorAr
            })
            .ToListAsync();
            
        var orderItems = await _db.OrderItems
            .Include(i => i.Order)
            .Where(i => i.Order.OrderNumber == "SPT-2608-0028")
            .Select(i => new {
                i.Id,
                i.ProductId,
                i.ProductVariantId,
                i.Size,
                i.Color,
                i.ProductNameAr
            })
            .ToListAsync();

        return Ok(new { variants, orderItems });
    }

    [HttpGet("orphaned-movements")]
    public async Task<IActionResult> GetOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && m.Reference.StartsWith("ORD-") // Or whatever order numbers start with, actually let's just do an Except or Where !Contains
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        return Ok(new
        {
            Count = orphanedSales.Count,
            Movements = orphanedSales.Select(m => new
            {
                m.Id,
                m.ProductId,
                m.ProductVariantId,
                m.Quantity,
                m.Reference,
                Type = m.Type.ToString(),
                m.CreatedAt
            })
        });
    }

    [HttpPost("cleanup-orphaned-movements")]
    public async Task<IActionResult> CleanupOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        // Include "POS" prefixed orders or "ORD" if needed. We just check if Reference looks like an order
        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        _db.InventoryMovements.RemoveRange(orphanedSales);
        await _db.SaveChangesAsync();

        // Run Recalculate Stock logic for affected products
        // We will just do the simple stock sums update like DataMaintenanceService does.
        var affectedProductIds = orphanedSales.Where(m => m.ProductId.HasValue).Select(m => m.ProductId!.Value).Distinct().ToList();
        var affectedVariantIds = orphanedSales.Where(m => m.ProductVariantId.HasValue).Select(m => m.ProductVariantId!.Value).Distinct().ToList();

        return Ok(new
        {
            DeletedCount = orphanedSales.Count,
            AffectedProductIds = affectedProductIds,
            AffectedVariantIds = affectedVariantIds,
            Message = "Deleted successfully. Please run recalculate-stock from UI to update product Stock."
        });
    }

    [HttpGet("po-diagnostics")]
    public async Task<IActionResult> GetPoDiagnostics()
    {
        var invoice = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == "PO-2607-0004");

        if (invoice == null)
        {
            return NotFound(new { message = "Purchase Invoice PO-2607-0004 not found" });
        }

        var payments = await _db.SupplierPayments
            .Where(p => p.SupplierId == invoice.SupplierId || p.PurchaseInvoiceId == invoice.Id)
            .ToListAsync();

        var entryRefs = payments.Select(p => p.PaymentNumber).Concat(new[] { invoice.InvoiceNumber }).ToList();
        
        var entries = await _db.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => (e.Reference != null && entryRefs.Contains(e.Reference)) || e.PurchaseInvoiceId == invoice.Id)
            .ToListAsync();

        return Ok(new
        {
            Invoice = new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                SupplierName = invoice.Supplier?.Name,
                invoice.SupplierId,
                invoice.TotalAmount,
                invoice.PaidAmount,
                invoice.RemainingAmount,
                Status = invoice.Status.ToString(),
                Terms = invoice.PaymentTerms.ToString()
            },
            Payments = payments.Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                p.Amount,
                p.PurchaseInvoiceId,
                PaymentMethod = p.PaymentMethod.ToString(),
                p.Notes
            })
        });
    }

    [HttpGet("sync-order-receipts")]
    public async Task<IActionResult> SyncOrderReceipts([FromQuery] string orderNumbers, [FromServices] Sportive.API.Services.IAccountingService accounting)
    {
        var numbers = orderNumbers.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
        var orders = await _db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Payments)
            .Where(o => numbers.Contains(o.OrderNumber))
            .ToListAsync();
        
        int count = 0;
        foreach (var order in orders)
        {
            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                await accounting.PostOrderPaymentAsync(order);
                count++;
            }
        }

        return Ok(new { synced = count, orders = orders.Select(o => o.OrderNumber) });
    }

    [HttpGet("inspect-product/{q}")]
    public async Task<IActionResult> GetProductDiagnostics(string q)
    {
        bool isInt = int.TryParse(q, out int id);
        var product = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .FirstOrDefaultAsync(p => (isInt && p.Id == id) || p.SKU.ToLower() == q.Trim().ToLower() || p.NameAr.Contains(q) || p.NameEn.Contains(q));

        if (product == null)
            return NotFound(new { message = $"Product with query '{q}' not found" });

        var variantIds = product.Variants.Select(v => v.Id).ToList();

        var whStocks = await _db.ProductWarehouseStocks
            .Include(w => w.Warehouse)
            .Where(w => variantIds.Contains(w.ProductVariantId))
            .Select(w => new { w.Id, w.ProductVariantId, w.WarehouseId, WarehouseName = w.Warehouse.Name, w.Quantity, w.UpdatedAt })
            .ToListAsync();

        var movements = await _db.InventoryMovements
            .Include(m => m.Warehouse)
            .Where(m => m.ProductId == product.Id || (m.ProductVariantId.HasValue && variantIds.Contains(m.ProductVariantId.Value)))
            .OrderByDescending(m => m.CreatedAt)
            .Take(30)
            .Select(m => new { m.Id, Type = m.Type.ToString(), m.Quantity, m.ProductVariantId, m.WarehouseId, WarehouseName = m.Warehouse != null ? m.Warehouse.Name : null, m.Reference, m.Note, m.CreatedAt })
            .ToListAsync();

        var warehouses = await _db.Warehouses.Select(w => new { w.Id, w.Name, w.IsActive, w.BranchId }).ToListAsync();

        return Ok(new
        {
            Product = new
            {
                product.Id,
                product.NameAr,
                product.NameEn,
                product.SKU,
                Status = product.Status.ToString(),
                product.TotalStock,
                product.Price,
                product.DiscountPrice,
                CategoryName = product.Category?.NameAr,
                BrandName = product.Brand?.NameAr,
            },
            Variants = product.Variants.Select(v => new
            {
                v.Id,
                v.Size,
                v.Color,
                v.ColorAr,
                v.StockQuantity,
                v.ReorderLevel
            }),
            WarehouseStocks = whStocks,
            Warehouses = warehouses,
            RecentMovements = movements
        });
    }

    [HttpGet("audit-duplicate-returns")]
    public async Task<IActionResult> AuditDuplicateReturns()
    {
        // 1. Fetch all ReturnIn inventory movements with non-null Reference
        var returnMovements = await _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => m.Type == InventoryMovementType.ReturnIn && !string.IsNullOrEmpty(m.Reference))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        // 2. Fetch linked orders
        var orderNumbers = returnMovements.Select(m => m.Reference).Distinct().ToList();
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => orderNumbers.Contains(o.OrderNumber))
            .ToDictionaryAsync(o => o.OrderNumber, o => o);

        var auditResults = new List<object>();

        // Group by Order Reference + Product + Variant
        var grouped = returnMovements.GroupBy(m => new { m.Reference, m.ProductId, m.ProductVariantId });

        foreach (var g in grouped)
        {
            orders.TryGetValue(g.Key.Reference!, out var order);
            int soldQuantity = 0;
            if (order != null)
            {
                soldQuantity = order.Items
                    .Where(i => i.ProductId == g.Key.ProductId && i.ProductVariantId == g.Key.ProductVariantId)
                    .Sum(i => i.Quantity);
            }

            int totalReturnQtyInLogs = g.Sum(m => m.Quantity);

            // If logged ReturnIn > soldQuantity OR there are multiple ReturnIn movement records for a single-item return
            if (g.Count() > 1 || totalReturnQtyInLogs > soldQuantity)
            {
                var firstProduct = g.First().Product;
                var firstVariant = g.First().ProductVariant;

                int excessQty = Math.Max(0, totalReturnQtyInLogs - (soldQuantity > 0 ? soldQuantity : 1));
                if (g.Count() > 1 && excessQty == 0)
                {
                    // Case where multiple movements add up to sold quantity, but were logged separately
                    // Check if duplicate entries were created within 60 seconds
                    var timestamps = g.Select(m => m.CreatedAt).OrderBy(t => t).ToList();
                    bool hasCloseDuplicates = false;
                    for (int i = 0; i < timestamps.Count - 1; i++)
                    {
                        if ((timestamps[i + 1] - timestamps[i]).TotalSeconds < 60)
                        {
                            hasCloseDuplicates = true;
                            break;
                        }
                    }
                    if (hasCloseDuplicates)
                    {
                        excessQty = g.Skip(1).Sum(m => m.Quantity);
                    }
                }

                if (excessQty > 0 || g.Count() > 1)
                {
                    auditResults.Add(new
                    {
                        OrderNumber = g.Key.Reference,
                        ProductId = g.Key.ProductId,
                        ProductName = firstProduct?.NameAr ?? firstProduct?.NameEn ?? "Unknown Product",
                        VariantId = g.Key.ProductVariantId,
                        VariantSize = firstVariant?.Size,
                        VariantColor = firstVariant?.ColorAr ?? firstVariant?.Color,
                        SoldQuantity = soldQuantity,
                        TotalReturnLogsCount = g.Count(),
                        TotalReturnQtyInLogs = totalReturnQtyInLogs,
                        ExcessDuplicateQty = excessQty,
                        MovementIds = g.Select(m => m.Id).ToList(),
                        MovementDates = g.Select(m => m.CreatedAt).ToList()
                    });
                }
            }
        }

        return Ok(new
        {
            TotalDuplicateCases = auditResults.Count,
            AffectedOrdersCount = auditResults.Select(r => (r as dynamic).OrderNumber).Distinct().Count(),
            DuplicateDetails = auditResults
        });
    }

    [HttpPost("fix-duplicate-returns")]
    public async Task<IActionResult> FixDuplicateReturns([FromServices] Sportive.API.Interfaces.IInventoryService inventory)
    {
        var returnMovements = await _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => m.Type == InventoryMovementType.ReturnIn && !string.IsNullOrEmpty(m.Reference))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var orderNumbers = returnMovements.Select(m => m.Reference).Distinct().ToList();
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => orderNumbers.Contains(o.OrderNumber))
            .ToDictionaryAsync(o => o.OrderNumber, o => o);

        var fixedResults = new List<object>();
        var grouped = returnMovements.GroupBy(m => new { m.Reference, m.ProductId, m.ProductVariantId });

        foreach (var g in grouped)
        {
            orders.TryGetValue(g.Key.Reference!, out var order);
            int soldQuantity = order != null 
                ? order.Items.Where(i => i.ProductId == g.Key.ProductId && i.ProductVariantId == g.Key.ProductVariantId).Sum(i => i.Quantity)
                : 0;

            int totalReturnQtyInLogs = g.Sum(m => m.Quantity);

            if (g.Count() > 1 || totalReturnQtyInLogs > soldQuantity)
            {
                int maxAllowedReturn = soldQuantity > 0 ? soldQuantity : 1;
                int excessQty = totalReturnQtyInLogs - maxAllowedReturn;

                // Check for rapid duplicates (logged within 60s)
                if (excessQty <= 0 && g.Count() > 1)
                {
                    var timestamps = g.Select(m => m.CreatedAt).OrderBy(t => t).ToList();
                    bool hasCloseDuplicates = false;
                    for (int i = 0; i < timestamps.Count - 1; i++)
                    {
                        if ((timestamps[i + 1] - timestamps[i]).TotalSeconds < 60)
                        {
                            hasCloseDuplicates = true;
                            break;
                        }
                    }
                    if (hasCloseDuplicates)
                    {
                        excessQty = g.Skip(1).Sum(m => m.Quantity);
                    }
                }

                if (excessQty > 0)
                {
                    var firstProduct = g.First().Product;
                    var firstVariant = g.First().ProductVariant;

                    // 1. Log inventory adjustment OUT (-excessQty) to fix the stock
                    await inventory.LogMovementAsync(
                        type: InventoryMovementType.Adjustment,
                        quantity: -excessQty,
                        productId: g.Key.ProductId,
                        variantId: g.Key.ProductVariantId,
                        reference: g.Key.Reference,
                        note: $"تعديل آلي - تصحيح مرتجع مكرر للفاتورة #{g.Key.Reference}",
                        userId: "system-audit",
                        autoSave: false,
                        force: true
                    );

                    // 2. Remove excess movement log records to keep audit trail clean
                    var movementsToRemove = g.Skip(1).ToList();
                    _db.InventoryMovements.RemoveRange(movementsToRemove);

                    fixedResults.Add(new
                    {
                        OrderNumber = g.Key.Reference,
                        ProductName = firstProduct?.NameAr ?? firstProduct?.NameEn ?? "Unknown Product",
                        VariantSize = firstVariant?.Size,
                        VariantColor = firstVariant?.ColorAr ?? firstVariant?.Color,
                        DeductedExcessQty = excessQty,
                        RemovedMovementIds = movementsToRemove.Select(m => m.Id).ToList()
                    });
                }
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            FixedCasesCount = fixedResults.Count,
            FixedDetails = fixedResults,
            Message = "تم تصحيح كافة حركات المرتجع المكررة وتعديل المخزون بنجاح 🎉"
        });
    }
}
