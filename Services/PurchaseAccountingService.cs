using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Interfaces;
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
    private readonly ITranslator _t;

    public PurchaseAccountingService(AppDbContext db, AccountingCoreService core, ILogger<PurchaseAccountingService> logger, ITranslator t)
    {
        _db = db;
        _core = core;
        _logger = logger;
        _t = t;
    }

    public async Task PostPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        if (string.IsNullOrEmpty(invoice.InvoiceNumber)) return;

        // The core service handles updating existing entries by reference automatically.
        // We removed the manual deletion to preserve the original CreatedAt and ID for stable sorting.

        if (invoice.IsAssetPurchase)
        {
            await PostAssetPurchaseInvoiceAsync(invoice);
            return;
        }

        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var typeKey = invoice.PaymentTerms == PaymentTerms.Cash ? "Accounting.PurchaseCash" : "Accounting.PurchaseCredit";
        var typeStr = _t.Get(typeKey);
        
        var invAcct = invoice.InventoryAccountId != null ? $"ID:{invoice.InventoryAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Inventory, mapDict)}";
        lines.Add((invAcct, invoice.SubTotal, 0, _t.Get("Accounting.PurchaseInvoiceDesc", typeStr, invoice.InvoiceNumber)));

        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatInput, mapDict)}";
            lines.Add((vatAcct, invoice.TaxAmount, 0, _t.Get("Accounting.VatDesc", typeStr, invoice.InvoiceNumber)));
        }

        // ─── Credit Side: Liability ───
        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";

        // Always post full amount to vendor liability first. 
        // Separate Payment Vouchers will handle clearing this liability.
        lines.Add((vendorAcct, 0, invoice.TotalAmount, _t.Get("Accounting.VendorLiabilityDesc", typeStr, invoice.InvoiceNumber)));


        if (invoice.DiscountAmount > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            lines.Add((discAcct, 0, invoice.DiscountAmount, _t.Get("Accounting.PurchaseDiscountDesc", typeStr, invoice.InvoiceNumber)));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber,
            _t.Get("Accounting.PurchaseEntryMainDesc", typeStr, invoice.InvoiceNumber, invoice.Supplier?.Name ?? ""),
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

        var partialStr = isPartial ? _t.Get("Accounting.PurchaseReturnPartial") : "";

        lines.Add((vendorAcct, totalAmt, 0, _t.Get("Accounting.PurchaseReturnVendorDesc", partialStr, invoice.InvoiceNumber)));
        lines.Add((rtnAcct, 0, subTotal, _t.Get("Accounting.PurchaseReturnInventoryDesc", partialStr, invoice.InvoiceNumber)));

        if (discAmt > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            lines.Add((discAcct, discAmt, 0, _t.Get("Accounting.PurchaseReturnDiscountDesc", invoice.InvoiceNumber)));
        }

        if (taxAmt > 0) lines.Add((vatAcct, 0, taxAmt, _t.Get("Accounting.PurchaseReturnVatDesc", invoice.InvoiceNumber)));

        await _core.PostEntryAsync(JournalEntryType.PurchaseReturn, refNo, _t.Get("Accounting.PurchaseReturnMainDesc", partialStr, invoice.InvoiceNumber), TimeHelper.GetEgyptTime(), lines, supplierId: invoice.SupplierId, source: invoice.CostCenter);
    }

    public async Task PostPurchaseReturnAsync(PurchaseReturn pReturn)
    {
        if (string.IsNullOrEmpty(pReturn.ReturnNumber)) return;

        // Handled by core service update logic

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
            lines.Add((cashAcct, pReturn.TotalAmount, 0, _t.Get("Accounting.PurchaseReturnCashRefund", pReturn.ReturnNumber)));
        }
        else
        {
            lines.Add((vendorAcct, pReturn.TotalAmount, 0, _t.Get("Accounting.PurchaseReturnCreditNote", pReturn.ReturnNumber)));
        }
        
        // Credit Inventory (Reducing asset)
        lines.Add((rtnAcct, 0, pReturn.SubTotal, _t.Get("Accounting.PurchaseReturnItemsValue", pReturn.ReturnNumber)));

        if (pReturn.DiscountAmount > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            // Reversing the discount granted originally (Debit Discount account if it was credited)
            // Or just reduce the cost. Standard is to debit it back if it's a dedicated account.
            lines.Add((discAcct, pReturn.DiscountAmount, 0, _t.Get("Accounting.PurchaseReturnDiscountDesc", pReturn.ReturnNumber)));
        }

        if (pReturn.TaxAmount > 0) 
        {
            lines.Add((vatAcct, 0, pReturn.TaxAmount, _t.Get("Accounting.PurchaseReturnVatReverse", pReturn.ReturnNumber)));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseReturn, 
            pReturn.ReturnNumber, 
            _t.Get("Accounting.PurchaseReturnSupplierDesc", pReturn.ReturnNumber, pReturn.Supplier?.Name ?? ""), 
            pReturn.ReturnDate, 
            lines, 
            supplierId: pReturn.SupplierId,
            purchaseInvoiceId: pReturn.PurchaseInvoiceId,
            source: pReturn.CostCenter);
    }

    private async Task PostAssetPurchaseInvoiceAsync(PurchaseInvoice invoice)
    {
        var mapDict = await _core.GetSafeSystemMappingsAsync();
        var lines = new List<(string code, decimal debit, decimal credit, string desc)>();

        var typeKey = invoice.PaymentTerms == PaymentTerms.Cash ? "Accounting.PurchaseCash" : "Accounting.PurchaseCredit";
        var typeStr = _t.Get(typeKey);

        // 1. Debit each asset line to its specific Asset Account
        // Note: For asset purchases, we MUST have FixedAssetCategoryId linked in the item
        var items = await _db.PurchaseInvoiceItems
            .Include(i => i.FixedAssetCategory)
            .Where(i => i.PurchaseInvoiceId == invoice.Id)
            .ToListAsync();

        foreach (var item in items)
        {
            var assetAcctId = item.FixedAssetCategory?.AssetAccountId 
                            ?? await _core.GetRequiredMappedAccountAsync(MK.FixedAssetRegistry, mapDict);
            
            lines.Add(($"ID:{assetAcctId}", item.TotalCost, 0, _t.Get("Accounting.AssetPurchaseLineDesc", item.AssetName ?? item.Description, invoice.InvoiceNumber)));
        }

        // 2. VAT (Debit)
        if (invoice.TaxAmount > 0)
        {
            var vatAcct = invoice.VatAccountId != null ? $"ID:{invoice.VatAccountId}" : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.VatInput, mapDict)}";
            lines.Add((vatAcct, invoice.TaxAmount, 0, _t.Get("Accounting.VatDesc", typeStr, invoice.InvoiceNumber)));
        }

        // 3. Credit Side: Liability
        var vendorAcct = invoice.VendorAccountId != null ? $"ID:{invoice.VendorAccountId}" 
                        : invoice.Supplier?.MainAccountId != null ? $"ID:{invoice.Supplier.MainAccountId}" 
                        : $"ID:{await _core.GetRequiredMappedAccountAsync(MK.Supplier, mapDict)}";

        // Always post full amount to vendor liability first.
        lines.Add((vendorAcct, 0, invoice.TotalAmount, _t.Get("Accounting.VendorLiabilityDesc", typeStr, invoice.InvoiceNumber)));


        // 4. Discount (Credit)
        if (invoice.DiscountAmount > 0)
        {
            var discAcct = $"ID:{await _core.GetRequiredMappedAccountAsync(MK.PurchaseDiscount, mapDict)}";
            lines.Add((discAcct, 0, invoice.DiscountAmount, _t.Get("Accounting.PurchaseDiscountDesc", typeStr, invoice.InvoiceNumber)));
        }

        await _core.PostEntryAsync(
            JournalEntryType.PurchaseInvoice, invoice.InvoiceNumber,
            _t.Get("Accounting.AssetPurchaseEntryMainDesc", typeStr, invoice.InvoiceNumber, invoice.Supplier?.Name ?? ""),
            invoice.InvoiceDate, lines, supplierId: invoice.SupplierId, purchaseInvoiceId: invoice.Id,
            source: invoice.CostCenter);
    }
}
