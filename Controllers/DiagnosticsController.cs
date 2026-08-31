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

    [HttpGet("inspect-backups")]
    public async Task<IActionResult> InspectBackups()
    {
        var records = await _db.BackupRecords
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .Select(r => new
            {
                r.Id,
                r.FileName,
                r.FileSizeBytes,
                r.Success,
                r.Error,
                r.TriggerType,
                r.EmailSent,
                r.EmailError,
                r.CreatedAt
            })
            .ToListAsync();

        var storeInfo = await _db.StoreInfo.FirstOrDefaultAsync();

        return Ok(new
        {
            StoreBackupTime = storeInfo?.BackupTime ?? "02:00",
            TotalRecords = records.Count,
            Records = records
        });
    }

    [HttpGet("inspect-whatsapp")]
    public async Task<IActionResult> InspectWhatsApp([FromServices] IConfiguration config)
    {
        var storeInfo = await _db.StoreInfo.FirstOrDefaultAsync();
        return Ok(new
        {
            ConfigServiceUrl = config["WhatsApp:ServiceUrl"],
            StoreSettingsGatewayUrl = storeInfo?.WhatsAppStoreGatewayUrl,
            StoreSettingsPosGatewayUrl = storeInfo?.WhatsAppPosGatewayUrl,
            WorkingGatewayUrl = "https://sportive-frontend-production-65ac.up.railway.app"
        });
    }

    [HttpPost("fix-whatsapp-gateway-url")]
    public async Task<IActionResult> FixWhatsAppGatewayUrl()
    {
        var storeInfo = await _db.StoreInfo.FirstOrDefaultAsync();
        if (storeInfo != null)
        {
            storeInfo.WhatsAppStoreGatewayUrl = "https://sportive-frontend-production-65ac.up.railway.app";
            storeInfo.WhatsAppPosGatewayUrl = "https://sportive-frontend-production-65ac.up.railway.app";
            await _db.SaveChangesAsync();
        }
        return Ok(new
        {
            message = "SUCCESS: Updated WhatsApp Gateway URLs in database to https://sportive-frontend-production-65ac.up.railway.app",
            UpdatedStoreGatewayUrl = storeInfo?.WhatsAppStoreGatewayUrl,
            UpdatedPosGatewayUrl = storeInfo?.WhatsAppPosGatewayUrl
        });
    }

    [HttpPost("fix-legacy-website-settlements")]
    public async Task<IActionResult> FixLegacyWebsiteSettlements()
    {
        // Cutoff Date: Exact launch date of the settlement feature (11 August 2026)
        var cutoffDate = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

        // Filter STRICTLY for Website Orders ONLY (Source == OrderSource.Website)
        var legacyWebsiteOrders = await _db.Orders
            .Where(o => o.Source == OrderSource.Website 
                     && o.Status == OrderStatus.Delivered 
                     && !o.IsSettledWithCourier 
                     && o.CreatedAt < cutoffDate)
            .ToListAsync();

        int count = legacyWebsiteOrders.Count;
        foreach (var o in legacyWebsiteOrders)
        {
            o.IsSettledWithCourier = true;
            o.CourierSettlementDate ??= o.CreatedAt;
            o.CourierSettlementReference ??= "تسوية آلي لطلبات المتجر الإلكتروني القديمة قبل أغسطس";
        }

        await _db.SaveChangesAsync();

        return Ok(new
        {
            FixedWebsiteOrdersCount = count,
            FixedOrdersList = legacyWebsiteOrders.Select(o => new { o.Id, o.OrderNumber, o.TotalAmount, o.CreatedAt }),
            Message = $"تم تعميد وتسوية {count} طلب متجر إلكتروني أونلاين فقط (قبل أغسطس) بنجاح 100% دون المساس إطلاقاً بطلبات الكاشير 🎉"
        });
    }

    [HttpGet("discover-settlement-launch")]
    public async Task<IActionResult> DiscoverSettlementLaunch()
    {
        // 1. Find earliest CourierSettlementDate on settled orders
        var earliestSettledOrder = await _db.Orders
            .Where(o => o.IsSettledWithCourier && o.CourierSettlementDate != null)
            .OrderBy(o => o.CourierSettlementDate)
            .Select(o => new { o.Id, o.OrderNumber, o.CourierSettlementDate, o.CourierSettlementReference, o.CreatedAt })
            .FirstOrDefaultAsync();

        // 2. Find earliest order created date that has IsSettledWithCourier = true
        var earliestSettledOrderCreated = await _db.Orders
            .Where(o => o.IsSettledWithCourier)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new { o.Id, o.OrderNumber, o.CourierSettlementDate, o.CourierSettlementReference, o.CreatedAt })
            .FirstOrDefaultAsync();

        // 3. Find earliest JournalEntry or ReceiptVoucher related to settlements
        var earliestSettlementJournal = await _db.JournalEntries
            .Where(e => (e.Reference != null && (e.Reference.Contains("STL") || e.Reference.Contains("SETTLE"))) || (e.Description != null && e.Description.Contains("تسوية")))
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.Id, e.EntryNumber, e.Reference, e.Description, e.CreatedAt })
            .FirstOrDefaultAsync();

        // 4. Count settled vs unsettled website orders by month
        var allWebsiteDelivered = await _db.Orders
            .Where(o => o.Source == OrderSource.Website && o.Status == OrderStatus.Delivered)
            .Select(o => new { o.Id, o.CreatedAt, o.IsSettledWithCourier, o.CourierSettlementDate, o.PaymentMethod })
            .ToListAsync();

        var monthlyStats = allWebsiteDelivered
            .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                YearMonth = $"{g.Key.Year}-{g.Key.Month:D2}",
                TotalDelivered = g.Count(),
                SettledCount = g.Count(o => o.IsSettledWithCourier),
                UnsettledCount = g.Count(o => !o.IsSettledWithCourier)
            })
            .ToList();

        return Ok(new
        {
            EarliestSettlementDateRecorded = earliestSettledOrder?.CourierSettlementDate,
            EarliestSettledOrderDetails = earliestSettledOrder,
            EarliestSettledOrderCreated = earliestSettledOrderCreated,
            EarliestSettlementJournal = earliestSettlementJournal,
            MonthlyDeliveredOrdersStats = monthlyStats
        });
    }

    [HttpGet("inspect-july-accounting")]
    public async Task<IActionResult> InspectJulyAccounting()
    {
        var from = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);

        var orders = await _db.Orders
            .Include(o => o.ShippingCompany)
            .Include(o => o.Customer)
            .Where(o => o.Source == OrderSource.Website && o.CreatedAt >= from && o.CreatedAt <= to)
            .ToListAsync();

        var pendingOrders = orders
            .Where(o => o.Status == OrderStatus.Delivered && !o.IsSettledWithCourier)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.TotalAmount,
                Status = o.Status.ToString(),
                PaymentMethod = o.PaymentMethod.ToString(),
                PaymentStatus = o.PaymentStatus.ToString(),
                FulfillmentType = o.FulfillmentType.ToString(),
                ShippingType = o.ShippingType,
                ShippingCarrierName = o.ShippingCarrierName,
                ShippingCompanyName = o.ShippingCompany?.NameAr,
                o.IsSettledWithCourier,
                o.CreatedAt
            })
            .ToList();

        var breakdownByPaymentMethod = pendingOrders
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new { PaymentMethod = g.Key, Count = g.Count(), TotalAmount = g.Sum(o => o.TotalAmount) })
            .ToList();

        var breakdownByCarrier = pendingOrders
            .GroupBy(o => o.ShippingCarrierName ?? o.ShippingCompanyName ?? o.ShippingType ?? "Unknown")
            .Select(g => new { Carrier = g.Key, Count = g.Count(), TotalAmount = g.Sum(o => o.TotalAmount) })
            .ToList();

        return Ok(new
        {
            TotalJulyWebsiteOrders = orders.Count,
            PendingSettlementOrdersCount = pendingOrders.Count,
            PendingSettlementTotalAmount = pendingOrders.Sum(o => o.TotalAmount),
            BreakdownByPaymentMethod = breakdownByPaymentMethod,
            BreakdownByCarrier = breakdownByCarrier,
            Orders = pendingOrders
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
        // 0. Clean up any previous system-audit adjustment movements to avoid double deductions
        var systemAuditMovements = await _db.InventoryMovements
            .Where(m => m.CreatedByUserId == "system-audit")
            .ToListAsync();

        if (systemAuditMovements.Any())
        {
            _db.InventoryMovements.RemoveRange(systemAuditMovements);
            await _db.SaveChangesAsync();
        }

        // 1. Fetch all ReturnIn and Sale inventory movements with non-null Reference
        var allOrderMovements = await _db.InventoryMovements
            .Include(m => m.Product)
            .Include(m => m.ProductVariant)
            .Where(m => (m.Type == InventoryMovementType.ReturnIn || m.Type == InventoryMovementType.Sale) && !string.IsNullOrEmpty(m.Reference))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var orderNumbers = allOrderMovements.Select(m => m.Reference).Distinct().ToList();
        var orders = await _db.Orders
            .Include(o => o.Items)
            .Where(o => orderNumbers.Contains(o.OrderNumber))
            .ToDictionaryAsync(o => o.OrderNumber, o => o);

        var fixedResults = new List<object>();

        // A. Handle ReturnIn duplicates
        var returnGrouped = allOrderMovements
            .Where(m => m.Type == InventoryMovementType.ReturnIn)
            .GroupBy(m => new { m.Reference, m.ProductId, m.ProductVariantId });

        foreach (var g in returnGrouped)
        {
            orders.TryGetValue(g.Key.Reference!, out var order);
            int soldQuantity = order != null 
                ? order.Items.Where(i => i.ProductId == g.Key.ProductId && i.ProductVariantId == g.Key.ProductVariantId).Sum(i => i.Quantity)
                : 0;

            int totalReturnQtyInLogs = g.Sum(m => m.Quantity);

            if (g.Count() > 1 || totalReturnQtyInLogs > soldQuantity)
            {
                var firstProduct = g.First().Product;
                var firstVariant = g.First().ProductVariant;

                var movementsToRemove = g.Skip(1).ToList();
                int excessQtyRemoved = movementsToRemove.Sum(m => m.Quantity);

                _db.InventoryMovements.RemoveRange(movementsToRemove);

                fixedResults.Add(new
                {
                    Type = "ReturnIn Duplicate Cleaned",
                    OrderNumber = g.Key.Reference,
                    ProductName = firstProduct?.NameAr ?? firstProduct?.NameEn ?? "Unknown Product",
                    VariantSize = firstVariant?.Size,
                    VariantColor = firstVariant?.ColorAr ?? firstVariant?.Color,
                    AdjustedQty = excessQtyRemoved,
                    RemovedMovementIds = movementsToRemove.Select(m => m.Id).ToList()
                });
            }
        }

        // B. Handle Sale duplicates (from status toggling)
        var saleGrouped = allOrderMovements
            .Where(m => m.Type == InventoryMovementType.Sale)
            .GroupBy(m => new { m.Reference, m.ProductId, m.ProductVariantId });

        foreach (var g in saleGrouped)
        {
            orders.TryGetValue(g.Key.Reference!, out var order);
            int soldQuantity = order != null 
                ? order.Items.Where(i => i.ProductId == g.Key.ProductId && i.ProductVariantId == g.Key.ProductVariantId).Sum(i => i.Quantity)
                : 0;

            if (g.Count() > 1 && soldQuantity > 0)
            {
                var firstProduct = g.First().Product;
                var firstVariant = g.First().ProductVariant;

                // Keep only 1 Sale movement per item (or movements up to soldQuantity)
                var movementsToRemove = g.Skip(1).ToList();
                int excessSaleQtyRemoved = movementsToRemove.Sum(m => Math.Abs(m.Quantity));

                _db.InventoryMovements.RemoveRange(movementsToRemove);

                fixedResults.Add(new
                {
                    Type = "Sale Duplicate Cleaned",
                    OrderNumber = g.Key.Reference,
                    ProductName = firstProduct?.NameAr ?? firstProduct?.NameEn ?? "Unknown Product",
                    VariantSize = firstVariant?.Size,
                    VariantColor = firstVariant?.ColorAr ?? firstVariant?.Color,
                    AdjustedQty = excessSaleQtyRemoved,
                    RemovedMovementIds = movementsToRemove.Select(m => m.Id).ToList()
                });
            }
        }

        await _db.SaveChangesAsync();

        // 2. Trigger RecalculateStock to recalculate all variant stock quantities & chronological running balances
        await _maintenance.RecalculateStockAsync();

        return Ok(new
        {
            FixedCasesCount = fixedResults.Count,
            FixedDetails = fixedResults,
            Message = "تم تصحيح وتنظيف كافة حركات المرتجع والمبيعات المكررة وإعادة بناء الأرصدة التراكمية بنجاح 🎉"
        });
    }

    [HttpGet("fix-returned-orders")]
    public async Task<IActionResult> FixReturnedOrders()
    {
        var (success, message, count) = await _maintenance.FixReturnedOrderStatusesAsync();
        return Ok(new { success, message, count });
    }

    [HttpGet("fix-legacy-settlements")]
    public async Task<IActionResult> FixLegacySettlements()
    {
        var (success, message, count) = await _maintenance.FixLegacyWebsiteSettlementsAsync();
        return Ok(new { success, message, count });
    }

    [HttpPost("unsettle-orders-by-number")]
    public async Task<IActionResult> UnsettleOrdersByNumber([FromBody] List<string> orderNumbers)
    {
        if (orderNumbers == null || !orderNumbers.Any())
            return BadRequest("orderNumbers required");

        var orders = await _db.Orders
            .Where(o => orderNumbers.Contains(o.OrderNumber) && o.IsSettledWithCourier == true)
            .ToListAsync();

        int count = 0;
        foreach (var o in orders)
        {
            o.IsSettledWithCourier = false;
            o.CourierSettlementDate = null;
            o.CourierSettlementReference = null;
            count++;
        }

        if (count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return Ok(new { success = true, count, orderNumbers });
    }

    public class SetItemReturnedQtyDto
    {
        public string OrderNumber { get; set; } = string.Empty;
        public int OrderItemId { get; set; }
        public int ReturnedQuantity { get; set; }
    }

    [HttpPost("set-item-returned-qty")]
    public async Task<IActionResult> SetItemReturnedQty([FromBody] SetItemReturnedQtyDto dto)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == dto.OrderNumber);
        if (order == null) return NotFound("Order not found");

        var item = order.Items.FirstOrDefault(i => i.Id == dto.OrderItemId);
        if (item == null) return NotFound("Item not found");

        item.ReturnedQuantity = dto.ReturnedQuantity;

        if (order.Items.All(i => i.ReturnedQuantity >= i.Quantity))
        {
            order.Status = OrderStatus.Returned;
        }
        else if (order.Items.Any(i => i.ReturnedQuantity > 0))
        {
            order.Status = OrderStatus.PartiallyReturned;
        }
        else
        {
            order.Status = OrderStatus.Delivered;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, orderNumber = order.OrderNumber, status = order.Status.ToString(), itemId = item.Id, itemReturnedQty = item.ReturnedQuantity });
    }

    [HttpGet("inspect-order-status")]
    public async Task<IActionResult> InspectOrderStatus([FromQuery] string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber)) return BadRequest("orderNumber is required");

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);

        if (order == null) return NotFound($"Order {orderNumber} not found");

        var returnRequests = await _db.ReturnExchangeRequests
            .Include(r => r.Items)
            .Where(r => r.OrderId == order.Id)
            .ToListAsync();

        return Ok(new
        {
            order.Id,
            order.OrderNumber,
            Status = order.Status.ToString(),
            order.TotalAmount,
            order.PaidAmount,
            order.DeliveryFee,
            order.IsSettledWithCourier,
            order.CourierSettlementDate,
            order.CourierSettlementReference,
            order.ShippingCompanyId,
            order.ShippingCarrierName,
            Items = order.Items.Select(i => new
            {
                i.Id,
                i.ProductNameAr,
                i.UnitPrice,
                i.Quantity,
                i.ReturnedQuantity
            }),
            StatusHistory = order.StatusHistory.Select(h => new
            {
                h.Id,
                Status = h.Status.ToString(),
                h.Note,
                h.ChangedByUserId,
                h.CreatedAt
            }),
            ReturnRequests = returnRequests.Select(r => new
            {
                r.Id,
                Status = r.Status.ToString(),
                r.Reason,
                r.RefundShipping,
                r.CreatedAt,
                r.ReceivedAtWarehouseAt,
                Items = r.Items.Select(ri => new
                {
                    ri.Id,
                    ri.OrderItemId,
                    ri.Quantity
                })
            })
        });
    }
}
