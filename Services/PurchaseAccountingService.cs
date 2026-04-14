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
        if (await _core.EntryExistsAsync(JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber)) return;

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var typeStr = invoice.PaymentTerms == PaymentTerms.Cash ? "نقدي" : "آجل";
        var invAcct = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : _core.GetMap(mapDict, MK.Inventory, AccountingCoreService.INVENTORY);
        lines.Add((invAcct, invoice.SubTotal, 0, $"مشتريات {typeStr} - {invoice.InvoiceNumber}"));

        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : _core.GetMap(mapDict, MK.VatInput, AccountingCoreService.VAT_INPUT);
            lines.Add((vatAcct, invoice.TaxAmount, 0, $"ضريبة قيمة مضافة {typeStr} - {invoice.InvoiceNumber}"));
        }

        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : _core.GetMap(mapDict, MK.Supplier, AccountingCoreService.PAYABLES);
        
        if (invoice.PaymentTerms != PaymentTerms.Cash)
        {
            lines.Add((vendorAcct, 0, invoice.TotalAmount, $"إثبات استحقاق مورد ({typeStr}) - {invoice.InvoiceNumber}"));
        }
        else
        {
            var cashAcct = invoice.CashAccountId != null ? $"ID:{invoice.CashAccountId}" : _core.GetMap(mapDict, MK.Cash, AccountingCoreService.CASH_ACCOUNTS);
            lines.Add((cashAcct, 0, invoice.TotalAmount, $"صرف نقدية مشتريات فورية - {invoice.InvoiceNumber}"));
        }

        if (invoice.DiscountAmount > 0)
        {
            var discAcct = _core.GetMap(mapDict, MK.PurchaseDiscount, AccountingCoreService.PURCHASE_DISC);
            lines.Add((discAcct, 0, invoice.DiscountAmount, $"خصم مكتسب ({typeStr}) - {invoice.InvoiceNumber}"));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber,
            $"فاتورة مشتريات {typeStr} {invoice.InvoiceNumber} - {invoice.Supplier?.Name}",
            invoice.InvoiceDate, lines, supplierId: invoice.SupplierId);
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
                        : _core.GetMap(mapDict, MK.Supplier, AccountingCoreService.PAYABLES);
        var rtnAcct    = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : _core.GetMap(mapDict, MK.Inventory, AccountingCoreService.INVENTORY);
        var vatAcct    = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : _core.GetMap(mapDict, MK.VatInput, AccountingCoreService.VAT_INPUT);

        lines.Add((vendorAcct, totalAmt, 0, $"رد للمورد {(isPartial ? "(جزئي)" : "")} - {invoice.InvoiceNumber}"));
        lines.Add((rtnAcct, 0, subTotal, $"مرتجع مشتريات {(isPartial ? "(جزئي)" : "")} - {invoice.InvoiceNumber}"));

        if (discAmt > 0)
        {
            var discAcct = _core.GetMap(mapDict, MK.PurchaseDiscount, AccountingCoreService.PURCHASE_DISC);
            lines.Add((discAcct, discAmt, 0, $"عكس خصم مشتريات - {invoice.InvoiceNumber}"));
        }

        if (taxAmt > 0) lines.Add((vatAcct, 0, taxAmt, $"استرداد ضريبة - {invoice.InvoiceNumber}"));

        await _core.PostEntryAsync(JournalEntryType.PurchaseReturn, refNo, $"مرتجع مشتريات {(isPartial ? "جزئي" : "")} {invoice.InvoiceNumber}", TimeHelper.GetEgyptTime(), lines, supplierId: invoice.SupplierId);
    }
}
