using System.Threading.Tasks;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Sportive.API.Services;

public interface ITaxIntegrationService
{
    Task ProcessOrderTaxesAsync(Order order);
}

public class TaxIntegrationService : ITaxIntegrationService
{
    private readonly AppDbContext _context;

    public TaxIntegrationService(AppDbContext context)
    {
        _context = context;
    }

    public async Task ProcessOrderTaxesAsync(Order order)
    {
        // Get store config
        var storeInfo = await _context.StoreInfo.FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        if (storeInfo == null || storeInfo.TaxAuthorityType == TaxAuthorityType.None)
            return; // No integration needed

        if (storeInfo.TaxAuthorityType == TaxAuthorityType.SaudiZATCA)
        {
            // Calculate total VAT from order
            decimal totalVat = order.Items?.Sum(i => i.ItemVatAmount) ?? 0;
            if (totalVat == 0 && order.Items == null)
            {
                // In case items are not loaded, load them
                var items = await _context.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync();
                totalVat = items.Sum(i => i.ItemVatAmount);
            }

            // Generate ZATCA Phase 1 QR
            var qrCode = ZatcaQrGenerator.GenerateQrCode(
                sellerName: storeInfo.StoreBrandName ?? "Unknown Seller",
                taxNumber: storeInfo.ZatcaTaxNumber ?? "000000000000000",
                timestamp: order.CreatedAt,
                invoiceTotal: order.TotalAmount,
                vatTotal: totalVat
            );

            order.TaxAuthorityQrCode = qrCode;
            order.TaxAuthorityStatus = "Local_QR_Generated";
        }
        else if (storeInfo.TaxAuthorityType == TaxAuthorityType.EgyptETA)
        {
            // ETA does not generate local QR code like ZATCA Phase 1.
            // ETA requires submission to get a UUID/Receipt ID.
            // That will be implemented in Phase 3.
            order.TaxAuthorityStatus = "Pending_ETA_Submission";
        }
    }
}
