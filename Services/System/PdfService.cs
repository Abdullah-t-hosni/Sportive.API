using System;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;
using QuestPDF.Previewer;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Data;
using Microsoft.EntityFrameworkCore;
using ArabicReshaper;
using System.Text.RegularExpressions;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace Sportive.API.Services;

public class PdfService : IPdfService
{
    private readonly ITranslator _t;
    private readonly AppDbContext _db;

    public PdfService(ITranslator t, AppDbContext db)
    {
        _t = t;
        _db = db;
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

    private static readonly string Chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private static int GetCharValue(char c)
    {
        int idx = Chars.IndexOf(c);
        return idx >= 0 ? idx : 0;
    }

    private static List<int> EncodeCode128B(string text)
    {
        var codes = new List<int> { 104 };
        int checksum = 104;
        for (int i = 0; i < text.Length; i++)
        {
            int v = GetCharValue(text[i]);
            codes.Add(v);
            checksum += v * (i + 1);
        }
        codes.Add(checksum % 103);
        codes.Add(106);
        return codes;
    }

    private static readonly Dictionary<int, string> BarcodePatterns = new()
    {
        { 0, "11011001100" }, { 1, "11001101100" }, { 2, "11001100110" }, { 3, "10010011000" }, { 4, "10010001100" },
        { 5, "10001001100" }, { 6, "10011001000" }, { 7, "10011000100" }, { 8, "10001100100" }, { 9, "11001001000" },
        { 10, "11001000100" }, { 11, "11000100100" }, { 12, "10110011100" }, { 13, "10011011100" }, { 14, "10011001110" },
        { 15, "10111001100" }, { 16, "10011101100" }, { 17, "10011100110" }, { 18, "11001110010" }, { 19, "11001011100" },
        { 20, "11001001110" }, { 21, "11011100100" }, { 22, "11001110100" }, { 23, "11101101110" }, { 24, "11101001100" },
        { 25, "11100101100" }, { 26, "11100100110" }, { 27, "11101100100" }, { 28, "11100110100" }, { 29, "11100110010" },
        { 30, "11011011000" }, { 31, "11011000110" }, { 32, "11000110110" }, { 33, "10100011000" }, { 34, "10001011000" },
        { 35, "10001000110" }, { 36, "10110001000" }, { 37, "10001101000" }, { 38, "10001100010" }, { 39, "11010001000" },
        { 40, "11000101000" }, { 41, "11000100010" }, { 42, "10110111000" }, { 43, "10110001110" }, { 44, "10001101110" },
        { 45, "10111011000" }, { 46, "10111000110" }, { 47, "10001110110" }, { 48, "11101110110" }, { 49, "11010001110" },
        { 50, "11000101110" }, { 51, "11011101000" }, { 52, "11011100010" }, { 53, "11011101110" }, { 54, "11101011000" },
        { 55, "11101000110" }, { 56, "11100010110" }, { 57, "11101101000" }, { 58, "11101100010" }, { 59, "11100011010" },
        { 60, "11101111010" }, { 61, "11001000010" }, { 62, "11110001010" }, { 63, "10100110000" }, { 64, "10100001100" },
        { 65, "10010110000" }, { 66, "10010000110" }, { 67, "10000101100" }, { 68, "10000100110" }, { 69, "10110010000" },
        { 70, "10110000100" }, { 71, "10011010000" }, { 72, "10011000010" }, { 73, "10000110100" }, { 74, "10000110010" },
        { 75, "11000010010" }, { 76, "11001010000" }, { 77, "11110111010" }, { 78, "11000010100" }, { 79, "10001111010" },
        { 80, "10100111100" }, { 81, "10010111100" }, { 82, "10010011110" }, { 83, "10111100100" }, { 84, "10011110100" },
        { 85, "10011110010" }, { 86, "11110100100" }, { 87, "11110010100" }, { 88, "11110010010" }, { 89, "11011011110" },
        { 90, "11011110110" }, { 91, "11110110110" }, { 92, "10101111000" }, { 93, "10100011110" }, { 94, "10001011110" },
        { 95, "10111101000" }, { 96, "10111100010" }, { 97, "11110101000" }, { 98, "11110100010" }, { 99, "10111011110" },
        { 100, "10111101110" }, { 101, "11101011110" }, { 102, "11110101110" }, { 103, "11010000100" }, { 104, "11010010000" },
        { 105, "11010011100" }, { 106, "1100011101011" }
    };

    public async Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order)
    {
        EnsureFontLoaded();
        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    int paperWidth = settings?.ReceiptWidth ?? 80;
                    page.Size(paperWidth, 297, Unit.Millimetre);
                    page.Margin(4, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    
                    int fs = settings?.ReceiptFontSize ?? 9;
                    page.DefaultTextStyle(x => x.FontSize(fs).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Content().Column(col =>
                    {
                        var sectionsOrder = (settings?.ReceiptSectionsOrder ?? "header,order_info,items_table,totals_area,tafqeet,payment_info,customer_signature,footer_text,terms_conditions,barcode")
                            .Split(',');

                        foreach (var sectionId in sectionsOrder)
                        {
                            var trimmedSection = sectionId.Trim().ToLower();

                            if (trimmedSection == "header")
                            {
                                col.Item().PaddingBottom(4).Column(hCol =>
                                {
                                    // 1. Logo
                                    if (settings?.ReceiptShowLogo == true && !string.IsNullOrEmpty(settings.LogoUrl))
                                    {
                                        try
                                        {
                                            using var client = new HttpClient();
                                            var logoBytes = client.GetByteArrayAsync(settings.LogoUrl).GetAwaiter().GetResult();
                                            float logoWidthMm = paperWidth * (settings.ReceiptLogoWidth / 100f);
                                            hCol.Item().AlignCenter().Width(logoWidthMm, Unit.Millimetre).Image(logoBytes);
                                        }
                                        catch
                                        {
                                            hCol.Item().AlignCenter().Text(settings.StoreBrandName).FontSize(14).Bold().FontColor(Colors.Black);
                                        }
                                    }
                                    else
                                    {
                                        hCol.Item().AlignCenter().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(14).Bold().FontColor(Colors.Black);
                                    }

                                    // 2. Slogan
                                    if (settings != null && !string.IsNullOrEmpty(settings.StoreSlogan))
                                    {
                                        hCol.Item().AlignCenter().Text(settings.StoreSlogan).FontSize(8).FontColor(Colors.Grey.Medium);
                                    }

                                    // 3. Title Badge
                                    var title = order.Source == "1" || order.Source == "POS"
                                        ? (_t.Get("Pdf.SalesInvoiceCashier") ?? "فاتورة مبيعات - كاشير")
                                        : (_t.Get("Pdf.SalesInvoiceOnline") ?? "فاتورة مبيعات - متجر إلكتروني");
                                    
                                    hCol.Item().PaddingTop(2).AlignCenter().Background(Colors.Black).PaddingHorizontal(8).PaddingVertical(1.5f)
                                        .Text(title).FontSize(8).Bold().FontColor(Colors.White);

                                    // 4. Header text
                                    if (settings != null && !string.IsNullOrEmpty(settings.ReceiptHeaderText))
                                    {
                                        hCol.Item().PaddingTop(2).AlignCenter().Text(settings.ReceiptHeaderText).FontSize(8).Bold();
                                    }

                                    // 5. Address / Phone
                                    if (settings?.ReceiptShowAddress == true && !string.IsNullOrEmpty(settings.StorePhysicalAddr))
                                    {
                                        hCol.Item().PaddingTop(1).AlignCenter().Text(settings.StorePhysicalAddr).FontSize(7.5f).Bold();
                                    }
                                    if (settings?.ReceiptShowPhone == true && !string.IsNullOrEmpty(settings.StorePhoneNo))
                                    {
                                        hCol.Item().PaddingTop(0.5f).AlignCenter().Text(settings.StorePhoneNo).FontSize(8).Bold();
                                    }

                                    // 6. Tax No / CR
                                    if (settings != null && (!string.IsNullOrEmpty(settings.TaxNumber) || !string.IsNullOrEmpty(settings.CommercialRegister)))
                                    {
                                        hCol.Item().PaddingTop(1).AlignCenter().Text(x =>
                                        {
                                            x.DefaultTextStyle(style => style.FontSize(7.5f).Bold());
                                            if (!string.IsNullOrEmpty(settings.TaxNumber))
                                                x.Span($"{_t.Get("Pdf.TaxNumber") ?? "الرقم الضريبي"}: {settings.TaxNumber}   ");
                                            if (!string.IsNullOrEmpty(settings.CommercialRegister))
                                                x.Span($"{_t.Get("Pdf.CommercialRegister") ?? "السجل التجاري"}: {settings.CommercialRegister}");
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "order_info")
                            {
                                col.Item().PaddingVertical(2).BorderTop(1).BorderBottom(1).BorderColor(Colors.Black).Column(infoCol =>
                                {
                                    // Order Number and Date
                                    infoCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text($"{_t.Get("Pdf.Number") ?? "الرقم"}: #{order.OrderNumber}").Bold().FontSize(8.5f);
                                        row.RelativeItem().AlignLeft().Text(order.CreatedAt.ToString("yyyy/MM/dd HH:mm")).FontSize(8);
                                    });

                                    // Customer Details
                                    if (settings?.ReceiptShowCustomerDetails == true)
                                    {
                                        var custName = order.Customer?.FullName ?? _t.Get("Pdf.CashCustomer") ?? "عميل نقدي";
                                        infoCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text($"{_t.Get("Pdf.Customer") ?? "العميل"}: {custName}").Bold().FontSize(8.5f);
                                            if (!string.IsNullOrEmpty(order.Customer?.Phone))
                                                row.RelativeItem().AlignLeft().Text(order.Customer.Phone).FontSize(8).Bold();
                                        });
                                    }

                                    // Cashier & Status
                                    infoCol.Item().Row(row =>
                                    {
                                        if (settings?.ReceiptShowCashier != false && !string.IsNullOrEmpty(order.SalesPersonName))
                                            row.RelativeItem().Text($"{_t.Get("Pdf.Seller") ?? "البائع"}: {order.SalesPersonName}").FontSize(7.5f).Bold();
                                        else
                                            row.RelativeItem().Text("");

                                        row.RelativeItem().AlignLeft().Text($"{_t.Get("Pdf.Status") ?? "الحالة"}: {order.Status}").FontSize(7.5f).Bold();
                                    });

                                    // Note
                                    if (settings?.ReceiptShowNote != false && !string.IsNullOrEmpty(order.CustomerNotes))
                                    {
                                        infoCol.Item().PaddingTop(2).Border(0.5f).BorderColor(Colors.Black).Padding(3)
                                            .Text($"{_t.Get("Pdf.Note") ?? "ملاحظة"}: {order.CustomerNotes}").FontSize(7.5f).Bold();
                                    }
                                });
                            }
                            else if (trimmedSection == "items_table")
                            {
                                col.Item().PaddingBottom(2).Column(itemsCol =>
                                {
                                    foreach (var item in order.Items)
                                    {
                                        itemsCol.Item().PaddingVertical(2).Column(itemCol =>
                                        {
                                            // Item Name
                                            var name = item.ProductNameAr;
                                            itemCol.Item().Text(name).Bold().FontSize(8.5f);

                                            // Qty x Price and Line Total
                                            itemCol.Item().Row(row =>
                                            {
                                                var qtyPriceText = settings?.ReceiptShowUnitPrice != false
                                                    ? $"{item.Quantity} × {item.UnitPrice:N2}"
                                                    : $"{item.Quantity}";
                                                row.RelativeItem().Text(qtyPriceText).FontSize(7.5f).FontColor(Colors.Black);
                                                row.RelativeItem().AlignLeft().Text($"{item.TotalPrice:N2}").Bold().FontSize(8.5f);
                                            });

                                            // SKU, Size, Color Badges
                                            var badges = new List<string>();
                                            if (settings?.ReceiptShowSKU != false && !string.IsNullOrEmpty(item.SKU))
                                                badges.Add($"#{item.SKU}");
                                            if (!string.IsNullOrEmpty(item.Size))
                                                badges.Add(item.Size);
                                            if (!string.IsNullOrEmpty(item.Color))
                                                badges.Add(item.Color);

                                            if (badges.Any())
                                            {
                                                itemCol.Item().PaddingTop(1).Row(row =>
                                                {
                                                    foreach (var badge in badges)
                                                    {
                                                        row.AutoItem().PaddingLeft(3).Border(0.5f).BorderColor(Colors.Black).PaddingHorizontal(2).PaddingVertical(0.5f)
                                                            .Text(badge).FontSize(6.5f).Bold();
                                                    }
                                                });
                                            }
                                        });

                                        itemsCol.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                                    }
                                });
                            }
                            else if (trimmedSection == "totals_area")
                            {
                                col.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(2).Column(totCol =>
                                {
                                    // Subtotal
                                    var totalSavings = order.DiscountAmount + order.TemporalDiscount;
                                    if (totalSavings > 0 && settings?.ReceiptShowDiscount != false)
                                    {
                                        totCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text(_t.Get("Pdf.SubTotal") ?? "الإجمالي الفرعي").FontSize(8).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{(order.TotalAmount + totalSavings):N2}").FontSize(8).Bold();
                                        });
                                        totCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text("إجمالي الخصومات").FontSize(8).Bold().FontColor(Colors.Red.Medium);
                                            row.RelativeItem().AlignLeft().Text($"-{totalSavings:N2}").FontSize(8).Bold().FontColor(Colors.Red.Medium);
                                        });
                                    }

                                    // VAT
                                    var totalVat = order.Items.Sum(i => i.ItemVatAmount);
                                    if (totalVat > 0 && settings?.ReceiptShowTax != false)
                                    {
                                        var commonVatItem = order.Items.FirstOrDefault(i => i.VatRateApplied > 0);
                                        var vatRate = commonVatItem?.VatRateApplied ?? settings.VatRatePercent;
                                        totCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text($"ضريبة القيمة المضافة ({vatRate}%)").FontSize(8).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{totalVat:N2}").FontSize(8).Bold();
                                        });
                                    }

                                    // Net Total Box
                                    totCol.Item().PaddingVertical(2).Border(1.5f).BorderColor(Colors.Black).Padding(4).Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.NetTotal") ?? "الصافي").Bold().FontSize(10.5f);
                                        row.RelativeItem().AlignLeft().Text($"{order.TotalAmount:N2} {settings?.CurrencySymbol ?? "ج.م"}").Bold().FontSize(10.5f);
                                    });

                                    // Item/Piece Counts
                                    totCol.Item().Row(row =>
                                    {
                                        if (settings?.ReceiptShowItemCount != false)
                                            row.RelativeItem().Text($"إجمالي عدد الأصناف: {order.Items.Count}").FontSize(7.5f).Bold();
                                        
                                        if (settings?.ReceiptShowTotalPieceCount != false)
                                        {
                                            var totalQty = order.Items.Sum(i => i.Quantity);
                                            row.RelativeItem().AlignLeft().Text($"إجمالي الكمية: {totalQty}").FontSize(7.5f).Bold();
                                        }
                                    });

                                    // Previous Balance
                                    if (settings?.ReceiptShowBalance != false && order.PreviousBalance != 0)
                                    {
                                        totCol.Item().PaddingTop(1).Row(row =>
                                        {
                                            row.RelativeItem().Text(_t.Get("Pdf.PreviousBalance") ?? "الرصيد السابق").FontSize(8).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{order.PreviousBalance:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(8).Bold();
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "tafqeet")
                            {
                                if (!string.IsNullOrEmpty(order.TotalAmountInWords))
                                {
                                    col.Item().PaddingVertical(2).AlignCenter().Text($"« {order.TotalAmountInWords} »").FontSize(7.5f).Bold();
                                }
                            }
                            else if (trimmedSection == "payment_info")
                            {
                                col.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(2).Column(payCol =>
                                {
                                    // Paid
                                    payCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.AmountPaid") ?? "المبلغ المدفوع").FontSize(8).Bold();
                                        row.RelativeItem().AlignLeft().Text($"{order.PaidAmount:N2}").FontSize(8).Bold();
                                    });

                                    // Remaining
                                    var remaining = order.TotalAmount - order.PaidAmount;
                                    if (remaining > 0)
                                    {
                                        payCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text(_t.Get("Pdf.Remaining") ?? "المتبقي").FontSize(8).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{remaining:N2}").FontSize(8).Bold();
                                        });
                                    }

                                    // Method
                                    payCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.PaymentMethod") ?? "طريقة الدفع").FontSize(8).Bold();
                                        row.AutoItem().Border(0.5f).BorderColor(Colors.Black).PaddingHorizontal(3).PaddingVertical(0.5f)
                                            .Text(order.PaymentMethod).FontSize(7).Bold();
                                    });

                                    // Current Balance
                                    if (settings?.ReceiptShowBalance != false && order.Customer != null)
                                    {
                                        decimal customerBalance = 0;
                                        try
                                        {
                                            var customer = _db.Customers.FirstOrDefault(c => c.Id == order.Customer.Id);
                                            if (customer != null)
                                            {
                                                customerBalance = customer.Balance;
                                            }
                                        }
                                        catch { }

                                        payCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text("رصيد العميل الحالي").FontSize(8).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{customerBalance:N2}").FontSize(8).Bold();
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "footer_text")
                            {
                                col.Item().BorderTop(1).BorderColor(Colors.Black).PaddingTop(2).Column(fCol =>
                                {
                                    if (!string.IsNullOrEmpty(settings?.ReceiptFooterText))
                                    {
                                        fCol.Item().AlignCenter().Text(settings.ReceiptFooterText).FontSize(8.5f).Bold();
                                    }
                                    if (!string.IsNullOrEmpty(settings?.ReceiptComplaintsPhone))
                                    {
                                        fCol.Item().PaddingTop(2).AlignCenter().Background(Colors.Black).PaddingHorizontal(6).PaddingVertical(1)
                                            .Text($"{_t.Get("Pdf.Complaints") ?? "شكاوى"}: {settings.ReceiptComplaintsPhone}").FontSize(7.5f).Bold().FontColor(Colors.White);
                                    }
                                    fCol.Item().PaddingTop(2).AlignCenter().Text(settings?.ReceiptSoftwareProvider ?? "By Sportive Team").FontSize(6.5f).Bold().FontColor(Colors.Grey.Darken1);
                                });
                            }
                            else if (trimmedSection == "terms_conditions")
                            {
                                if (!string.IsNullOrEmpty(settings?.ReceiptTermsAndConditions))
                                {
                                    col.Item().PaddingTop(4).Border(0.5f).BorderColor(Colors.Black).Padding(3)
                                        .Text(settings.ReceiptTermsAndConditions).FontSize(6.5f).FontColor(Colors.Grey.Darken4).LineHeight(1.3f);
                                }
                            }
                            else if (trimmedSection == "barcode")
                            {
                                if (settings?.ReceiptShowBarcode != false)
                                {
                                    var barcodeText = order.OrderNumber ?? order.Id.ToString();
                                    col.Item().PaddingTop(4).AlignCenter().Column(barCol =>
                                    {
                                        barCol.Item().Width(150).Height(25).Element(barContainer =>
                                        {
                                            var codes = EncodeCode128B(barcodeText);
                                            var bitString = new StringBuilder();
                                            foreach (int code in codes)
                                            {
                                                if (BarcodePatterns.TryGetValue(code, out string? pattern))
                                                    bitString.Append(pattern);
                                            }
                                            int totalSlots = bitString.Length;
                                            float moduleW = 150f / totalSlots;
                                            barContainer.Row(row =>
                                            {
                                                foreach (char bit in bitString.ToString())
                                                {
                                                    if (bit == '1')
                                                        row.ConstantItem(moduleW).Height(25).Background(Colors.Black);
                                                    else
                                                        row.ConstantItem(moduleW).Height(25).Background(Colors.White);
                                                }
                                            });
                                            return barContainer;
                                        });
                                        barCol.Item().AlignCenter().Text(barcodeText).FontSize(7.5f).Bold();
                                    });
                                }
                            }
                        }
                    });
                });
            }).GeneratePdf();
        });
    }

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
