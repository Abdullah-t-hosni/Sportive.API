using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IPdfService
{
    Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order);
    Task<byte[]> GeneratePurchaseInvoicePdfAsync(PurchaseInvoice invoice);
    Task<byte[]> GenerateVoucherPdfAsync(ReceiptVoucher? receiptVoucher, PaymentVoucher? paymentVoucher);
    Task<byte[]> GenerateOpeningBalancePdfAsync(InventoryOpeningBalance openingBalance);
    Task<byte[]> GeneratePurchaseReturnPdfAsync(PurchaseReturn pReturn);
    Task<byte[]> GenerateJournalEntryPdfAsync(JournalEntry entry);
}
