using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using MK = Sportive.API.Utils.MappingKeys;

namespace Sportive.API.Services;

/// <summary>
/// خدمة مخصصة لقيود المشتريات: فواتير الشراء + المرتجعات
/// </summary>
public class PurchaseAccountingService
{
    private readonly AppDbContext _db;
    private readonly AccountingCoreService _core;
    private readonly ILogger<PurchaseAccountingService> _logger;

    public PurchaseAccountingService(AppDbContext db, AccountingCoreService core, ILogger<PurchaseAccountingService> logger)
    {
        _db = db;
        _core = core;
        _logger = logger;
    }

    public async Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        if (string.IsNullOrEmpty(invoice.InvoiceNumber)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var typeStr = invoice.PaymentTerms == PaymentTerms.Cash ? "نقدي" : "آجل";
        var invAcct = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        lines.Add((invAcct, invoice.SubTotal, 0, $"مشتريات {typeStr} - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatInput, mapDict)}";
            lines.Add((vatAcct, invoice.TaxAmount, 0, $"ضريبة قيمة مضافة {typeStr} - {invoice.InvoiceNumber}"));
        }

        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";
        
        if (invoice.PaymentTerms != PaymentTerms.Cash)
        {
            lines.Add((vendorAcct, 0, invoice.TotalAmount, $"إثبات استحقاق مورد ({typeStr}) - {invoice.InvoiceNumber}"));
        }
        else
        {
            var cashAcct = invoice.CashAccountId != null ? $"ID:{invoice.CashAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Cash, mapDict)}";
            lines.Add((cashAcct, 0, invoice.TotalAmount, $"صرف نقدية مشتريات فورية - {invoice.InvoiceNumber}"));
        }

        if (invoice.DiscountAmount > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            lines.Add((discAcct, 0, invoice.DiscountAmount, $"خصم مكتسب ({typeStr}) - {invoice.InvoiceNumber}"));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber,
            $"فاتورة مشتريات {typeStr} {invoice.InvoiceNumber} - {invoice.Supplier?.Name}",
            invoice.InvoiceDate, lines, supplierId: invoice.SupplierId, purchaseInvoiceId: invoice.Id,
            source: invoice.CostCenter);
    }

    public async Task PostPurchaseReturnAsync(PurchaseInvoice invoice, decimal returnedSubTotal = 0, decimal returnedTaxAmount = 0, decimal returnedDiscountAmount = 0)
    {
        var isPartial = returnedSubTotal > 0;
        var subTotal = isPartial ? returnedSubTotal : invoice.SubTotal;
        var taxAmt   = isPartial ? returnedTaxAmount : invoice.TaxAmount;
        var discAmt  = isPartial ? returnedDiscountAmount : invoice.DiscountAmount;
        var totalAmt = (subTotal + taxAmt) - discAmt;

        var refNo = isPartial ? $"{invoice.InvoiceNumber}-PRTN-{DateTime.UtcNow.Ticks % 10000}" : invoice.InvoiceNumber + "-RTN";
        if (await _core.EntryExistsAsync(JournalEntryType.PurchaseReturn, refNo)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";
        var rtnAcct    = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        var vatAcct    = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatInput, mapDict)}";

        lines.Add((vendorAcct, totalAmt, 0, $"رد للمورد {(isPartial ? "(جزئي)" : "")} - {invoice.InvoiceNumber}"));
        lines.Add((rtnAcct, 0, subTotal, $"مرتجع مشتريات {(isPartial ? "(جزئي)" : "")} - {invoice.InvoiceNumber}"));

        if (discAmt > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            lines.Add((discAcct, discAmt, 0, $"عكس خصم مشتريات - {invoice.InvoiceNumber}"));
        }

        if (taxAmt > 0) lines.Add((vatAcct, 0, taxAmt, $"استرداد ضريبة - {invoice.InvoiceNumber}"));

        await _core.PostEntryAsync(JournalEntryType.PurchaseReturn, refNo, $"مرتجع مشتريات {(isPartial ? "جزئي" : "")} {invoice.InvoiceNumber}", TimeHelper.GetEgyptTime(), lines, supplierId: invoice.SupplierId, source: invoice.CostCenter);
    }

    public async Task PostPurchaseReturnAsync(PurchaseReturn pReturn)
    {
        if (string.IsNullOrEmpty(pReturn.ReturnNumber)) return;

        // 🚨 AUTO-UPDATE: حذف القيد القديم إن وجد للسماح بالتعديل
        var existing = await _db.JournalEntries
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.PurchaseReturn && e.Reference == pReturn.ReturnNumber);
        
        if (existing != null)
        {
            _db.JournalEntries.Remove(existing);
            await _db.SaveChangesAsync();
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var vendorAcct = pReturn.Supplier?.MainAccountId != null ? $"ID:{pReturn.Supplier.MainAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";
        var rtnAcct    = pReturn.Invoice?.InventoryAccountId != null ? $"ID:{pReturn.Invoice.InventoryAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        var vatAcct    = pReturn.Invoice?.VatAccountId != null ? $"ID:{pReturn.Invoice.VatAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatInput, mapDict)}";

        // Debit Target (Reducing liability or increasing cash)
        if (pReturn.PaymentTerms == PaymentTerms.Cash)
        {
            var cashAcct = pReturn.CashAccountId != null ? $"ID:{pReturn.CashAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Cash, mapDict)}";
            lines.Add((cashAcct, pReturn.TotalAmount, 0, $"استرداد نقدي مرتجع {pReturn.ReturnNumber}"));
        }
        else
        {
            lines.Add((vendorAcct, pReturn.TotalAmount, 0, $"مرتجع آجل {pReturn.ReturnNumber} - تخفيض مديونية"));
        }
        
        // Credit Inventory (Reducing asset)
        lines.Add((rtnAcct, 0, pReturn.SubTotal, $"مرتجع مشتريات {pReturn.ReturnNumber} - قيمة أصناف"));

        if (pReturn.DiscountAmount > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            // Reversing the discount granted originally (Debit Discount account if it was credited)
            // Or just reduce the cost. Standard is to debit it back if it's a dedicated account.
            lines.Add((discAcct, pReturn.DiscountAmount, 0, $"عكس خصم مشتريات - {pReturn.ReturnNumber}"));
        }

        if (pReturn.TaxAmount > 0) 
        {
            lines.Add((vatAcct, 0, pReturn.TaxAmount, $"عكس ضريبة مدخلات - {pReturn.ReturnNumber}"));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseReturn, 
            pReturn.ReturnNumber, 
            $"مرتجع مشتريات {pReturn.ReturnNumber} للمورد {pReturn.Supplier?.Name}", 
            pReturn.ReturnDate, 
            lines, 
            supplierId: pReturn.SupplierId,
            purchaseInvoiceId: pReturn.PurchaseInvoiceId,
            source: pReturn.CostCenter);
    }
}

