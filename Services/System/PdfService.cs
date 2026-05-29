using System;
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
    private readonly ITranslator _t;

    public PdfService(ITranslator t)
    {
        _t = t;
    }

    private static string _activeFont = "DejaVu Sans";
    private static bool _fontRegistered = false;

    private void EnsureFontLoaded()
    {
        if (_fontRegistered) return;
        
        try {
            string fontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts");
            if (!Directory.Exists(fontDir))
            {
                Directory.CreateDirectory(fontDir);
            }
            
            string fontPath = Path.Combine(fontDir, "Cairo-Regular.ttf");
            if (!File.Exists(fontPath))
            {
                // Download Cairo-Regular.ttf from Google Fonts raw repository
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    var bytes = client.GetByteArrayAsync("https://github.com/google/fonts/raw/main/ofl/cairo/Cairo-Regular.ttf").GetAwaiter().GetResult();
                    File.WriteAllBytes(fontPath, bytes);
                }
            }

            if (File.Exists(fontPath))
            {
                using (var stream = File.OpenRead(fontPath))
                {
                    QuestPDF.Drawing.FontManager.RegisterFont(stream);
                }
                _activeFont = "Cairo";
            }
            else
            {
                _activeFont = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Arial" : "DejaVu Sans";
            }
            
            _fontRegistered = true;
        } catch (Exception ex) {
            Console.WriteLine($"[PdfService] Error loading custom Cairo font: {ex.Message}");
            _activeFont = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ? "Arial" : "DejaVu Sans";
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
                        col.Item().AlignCenter().Text(Reshape(order.Source == "1" || order.Source == "POS" ? _t.Get("Pdf.SalesInvoiceCashier") : _t.Get("Pdf.SalesInvoiceOnline"))).FontSize(8).FontColor(Colors.Grey.Medium);
                        
                        col.Item().PaddingTop(5).Row(row => {
                            row.RelativeItem().Text(Reshape(_t.Get("Pdf.Number", order.OrderNumber))).Bold();
                            row.RelativeItem().AlignRight().Text(order.CreatedAt.ToString("yyyy/MM/dd HH:mm"));
                        });

                        if (!string.IsNullOrEmpty(order.SalesPersonName))
                            col.Item().Text(Reshape(_t.Get("Pdf.Seller", order.SalesPersonName))).FontSize(7);
                        
                        col.Item().PaddingTop(2).BorderBottom(1).PaddingBottom(2);
                    });

                    page.Content().PaddingVertical(5).Column(col =>
                    {
                        col.Item().Column(c =>
                        {
                            c.Item().Text(Reshape(_t.Get("Pdf.Customer", order.Customer?.FullName ?? _t.Get("Pdf.CashCustomer")))).Bold().FontSize(9);
                            if (!string.IsNullOrEmpty(order.Customer?.Phone))
                                c.Item().Text(Reshape(_t.Get("Pdf.Phone", order.Customer.Phone))).FontSize(8);
                            
                            c.Item().PaddingTop(2).Row(r => {
                                r.RelativeItem().Text(Reshape(_t.Get("Pdf.Payment", order.PaymentMethod))).FontSize(8);
                                r.RelativeItem().AlignRight().Text(Reshape(_t.Get("Pdf.Fulfillment", order.FulfillmentType))).FontSize(8);
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
                                header.Cell().Element(CellStyle).Text(Reshape(_t.Get("Pdf.Item")));
                                header.Cell().Element(CellStyle).AlignCenter().Text(Reshape(_t.Get("Pdf.Qty")));
                                header.Cell().Element(CellStyle).AlignRight().Text(Reshape(_t.Get("Pdf.Price")));
                                header.Cell().Element(CellStyle).AlignRight().Text(Reshape(_t.Get("Pdf.Total")));

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
                            c.Item().Text(Reshape(_t.Get("Pdf.SubTotal", order.SubTotal.ToString("N0")))).FontSize(9).Bold();
                            var totalDisc = order.DiscountAmount + order.TemporalDiscount;
                            if (totalDisc > 0)
                                c.Item().Text(Reshape(_t.Get("Pdf.Discount", totalDisc.ToString("N0")))).FontSize(8).FontColor(Colors.Red.Medium);
                            
                            c.Item().Text(Reshape(_t.Get("Pdf.NetTotal", order.TotalAmount.ToString("N0")))).FontSize(12).Bold().FontColor(Colors.Black);
                        });

                        if (order.Payments != null && order.Payments.Any())
                        {
                            col.Item().PaddingTop(10).Column(c => {
                                c.Item().Text(Reshape(_t.Get("Pdf.PaymentDetails"))).FontSize(8).Bold();
                                foreach (var p in order.Payments)
                                {
                                    var method = p.Method;
                                    if (method == "Cash") method = _t.Get("Pdf.Method.Cash");
                                    else if (method == "Bank" || method == "CreditCard") method = _t.Get("Pdf.Method.Bank");
                                    else if (method == "InstaPay") method = _t.Get("Pdf.Method.InstaPay");
                                    else if (method == "Vodafone") method = _t.Get("Pdf.Method.Vodafone");
                                    else if (method == "Credit") method = _t.Get("Pdf.Method.Credit");

                                    c.Item().Text(Reshape($"- {method}: {p.Amount:N0} ج.م")).FontSize(7);
                                }
                            });
                        }

                        col.Item().PaddingTop(20).AlignCenter().Text(Reshape(_t.Get("Pdf.ThankYou"))).FontSize(10).Bold();
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
        // Sanitize: Limit length
        if (input.Length > 500) input = input.Substring(0, 500);
        // Replace problematic control chars, leaving standard text intact
        return new string(input.Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray());
    }
}
