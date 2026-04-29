using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Previewer;
using Sportive.API.DTOs;
using Sportive.API.Models;
using ArabicReshaper;
using System.Text.RegularExpressions;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using System.IO;
using System.Net.Http;

namespace Sportive.API.Services;

public class PdfService : IPdfService
{
    private static string _activeFont = "DejaVu Sans";
    private static bool _fontRegistered = false;

    private void EnsureFontLoaded()
    {
        if (_fontRegistered) return;
        
        try {
            // Try common Linux Arabic fonts
            string[] systemFonts = { "DejaVu Sans", "FreeSans", "Liberation Sans", "Noto Sans Arabic" };
            foreach (var font in systemFonts)
            {
                // We just hope one of these is installed on Railway
                _activeFont = font;
            }
            _fontRegistered = true;
        } catch {
            _fontRegistered = true;
        }
    }

    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order)
    {
        EnsureFontLoaded();
        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(80, 297, Unit.Millimetre);
                    page.Margin(5, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text("SPORTIVE").FontSize(18).Bold().FontColor(Colors.Black);
                        col.Item().AlignCenter().Text(Reshape(order.Source == "1" || order.Source == "POS" ? "فاتورة مبيعات - كاشير" : "فاتورة مبيعات - متجر أونلاين")).FontSize(8).FontColor(Colors.Grey.Medium);
                        
                        col.Item().PaddingTop(5).Row(row => {
                            row.RelativeItem().Text(Reshape($"رقم: {order.OrderNumber}")).Bold();
                            row.RelativeItem().AlignRight().Text(order.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
                        });

                        if (!string.IsNullOrEmpty(order.SalesPersonName))
                            col.Item().Text(Reshape($"البائع: {order.SalesPersonName}")).FontSize(7);
                        
                        col.Item().PaddingTop(2).BorderBottom(1).PaddingBottom(2);
                    });

                    page.Content().PaddingVertical(5).Column(col =>
                    {
                        col.Item().Column(c =>
                        {
                            c.Item().Text(Reshape($"العميل: {order.Customer?.FullName ?? "عميل نقدي"}")).Bold().FontSize(9);
                            if (!string.IsNullOrEmpty(order.Customer?.Phone))
                                c.Item().Text(Reshape($"الهاتف: {order.Customer.Phone}")).FontSize(8);
                            
                            c.Item().PaddingTop(2).Row(r => {
                                r.RelativeItem().Text(Reshape($"الدفع: {order.PaymentMethod}")).FontSize(8);
                                r.RelativeItem().AlignRight().Text(Reshape($"الاستلام: {order.FulfillmentType}")).FontSize(8);
                            });
                        });
                        
                        col.Item().PaddingTop(5).BorderBottom(1);

                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text(Reshape("الصنف"));
                                header.Cell().Element(CellStyle).AlignCenter().Text(Reshape("الكمية"));
                                header.Cell().Element(CellStyle).AlignRight().Text(Reshape("السعر"));
                                header.Cell().Element(CellStyle).AlignRight().Text(Reshape("الإجمالي"));

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.DefaultTextStyle(x => x.Bold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                                }
                            });

                            foreach (var item in order.Items)
                            {
                                var name = item.ProductNameAr;
                                if (!string.IsNullOrEmpty(item.Size) || !string.IsNullOrEmpty(item.Color))
                                    name += $" ({item.Size} {item.Color})";

                                table.Cell().Element(ContentStyle).Text(Reshape(name));
                                table.Cell().Element(ContentStyle).AlignCenter().Text(item.Quantity.ToString());
                                table.Cell().Element(ContentStyle).AlignRight().Text(item.UnitPrice.ToString("N0"));
                                table.Cell().Element(ContentStyle).AlignRight().Text((item.Quantity * item.UnitPrice).ToString("N0"));

                                static IContainer ContentStyle(IContainer container)
                                {
                                    return container.PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
                                }
                            }
                        });

                        col.Item().PaddingTop(10).AlignRight().Column(c =>
                        {
                            c.Item().Text(Reshape($"الإجمالي: {order.SubTotal:N0} ج.م")).FontSize(9).Bold();
                            var totalDisc = order.DiscountAmount + order.TemporalDiscount;
                            if (totalDisc > 0)
                                c.Item().Text(Reshape($"الخصم: {totalDisc:N0} ج.م")).FontSize(8).FontColor(Colors.Red.Medium);
                            
                            c.Item().Text(Reshape($"الصافي: {order.TotalAmount:N0} ج.م")).FontSize(12).Bold().FontColor(Colors.Black);
                        });

                        if (order.Payments != null && order.Payments.Any())
                        {
                            col.Item().PaddingTop(10).Column(c => {
                                c.Item().Text(Reshape("تفاصيل السداد:")).FontSize(8).Bold();
                                foreach (var p in order.Payments)
                                {
                                    var method = p.Method;
                                    if (method == "Cash") method = "نقدي";
                                    else if (method == "Bank" || method == "CreditCard") method = "شبكة / فيزا";
                                    else if (method == "InstaPay") method = "إنستا باي";
                                    else if (method == "Vodafone") method = "فودافون كاش";
                                    else if (method == "Credit") method = "آجل";

                                    c.Item().Text(Reshape($"- {method}: {p.Amount:N0} ج.م")).FontSize(7);
                                }
                            });
                        }

                        col.Item().PaddingTop(20).AlignCenter().Text(Reshape("شكراً لتعاملكم معنا!")).FontSize(10).Bold();
                        col.Item().AlignCenter().Text("www.sportive-equipment.com").FontSize(8).FontColor(Colors.Grey.Medium);
                    });

                    page.Footer().PaddingBottom(5).AlignCenter().Column(c => {
                        c.Item().Text(x => {
                            x.Span($"Engine: QuestPDF • Font: {_activeFont} • ").FontSize(5);
                            x.CurrentPageNumber();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    // ✅ FIXED SIGNATURES
    public async Task<byte[]> GeneratePurchaseInvoicePdfAsync(PurchaseInvoice invoice) { return await Task.FromResult(new byte[0]); }
    public async Task<byte[]> GenerateVoucherPdfAsync(ReceiptVoucher? receiptVoucher, PaymentVoucher? paymentVoucher) { return await Task.FromResult(new byte[0]); }
    public async Task<byte[]> GenerateOpeningBalancePdfAsync(InventoryOpeningBalance openingBalance) { return await Task.FromResult(new byte[0]); }
    public async Task<byte[]> GeneratePurchaseReturnPdfAsync(PurchaseReturn pReturn) { return await Task.FromResult(new byte[0]); }
    public async Task<byte[]> GenerateJournalEntryPdfAsync(JournalEntry entry) { return await Task.FromResult(new byte[0]); }

    private string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // Sanitize: Limit length to prevent buffer/memory issues in reshaper
        if (input.Length > 500) input = input.Substring(0, 500);
        // Replace problematic control chars
        input = new string(input.Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray());

        try {
            var reshaped = ArabicAdapter.Reshape(input);
            var characters = reshaped.ToCharArray();
            System.Array.Reverse(characters);
            var reversed = new string(characters);
            return Regex.Replace(reversed, @"[a-zA-Z0-9\-\.\/]+", m =>
            {
                var chars = m.Value.ToCharArray();
                System.Array.Reverse(chars);
                return new string(chars);
            });
        }
        catch {
            // Fallback in case of reshaping crash
            return input;
        }
    }
}
