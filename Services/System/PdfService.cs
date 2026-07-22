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
            
            string regPath = Path.Combine(fontDir, "Cairo-Regular.ttf");
            string boldPath = Path.Combine(fontDir, "Cairo-Bold.ttf");
            
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                
                // 1. Download Regular if it doesn't exist or if it's the wrong size (indicating it's the variable font ~600KB or corrupted)
                bool needReg = !File.Exists(regPath);
                if (!needReg)
                {
                    var info = new FileInfo(regPath);
                    if (info.Length > 150000 || info.Length < 30000) // Static Cairo-Regular is ~75KB
                    {
                        try { File.Delete(regPath); } catch {}
                        needReg = true;
                    }
                }
                if (needReg)
                {
                    var bytes = client.GetByteArrayAsync("https://cdn.jsdelivr.net/fontsource/fonts/cairo@latest/arabic-400-normal.ttf").GetAwaiter().GetResult();
                    File.WriteAllBytes(regPath, bytes);
                }

                // 2. Download Bold if it doesn't exist or if it's the wrong size
                bool needBold = !File.Exists(boldPath);
                if (!needBold)
                {
                    var info = new FileInfo(boldPath);
                    if (info.Length > 150000 || info.Length < 30000) // Static Cairo-Bold is ~115KB
                    {
                        try { File.Delete(boldPath); } catch {}
                        needBold = true;
                    }
                }
                if (needBold)
                {
                    var bytes = client.GetByteArrayAsync("https://cdn.jsdelivr.net/fontsource/fonts/cairo@latest/arabic-700-normal.ttf").GetAwaiter().GetResult();
                    File.WriteAllBytes(boldPath, bytes);
                }
            }

            bool regRegistered = false;
            if (File.Exists(regPath))
            {
                using (var stream = File.OpenRead(regPath))
                {
                    QuestPDF.Drawing.FontManager.RegisterFont(stream);
                }
                regRegistered = true;
            }
            if (File.Exists(boldPath))
            {
                using (var stream = File.OpenRead(boldPath))
                {
                    QuestPDF.Drawing.FontManager.RegisterFont(stream);
                }
            }

            if (regRegistered)
            {
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
            int paperWidth = settings?.ReceiptWidth ?? 80;
            if (paperWidth == 80) paperWidth = 72;
            else if (paperWidth == 58) paperWidth = 48;
            
            // Estimate height dynamically based on items count
            int itemCount = order.Items?.Count ?? 0;
            // Base height: header, totals, payment details, footer ~200mm. Per item: ~15mm.
            int estimatedHeight = Math.Max(180, 200 + (itemCount * 15));
            // Cap at 1500mm to prevent Skia bitmap allocation crash (> 32,767 pixels)
            int paperHeight = Math.Min(1500, estimatedHeight);

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(paperWidth, paperHeight, Unit.Millimetre);
                    page.MarginHorizontal(0.5f, Unit.Millimetre);
                    page.MarginVertical(2, Unit.Millimetre);
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
                                            hCol.Item().AlignCenter().Text(CleanKashida(settings.StoreBrandName)).FontSize(14).Bold().FontColor(Colors.Black);
                                        }
                                    }
                                    else
                                    {
                                        hCol.Item().AlignCenter().Text(CleanKashida(settings?.StoreBrandName ?? "SPORTIVE")).FontSize(14).Bold().FontColor(Colors.Black);
                                    }

                                    // 2. Slogan
                                    if (settings != null && !string.IsNullOrEmpty(settings.StoreSlogan))
                                    {
                                        hCol.Item().PaddingTop(1).AlignCenter().Text(CleanKashida(settings.StoreSlogan)).FontSize(10.5f).Bold().FontColor(Colors.Black);
                                    }

                                    // 3. Title Badge
                                    var title = order.Source == "1" || order.Source == "POS"
                                        ? (_t.Get("Pdf.SalesInvoiceCashier") ?? "فاتورة مبيعات - كاشير")
                                        : (_t.Get("Pdf.SalesInvoiceOnline") ?? "فاتورة مبيعات - متجر إلكتروني");
                                    
                                    hCol.Item().PaddingTop(2).AlignCenter().Background(Colors.Black).PaddingHorizontal(8).PaddingVertical(2f)
                                        .Text(title).FontSize(10f).Bold().FontColor(Colors.White);

                                    // 4. Header text
                                    if (settings != null && !string.IsNullOrEmpty(settings.ReceiptHeaderText))
                                    {
                                        hCol.Item().PaddingTop(2).AlignCenter().Text(CleanKashida(settings.ReceiptHeaderText)).FontSize(10f).Bold();
                                    }

                                    // 5. Address / Phone
                                    if (settings?.ReceiptShowAddress == true && !string.IsNullOrEmpty(settings.StorePhysicalAddr))
                                    {
                                        hCol.Item().PaddingTop(1).AlignCenter().Text(CleanKashida(settings.StorePhysicalAddr)).FontSize(10f).Bold();
                                    }
                                    if (settings?.ReceiptShowPhone == true && !string.IsNullOrEmpty(settings.StorePhoneNo))
                                    {
                                        hCol.Item().PaddingTop(0.5f).AlignCenter().Text(settings.StorePhoneNo).FontSize(10.5f).Bold();
                                    }

                                    // 6. Tax No / CR
                                    if (settings != null && (!string.IsNullOrEmpty(settings.TaxNumber) || !string.IsNullOrEmpty(settings.CommercialRegister)))
                                    {
                                        hCol.Item().PaddingTop(1).AlignCenter().Text(x =>
                                        {
                                            x.DefaultTextStyle(style => style.FontSize(9.5f).Bold());
                                            if (!string.IsNullOrEmpty(settings.TaxNumber))
                                                x.Span($"{_t.Get("Pdf.TaxNumber")}: {settings.TaxNumber}   ");
                                            if (!string.IsNullOrEmpty(settings.CommercialRegister))
                                                x.Span($"{_t.Get("Pdf.CommercialRegister")}: {settings.CommercialRegister}");
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "order_info")
                            {
                                col.Item().PaddingVertical(2).BorderTop(1.5f).BorderBottom(1.5f).BorderColor(Colors.Black).Column(infoCol =>
                                {
                                    // Order Number and Date
                                    infoCol.Item().Row(row =>
                                    {
                                        row.RelativeItem(3).Text(_t.Get("Pdf.Number", order.OrderNumber ?? "")).Bold().FontSize(10.5f);
                                        row.RelativeItem(2).AlignLeft().Text(order.CreatedAt.ToString("yyyy/MM/dd HH:mm")).FontSize(10.5f).Bold();
                                    });

                                    // Customer Details
                                    if (settings?.ReceiptShowCustomerDetails == true)
                                    {
                                        var custName = !string.IsNullOrWhiteSpace(order.Customer?.FullName) 
                                             ? order.Customer.FullName 
                                             : (order.Source == "3" || order.Source == "Website" ? "عميل متجر أونلاين" : (_t.Get("Pdf.CashCustomer") ?? "عميل نقدي"));

                                         var phone = order.Customer?.Phone;

                                         infoCol.Item().PaddingTop(1).Row(row =>
                                         {
                                             row.RelativeItem(3).Text(_t.Get("Pdf.Customer", custName)).Bold().FontSize(10.5f);
                                             if (!string.IsNullOrWhiteSpace(phone))
                                                 row.RelativeItem(2).AlignLeft().Text(phone).FontSize(10.5f).Bold();
                                         });

                                        // Delivery Address Block
                                        if (order.DeliveryAddress != null)
                                        {
                                            var addr = order.DeliveryAddress;
                                            var parts = new List<string>();
                                            if (!string.IsNullOrWhiteSpace(addr.Street)) parts.Add(addr.Street);
                                            if (!string.IsNullOrWhiteSpace(addr.District)) parts.Add(addr.District);
                                            if (!string.IsNullOrWhiteSpace(addr.City)) parts.Add(addr.City);
                                            if (!string.IsNullOrWhiteSpace(addr.BuildingNo)) parts.Add($"عمارة {addr.BuildingNo}");
                                            if (!string.IsNullOrWhiteSpace(addr.Floor)) parts.Add($"دور {addr.Floor}");
                                            if (!string.IsNullOrWhiteSpace(addr.ApartmentNo)) parts.Add($"شقة {addr.ApartmentNo}");
                                            var fullAddr = string.Join(" - ", parts);
                                            if (!string.IsNullOrWhiteSpace(fullAddr))
                                            {
                                                infoCol.Item().PaddingTop(2).Border(0.6f).BorderColor(Colors.Black).PaddingHorizontal(5).PaddingVertical(3)
                                                    .Text(x =>
                                                    {
                                                        x.DefaultTextStyle(style => style.FontSize(9.5f).LineHeight(1.2f).FontColor(Colors.Black));
                                                        x.Span("عنوان التوصيل: ").Bold();
                                                        x.Span(fullAddr);
                                                    });
                                            }
                                        }
                                    }

                                    // Cashier & Status
                                    infoCol.Item().PaddingTop(1).Row(row =>
                                    {
                                        if (settings?.ReceiptShowCashier != false && !string.IsNullOrEmpty(order.SalesPersonName))
                                            row.RelativeItem().Text(_t.Get("Pdf.Seller", order.SalesPersonName ?? "")).FontSize(10f).Bold();
                                        else
                                            row.RelativeItem().Text("");

                                        var statusKey = $"Status.{order.Status ?? ""}";
                                        var translatedStatus = _t.Get(statusKey);
                                        if (translatedStatus == statusKey)
                                        {
                                            translatedStatus = order.Status ?? "";
                                        }
                                        row.RelativeItem().AlignLeft().Text(_t.Get("Pdf.Status", translatedStatus)).FontSize(10f).Bold();
                                    });

                                    // Note
                                    if (settings?.ReceiptShowNote != false && !string.IsNullOrEmpty(order.CustomerNotes))
                                    {
                                        infoCol.Item().PaddingTop(2).Border(0.6f).BorderColor(Colors.Black).PaddingHorizontal(5).PaddingVertical(3)
                                            .Text(x =>
                                            {
                                                x.DefaultTextStyle(style => style.FontSize(10f).FontColor(Colors.Black));
                                                x.Span(_t.Get("Pdf.Note", "").Replace(" {0}", "").Replace("{0}", "")).Bold();
                                                x.Span(order.CustomerNotes ?? "");
                                            });
                                    }
                                });
                            }
                            else if (trimmedSection == "items_table")
                            {
                                col.Item().PaddingBottom(2).Column(itemsCol =>
                                {
                                    foreach (var item in order.Items)
                                    {
                                        itemsCol.Item().PaddingVertical(3).Column(itemCol =>
                                        {
                                            // Item Name
                                            var name = item.ProductNameAr;
                                            itemCol.Item().Text(name).Bold().FontSize(11f).LineHeight(1.15f);

                                            // Qty x Price and Line Total
                                            itemCol.Item().PaddingTop(1).Row(row =>
                                            {
                                                var qtyPriceText = settings?.ReceiptShowUnitPrice != false
                                                    ? $"{item.Quantity} × {item.UnitPrice:N2} {settings?.CurrencySymbol ?? "ج.م"}"
                                                    : $"{item.Quantity}";
                                                row.RelativeItem(3).Text(qtyPriceText).FontSize(10.5f).FontColor(Colors.Black);
                                                row.RelativeItem(2).AlignLeft().Text($"{item.TotalPrice:N2} {settings?.CurrencySymbol ?? "ج.م"}").Bold().FontSize(11f);
                                            });

                                            // SKU, Size, Color text line
                                            var badgeParts = new List<string>();
                                            if (settings?.ReceiptShowSKU != false && !string.IsNullOrEmpty(item.SKU))
                                                badgeParts.Add($"#{item.SKU}");
                                            if (!string.IsNullOrEmpty(item.Size))
                                                badgeParts.Add($"المقاس: {item.Size}");
                                            if (!string.IsNullOrEmpty(item.Color))
                                                badgeParts.Add($"اللون: {item.Color}");

                                            if (badgeParts.Any())
                                            {
                                                itemCol.Item().PaddingTop(1).Text(string.Join("   |   ", badgeParts)).FontSize(9.5f).FontColor(Colors.Black);
                                            }
                                        });

                                        itemsCol.Item().BorderBottom(0.6f).BorderColor(Colors.Black);
                                    }
                                });
                            }
                            else if (trimmedSection == "totals_area")
                            {
                                col.Item().BorderTop(1.5f).BorderColor(Colors.Black).PaddingTop(2).Column(totCol =>
                                {
                                    // Subtotal
                                    totCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.SubTotal")).FontSize(10.5f).Bold();
                                        row.RelativeItem().AlignLeft().Text($"{order.SubTotal:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                    });

                                    // Discount
                                    var totalSavings = order.DiscountAmount + order.TemporalDiscount;
                                    totCol.Item().Row(row =>
                                    {
                                        var discountLabel = _t.Get("Pdf.TotalDiscount");
                                        var discountDetails = new List<string>();
                                        if (!string.IsNullOrEmpty(order.CouponCode))
                                            discountDetails.Add($"كوبون: {order.CouponCode}");
                                        if (order.TemporalDiscount > 0)
                                            discountDetails.Add("عروض");
                                        if (order.DiscountAmount > 0 && string.IsNullOrEmpty(order.CouponCode))
                                            discountDetails.Add("خصم إضافي");

                                        if (discountDetails.Any())
                                            discountLabel += $" ({string.Join(" + ", discountDetails)})";

                                        row.RelativeItem().Text(discountLabel).FontSize(10.5f).Bold();
                                        row.RelativeItem().AlignLeft().Text($"خصم {totalSavings:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold().FontColor(Colors.Black);
                                    });

                                    // Delivery Fee
                                    totCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.DeliveryFee")).FontSize(10.5f).Bold();
                                        row.RelativeItem().AlignLeft().Text($"{order.DeliveryFee:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                    });

                                    // VAT
                                    var totalVat = order.Items.Sum(i => i.ItemVatAmount);
                                    if (totalVat > 0 && settings?.ReceiptShowTax != false)
                                    {
                                        var commonVatItem = order.Items.FirstOrDefault(i => i.VatRateApplied > 0);
                                        var vatRate = commonVatItem?.VatRateApplied ?? settings?.VatRatePercent ?? 0;
                                        totCol.Item().Row(row =>
                                        {
                                            row.RelativeItem().Text(_t.Get("Pdf.Vat", vatRate)).FontSize(10.5f).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{totalVat:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                        });
                                    }

                                    // Net Total Box
                                    totCol.Item().PaddingVertical(3).Border(1.5f).BorderColor(Colors.Black).PaddingHorizontal(5).PaddingVertical(3).Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.NetTotal")).Bold().FontSize(12f);
                                        row.RelativeItem().AlignLeft().Text($"{order.TotalAmount:N2} {settings?.CurrencySymbol ?? "ج.م"}").Bold().FontSize(12f);
                                    });

                                    // Item/Piece Counts
                                    totCol.Item().Row(row =>
                                    {
                                        if (settings?.ReceiptShowItemCount != false)
                                            row.RelativeItem().Text(_t.Get("Pdf.ItemsCount", order.Items.Count)).FontSize(10f).Bold();
                                        
                                        if (settings?.ReceiptShowTotalPieceCount != false)
                                        {
                                            var totalQty = order.Items.Sum(i => i.Quantity);
                                            row.RelativeItem().AlignLeft().Text(_t.Get("Pdf.TotalQty", totalQty)).FontSize(10f).Bold();
                                        }
                                    });

                                    // Previous Balance
                                    if (settings?.ReceiptShowBalance != false && order.PreviousBalance != 0)
                                    {
                                        totCol.Item().PaddingTop(1).Row(row =>
                                        {
                                            row.RelativeItem().Text(_t.Get("Pdf.PreviousBalance")).FontSize(10.5f).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{order.PreviousBalance:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "tafqeet")
                            {
                                if (!string.IsNullOrEmpty(order.TotalAmountInWords))
                                {
                                    col.Item().PaddingVertical(2).AlignCenter().Text($"« {order.TotalAmountInWords} »").FontSize(10.5f).Bold();
                                }
                            }
                            else if (trimmedSection == "payment_info")
                            {
                                col.Item().BorderTop(1.5f).BorderColor(Colors.Black).PaddingTop(2).Column(payCol =>
                                {
                                    // Paid
                                    payCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.AmountPaid")).FontSize(10.5f).Bold();
                                        row.RelativeItem().AlignLeft().Text($"{order.PaidAmount:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                    });

                                    // Remaining
                                    var remaining = order.TotalAmount - order.PaidAmount;
                                    payCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.Remaining")).FontSize(10.5f).Bold();
                                        row.RelativeItem().AlignLeft().Text($"{remaining:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                    });

                                    // Method
                                    payCol.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(_t.Get("Pdf.PaymentMethod")).FontSize(10.5f).Bold();
                                        
                                        var methodKey = $"Pdf.Method.{order.PaymentMethod}";
                                        var translatedMethod = _t.Get(methodKey);
                                        if (translatedMethod == methodKey)
                                        {
                                            translatedMethod = order.PaymentMethod;
                                        }

                                        row.AutoItem().Border(1.2f).BorderColor(Colors.Black).PaddingHorizontal(5).PaddingVertical(1f)
                                            .Text(translatedMethod).FontSize(10.5f).Bold();
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
                                            row.RelativeItem().Text(_t.Get("Pdf.CurrentBalance")).FontSize(10.5f).Bold();
                                            row.RelativeItem().AlignLeft().Text($"{customerBalance:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(10.5f).Bold();
                                        });
                                    }
                                });
                            }
                            else if (trimmedSection == "footer_text")
                            {
                                col.Item().BorderTop(1.5f).BorderColor(Colors.Black).PaddingTop(3).Column(fCol =>
                                {
                                    if (!string.IsNullOrEmpty(settings?.ReceiptFooterText))
                                    {
                                        fCol.Item().AlignCenter().Text(CleanKashida(settings.ReceiptFooterText)).FontSize(10.5f).Bold();
                                    }
                                    if (!string.IsNullOrEmpty(settings?.ReceiptComplaintsPhone))
                                    {
                                        fCol.Item().PaddingTop(2).AlignCenter().Background(Colors.Black).PaddingHorizontal(8).PaddingVertical(2)
                                            .Text(_t.Get("Pdf.Complaints", settings?.ReceiptComplaintsPhone ?? "")).FontSize(10.5f).Bold().FontColor(Colors.White);
                                    }
                                    fCol.Item().PaddingTop(2).AlignCenter().Text(settings?.ReceiptSoftwareProvider ?? "By Sportive Team").FontSize(9.5f).Bold().FontColor(Colors.Black);
                                });
                            }
                            else if (trimmedSection == "terms_conditions")
                            {
                                if (!string.IsNullOrEmpty(settings?.ReceiptTermsAndConditions))
                                {
                                    col.Item().PaddingTop(4).Border(1f).BorderColor(Colors.Black).Padding(4)
                                        .Text(CleanKashida(settings.ReceiptTermsAndConditions)).FontSize(10f).Bold().FontColor(Colors.Black).LineHeight(1.3f);
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
                                        barCol.Item().AlignCenter().Text(barcodeText).FontSize(10f).Bold();
                                    });
                                }
                            }
                        }
                    });
                });
            });

            // Convert to Images and rebuild as a SINGLE flat PDF page
            var images = doc.GenerateImages();
            int finalPaperWidth = settings?.ReceiptWidth ?? 80;
            if (finalPaperWidth == 80) finalPaperWidth = 72;
            else if (finalPaperWidth == 58) finalPaperWidth = 48;

            // Calculate total actual height: each image is a PNG with known pixel dimensions
            float totalHeightMm = 0;
            var imageList = images.ToList();
            foreach (var imgBytes in imageList)
            {
                // QuestPDF generates PNG images — read dimensions from PNG header
                // PNG: bytes 16-19 = width, 20-23 = height (big-endian)
                int imgWidthPx  = (imgBytes[16] << 24) | (imgBytes[17] << 16) | (imgBytes[18] << 8) | imgBytes[19];
                int imgHeightPx = (imgBytes[20] << 24) | (imgBytes[21] << 16) | (imgBytes[22] << 8) | imgBytes[23];
                if (imgWidthPx > 0)
                {
                    float aspectRatio = (float)imgHeightPx / imgWidthPx;
                    totalHeightMm += finalPaperWidth * aspectRatio;
                }
            }

            // Add generous bottom padding (18mm) so physical printer cutter NEVER cuts off footer text or URLs!
            totalHeightMm += 18f;

            var finalDoc = Document.Create(finalContainer =>
            {
                finalContainer.Page(page =>
                {
                    page.Size(finalPaperWidth, totalHeightMm, Unit.Millimetre);
                    page.Margin(0);
                    page.Content().Column(col =>
                    {
                        foreach (var img in imageList)
                        {
                            col.Item().Image(img);
                        }
                    });
                });
            });

            return finalDoc.GeneratePdf();
        });
    }

    public async Task<byte[]> GeneratePurchaseInvoicePdfAsync(PurchaseInvoice invoice)
    {
        EnsureFontLoaded();
        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(storeCol =>
                            {
                                storeCol.Item().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(16).Bold().FontColor(Colors.Black);
                                if (!string.IsNullOrEmpty(settings?.StoreSlogan))
                                    storeCol.Item().Text(settings.StoreSlogan).FontSize(9).FontColor(Colors.Grey.Medium);
                                if (!string.IsNullOrEmpty(settings?.TaxNumber))
                                    storeCol.Item().Text($"الرقم الضريبي: {settings.TaxNumber}").FontSize(8.5f);
                                if (!string.IsNullOrEmpty(settings?.CommercialRegister))
                                    storeCol.Item().Text($"السجل التجاري: {settings.CommercialRegister}").FontSize(8.5f);
                            });

                            row.RelativeItem().AlignLeft().Column(titleCol =>
                            {
                                titleCol.Item().Text("فاتورة مشتريات").FontSize(18).Bold().FontColor(Colors.Black);
                                titleCol.Item().PaddingTop(2).Text($"رقم الفاتورة: {invoice.InvoiceNumber}").Bold().FontSize(10);
                                if (!string.IsNullOrEmpty(invoice.SupplierInvoiceNumber))
                                    titleCol.Item().Text($"فاتورة المورد: {invoice.SupplierInvoiceNumber}").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        // Info section (Supplier details vs Invoice metadata)
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(suppCol =>
                            {
                                suppCol.Item().Text("بيانات المورد").Bold().FontSize(10.5f).Underline();
                                suppCol.Item().PaddingTop(4).Text($"الاسم: {invoice.Supplier?.Name}").Bold();
                                if (!string.IsNullOrEmpty(invoice.Supplier?.Phone))
                                    suppCol.Item().Text($"الهاتف: {invoice.Supplier.Phone}");
                                if (!string.IsNullOrEmpty(invoice.Supplier?.TaxNumber))
                                    suppCol.Item().Text($"الرقم الضريبي للمورد: {invoice.Supplier.TaxNumber}");
                                if (!string.IsNullOrEmpty(invoice.Supplier?.Address))
                                    suppCol.Item().Text($"العنوان: {invoice.Supplier.Address}");
                            });

                            row.RelativeItem().AlignLeft().Column(metaCol =>
                            {
                                metaCol.Item().Text("تفاصيل المستند").Bold().FontSize(10.5f).Underline();
                                metaCol.Item().PaddingTop(4).Text($"التاريخ: {invoice.InvoiceDate:yyyy/MM/dd}");
                                if (invoice.DueDate.HasValue)
                                    metaCol.Item().Text($"تاريخ الاستحقاق: {invoice.DueDate.Value:yyyy/MM/dd}");
                                metaCol.Item().Text($"طريقة الدفع: {(invoice.PaymentTerms == PaymentTerms.Cash ? "نقدي" : "آجل")}");
                                metaCol.Item().Text($"حالة الفاتورة: {invoice.Status}");
                                if (invoice.CostCenter.HasValue)
                                    metaCol.Item().Text($"مركز التكلفة: {invoice.CostCenter}");
                            });
                        });

                        col.Item().PaddingTop(15);

                        // Items Table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30); // #
                                columns.RelativeColumn(3);  // Product Name
                                columns.RelativeColumn(1.5f); // SKU
                                columns.RelativeColumn(1.2f); // Quantity
                                columns.RelativeColumn(1.2f); // Unit Cost
                                columns.RelativeColumn(1);   // Tax %
                                columns.RelativeColumn(1.5f); // Total
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("الصنف").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكود").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكمية").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("سعر الوحدة").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الضريبة").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignLeft().Text("الإجمالي").Bold();
                            });

                            int index = 1;
                            foreach (var item in invoice.Items)
                            {
                                var cellStyle = TextStyle.Default.FontSize(8.5f);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(index++.ToString()).Style(cellStyle);
                                
                                var productName = item.Product?.NameAr ?? item.Description;
                                if (item.ProductVariant != null)
                                {
                                    var variantDetails = new List<string>();
                                    if (!string.IsNullOrEmpty(item.ProductVariant.Size)) variantDetails.Add(item.ProductVariant.Size);
                                    if (!string.IsNullOrEmpty(item.ProductVariant.ColorAr)) variantDetails.Add(item.ProductVariant.ColorAr);
                                    else if (!string.IsNullOrEmpty(item.ProductVariant.Color)) variantDetails.Add(item.ProductVariant.Color);
                                    
                                    if (variantDetails.Any())
                                        productName += $" ({string.Join(" - ", variantDetails)})";
                                }
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(productName).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.Product?.SKU ?? "—").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text($"{item.Quantity} {item.Unit}").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.UnitCost.ToString("N2")).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text($"{item.TaxRate}%").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignLeft().Text(item.TotalCost.ToString("N2")).Style(cellStyle);
                            }
                        });

                        col.Item().PaddingTop(15);

                        // Totals block & Tafqeet
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(2).Column(tafCol =>
                            {
                                var tafqeet = CurrencyHelper.ToArabicWords(invoice.TotalAmount);
                                tafCol.Item().Padding(4).Background(Colors.Grey.Lighten4).Text(tafqeet).Bold().FontSize(9);
                                if (!string.IsNullOrEmpty(invoice.Notes))
                                {
                                    tafCol.Item().PaddingTop(8).Text($"ملاحظات: {invoice.Notes}").FontSize(8.5f);
                                }
                            });

                            row.RelativeItem(1.5f).Column(totCol =>
                            {
                                totCol.Item().Row(r => { r.RelativeItem().Text("الإجمالي الفرعي:"); r.RelativeItem().AlignLeft().Text(invoice.SubTotal.ToString("N2")); });
                                if (invoice.DiscountAmount > 0)
                                    totCol.Item().Row(r => { r.RelativeItem().Text("الخصم:"); r.RelativeItem().AlignLeft().Text($"-{invoice.DiscountAmount:N2}"); });
                                if (invoice.TaxAmount > 0)
                                    totCol.Item().Row(r => { r.RelativeItem().Text("الضريبة:"); r.RelativeItem().AlignLeft().Text(invoice.TaxAmount.ToString("N2")); });
                                
                                totCol.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Black).Row(r => 
                                { 
                                    r.RelativeItem().Text("الإجمالي النهائي:").Bold(); 
                                    r.RelativeItem().AlignLeft().Text($"{invoice.TotalAmount:N2}").Bold(); 
                                });

                                if (invoice.PaidAmount > 0)
                                {
                                    totCol.Item().Row(r => { r.RelativeItem().Text("المبلغ المدفوع:"); r.RelativeItem().AlignLeft().Text(invoice.PaidAmount.ToString("N2")); });
                                    var remaining = invoice.RemainingAmount;
                                    if (remaining > 0)
                                        totCol.Item().Row(r => { r.RelativeItem().Text("المتبقي:").Bold(); r.RelativeItem().AlignLeft().Text(remaining.ToString("N2")).Bold(); });
                                }
                            });
                        });
                    });

                    page.Footer().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("إعداد: __________________").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("اعتماد المشتريات: __________________").FontSize(9);
                        });
                        col.Item().PaddingTop(8).AlignCenter().Text(x =>
                        {
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    public async Task<byte[]> GeneratePurchaseReturnPdfAsync(PurchaseReturn pReturn)
    {
        EnsureFontLoaded();
        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.2f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(storeCol =>
                            {
                                storeCol.Item().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(16).Bold().FontColor(Colors.Black);
                                if (!string.IsNullOrEmpty(settings?.StoreSlogan))
                                    storeCol.Item().Text(settings.StoreSlogan).FontSize(9).FontColor(Colors.Grey.Medium);
                            });

                            row.RelativeItem().AlignLeft().Column(titleCol =>
                            {
                                titleCol.Item().Text("مرتجع مشتريات").FontSize(18).Bold().FontColor(Colors.Black);
                                titleCol.Item().PaddingTop(2).Text($"رقم الإرجاع: {pReturn.ReturnNumber}").Bold().FontSize(10);
                                if (pReturn.Invoice != null)
                                    titleCol.Item().Text($"فاتورة الشراء المرتبطة: {pReturn.Invoice.InvoiceNumber}").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(suppCol =>
                            {
                                suppCol.Item().Text("بيانات المورد").Bold().FontSize(10.5f).Underline();
                                suppCol.Item().PaddingTop(4).Text($"الاسم: {pReturn.Supplier?.Name}").Bold();
                                if (!string.IsNullOrEmpty(pReturn.Supplier?.Phone))
                                    suppCol.Item().Text($"الهاتف: {pReturn.Supplier.Phone}");
                                if (!string.IsNullOrEmpty(pReturn.Supplier?.Address))
                                    suppCol.Item().Text($"العنوان: {pReturn.Supplier.Address}");
                            });

                            row.RelativeItem().AlignLeft().Column(metaCol =>
                            {
                                metaCol.Item().Text("تفاصيل المستند").Bold().FontSize(10.5f).Underline();
                                metaCol.Item().PaddingTop(4).Text($"التاريخ: {pReturn.ReturnDate:yyyy/MM/dd}");
                                metaCol.Item().Text($"طريقة الدفع للمرتجع: {(pReturn.PaymentTerms == PaymentTerms.Cash ? "نقدي" : "آجل / رصيد مورد")}");
                                if (pReturn.CostCenter.HasValue)
                                    metaCol.Item().Text($"مركز التكلفة: {pReturn.CostCenter}");
                            });
                        });

                        col.Item().PaddingTop(15);

                        // Items Table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30); // #
                                columns.RelativeColumn(3);  // Product Name
                                columns.RelativeColumn(1.5f); // SKU
                                columns.RelativeColumn(1.2f); // Quantity
                                columns.RelativeColumn(1.2f); // Unit Cost
                                columns.RelativeColumn(1.5f); // Total
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("الصنف").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكود").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكمية المرتجعة").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("سعر التكلفة").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignLeft().Text("الإجمالي").Bold();
                            });

                            int index = 1;
                            foreach (var item in pReturn.Items)
                            {
                                var cellStyle = TextStyle.Default.FontSize(8.5f);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(index++.ToString()).Style(cellStyle);
                                
                                var productName = item.Product?.NameAr ?? "مرتجع صنف";
                                if (item.ProductVariant != null)
                                {
                                    var variantDetails = new List<string>();
                                    if (!string.IsNullOrEmpty(item.ProductVariant.Size)) variantDetails.Add(item.ProductVariant.Size);
                                    if (!string.IsNullOrEmpty(item.ProductVariant.ColorAr)) variantDetails.Add(item.ProductVariant.ColorAr);
                                    else if (!string.IsNullOrEmpty(item.ProductVariant.Color)) variantDetails.Add(item.ProductVariant.Color);
                                    
                                    if (variantDetails.Any())
                                        productName += $" ({string.Join(" - ", variantDetails)})";
                                }
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(productName).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.Product?.SKU ?? "—").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text($"{item.Quantity} {item.Unit}").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.UnitCost.ToString("N2")).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignLeft().Text(item.TotalCost.ToString("N2")).Style(cellStyle);
                            }
                        });

                        col.Item().PaddingTop(15);

                        // Totals & Tafqeet
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(2).Column(tafCol =>
                            {
                                var tafqeet = CurrencyHelper.ToArabicWords(pReturn.TotalAmount);
                                tafCol.Item().Padding(4).Background(Colors.Grey.Lighten4).Text(tafqeet).Bold().FontSize(9);
                                if (!string.IsNullOrEmpty(pReturn.Notes))
                                {
                                    tafCol.Item().PaddingTop(8).Text($"ملاحظات: {pReturn.Notes}").FontSize(8.5f);
                                }
                            });

                            row.RelativeItem(1.5f).Column(totCol =>
                            {
                                totCol.Item().Row(r => { r.RelativeItem().Text("الإجمالي الفرعي:"); r.RelativeItem().AlignLeft().Text(pReturn.SubTotal.ToString("N2")); });
                                if (pReturn.DiscountAmount > 0)
                                    totCol.Item().Row(r => { r.RelativeItem().Text("الخصم:"); r.RelativeItem().AlignLeft().Text($"-{pReturn.DiscountAmount:N2}"); });
                                if (pReturn.TaxAmount > 0)
                                    totCol.Item().Row(r => { r.RelativeItem().Text("الضريبة:"); r.RelativeItem().AlignLeft().Text(pReturn.TaxAmount.ToString("N2")); });
                                
                                totCol.Item().PaddingTop(4).BorderTop(1).BorderColor(Colors.Black).Row(r => 
                                { 
                                    r.RelativeItem().Text("إجمالي المرتجع:").Bold(); 
                                    r.RelativeItem().AlignLeft().Text($"{pReturn.TotalAmount:N2}").Bold(); 
                                });
                            });
                        });
                    });

                    page.Footer().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("إعداد: __________________").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("اعتماد المخزن: __________________").FontSize(9);
                        });
                        col.Item().PaddingTop(8).AlignCenter().Text(x =>
                        {
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateVoucherPdfAsync(ReceiptVoucher? receiptVoucher, PaymentVoucher? paymentVoucher)
    {
        EnsureFontLoaded();
        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            bool isReceipt = receiptVoucher != null;
            string voucherTitle = isReceipt ? "سند قبض" : "سند صرف / دفع";
            string voucherNumber = isReceipt ? receiptVoucher!.VoucherNumber : paymentVoucher!.VoucherNumber;
            DateTime voucherDate = isReceipt ? receiptVoucher!.VoucherDate : paymentVoucher!.VoucherDate;
            decimal amount = isReceipt ? receiptVoucher!.Amount : paymentVoucher!.Amount;
            string paymentMethod = isReceipt ? receiptVoucher!.PaymentMethod.ToString() : paymentVoucher!.PaymentMethod.ToString();
            string? reference = isReceipt ? receiptVoucher!.Reference : paymentVoucher!.Reference;
            string? description = isReceipt ? receiptVoucher!.Description : paymentVoucher!.Description;
            string cashAccountName = isReceipt ? (receiptVoucher!.CashAccount?.NameAr ?? "حساب الخزينة/البنك") : (paymentVoucher!.CashAccount?.NameAr ?? "حساب الخزينة/البنك");
            
            string paidToOrReceivedFromTitle = isReceipt ? "مستلم من:" : "مدفوع لـ:";
            string partyName = "عام";
            if (isReceipt)
            {
                if (receiptVoucher!.Customer != null)
                    partyName = $"العميل: {receiptVoucher.Customer.FullName}";
                else if (receiptVoucher.Employee != null)
                    partyName = $"الموظف: {receiptVoucher.Employee.Name}";
                else if (receiptVoucher.FromAccount != null)
                    partyName = $"الحساب الدائن: {receiptVoucher.FromAccount.NameAr}";
            }
            else
            {
                if (paymentVoucher!.Supplier != null)
                    partyName = $"المورد: {paymentVoucher.Supplier.Name}";
                else if (paymentVoucher.Employee != null)
                    partyName = $"الموظف: {paymentVoucher.Employee.Name}";
                else if (paymentVoucher.ToAccount != null)
                    partyName = $"الحساب المدين: {paymentVoucher.ToAccount.NameAr}";
            }

            OrderSource? costCenter = isReceipt ? receiptVoucher!.CostCenter : paymentVoucher!.CostCenter;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(storeCol =>
                            {
                                storeCol.Item().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(16).Bold().FontColor(Colors.Black);
                                if (!string.IsNullOrEmpty(settings?.StoreSlogan))
                                    storeCol.Item().Text(settings.StoreSlogan).FontSize(9).FontColor(Colors.Grey.Medium);
                            });

                            row.RelativeItem().AlignLeft().Column(titleCol =>
                            {
                                titleCol.Item().Text(voucherTitle).FontSize(18).Bold().FontColor(Colors.Black);
                                titleCol.Item().PaddingTop(2).Text($"رقم السند: {voucherNumber}").Bold().FontSize(10);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(20).Column(col =>
                    {
                        // Main amount card
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(infoCol =>
                            {
                                infoCol.Item().Text($"تاريخ السند: {voucherDate:yyyy/MM/dd}").Bold();
                                infoCol.Item().Text($"طريقة الدفع: {paymentMethod}");
                                if (!string.IsNullOrEmpty(reference))
                                    infoCol.Item().Text($"رقم المرجع: {reference}");
                                if (costCenter.HasValue)
                                    infoCol.Item().Text($"مركز التكلفة: {costCenter}");
                            });

                            row.ConstantItem(180).Border(1.5f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).Padding(10).Column(amtCol =>
                            {
                                amtCol.Item().AlignCenter().Text("المبلغ الإجمالي").FontSize(9).Bold();
                                amtCol.Item().AlignCenter().Text($"{amount:N2} {settings?.CurrencySymbol ?? "ج.م"}").FontSize(16).Bold();
                            });
                        });

                        col.Item().PaddingTop(20).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(12).Column(box =>
                        {
                            box.Item().Row(r =>
                            {
                                r.ConstantItem(80).Text(paidToOrReceivedFromTitle).Bold();
                                r.RelativeItem().Text(partyName).Bold();
                            });

                            box.Item().PaddingTop(8).Row(r =>
                            {
                                r.ConstantItem(80).Text("مبلغ وقدره:").Bold();
                                r.RelativeItem().Text(CurrencyHelper.ToArabicWords(amount)).Bold();
                            });

                            box.Item().PaddingTop(8).Row(r =>
                            {
                                r.ConstantItem(80).Text("حساب النقدية:").Bold();
                                r.RelativeItem().Text(cashAccountName);
                            });

                            if (!string.IsNullOrEmpty(description))
                            {
                                box.Item().PaddingTop(12).BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(8).Row(r =>
                                {
                                    r.ConstantItem(80).Text("وذلك قيمة:").Bold();
                                    r.RelativeItem().Text(description).LineHeight(1.3f);
                                });
                            }
                        });
                    });

                    page.Footer().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("المُستلم: __________________").FontSize(9.5f);
                            row.RelativeItem().AlignCenter().Text("المُحاسب: __________________").FontSize(9.5f);
                            row.RelativeItem().AlignCenter().Text("المدير المالي: __________________").FontSize(9.5f);
                        });
                        col.Item().PaddingTop(10).AlignCenter().Text(x =>
                        {
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateOpeningBalancePdfAsync(InventoryOpeningBalance openingBalance)
    {
        EnsureFontLoaded();
        
        // Eager load items and products if not already loaded to prevent null reference errors
        if (openingBalance.Items == null || openingBalance.Items.Any(i => i.Product == null))
        {
            await _db.InventoryOpeningBalanceItems
                .Where(i => i.InventoryOpeningBalanceId == openingBalance.Id)
                .Include(i => i.Product)
                .Include(i => i.ProductVariant)
                .LoadAsync();
        }

        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(storeCol =>
                            {
                                storeCol.Item().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(16).Bold().FontColor(Colors.Black);
                                if (!string.IsNullOrEmpty(settings?.StoreSlogan))
                                    storeCol.Item().Text(settings.StoreSlogan).FontSize(9).FontColor(Colors.Grey.Medium);
                            });

                            row.RelativeItem().AlignLeft().Column(titleCol =>
                            {
                                titleCol.Item().Text("الأرصدة الافتتاحية للمخزون").FontSize(16).Bold().FontColor(Colors.Black);
                                titleCol.Item().PaddingTop(2).Text($"الرقم المرجعي: {openingBalance.Reference}").Bold().FontSize(10);
                                titleCol.Item().Text($"تاريخ القيد: {openingBalance.Date:yyyy/MM/dd}").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        if (!string.IsNullOrEmpty(openingBalance.Notes))
                        {
                            col.Item().PaddingBottom(10).Text($"ملاحظات: {openingBalance.Notes}").FontSize(9).Italic();
                        }
                        
                        if (openingBalance.CostCenter.HasValue)
                        {
                            col.Item().PaddingBottom(10).Text($"مركز التكلفة: {openingBalance.CostCenter}").FontSize(9).Bold();
                        }

                        // Items Table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30); // #
                                columns.RelativeColumn(3.5f);  // Product Name
                                columns.RelativeColumn(1.5f); // SKU
                                columns.RelativeColumn(1.5f); // Size / Color
                                columns.RelativeColumn(1);   // Quantity
                                columns.RelativeColumn(1.2f); // Cost Price
                                columns.RelativeColumn(1.5f); // Total Cost
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("الصنف").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكود (SKU)").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("المقاس / اللون").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("الكمية").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("سعر التكلفة").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignLeft().Text("إجمالي التكلفة").Bold();
                            });

                            int index = 1;
                            foreach (var item in openingBalance.Items!)
                            {
                                var cellStyle = TextStyle.Default.FontSize(8.5f);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(index++.ToString()).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(item.Product?.NameAr ?? "منتج").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.Product?.SKU ?? "—").Style(cellStyle);
                                
                                var sizeColorText = "—";
                                if (item.ProductVariant != null)
                                {
                                    var details = new List<string>();
                                    if (!string.IsNullOrEmpty(item.ProductVariant.Size)) details.Add(item.ProductVariant.Size);
                                    if (!string.IsNullOrEmpty(item.ProductVariant.ColorAr)) details.Add(item.ProductVariant.ColorAr);
                                    else if (!string.IsNullOrEmpty(item.ProductVariant.Color)) details.Add(item.ProductVariant.Color);
                                    
                                    if (details.Any())
                                        sizeColorText = string.Join(" / ", details);
                                }
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(sizeColorText).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.Quantity.ToString()).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(item.CostPrice.ToString("N2")).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignLeft().Text(item.TotalCost.ToString("N2")).Style(cellStyle);
                            }
                        });

                        col.Item().PaddingTop(15);

                        // Grand Total & Tafqeet
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(2).Column(tafCol =>
                            {
                                var tafqeet = CurrencyHelper.ToArabicWords(openingBalance.TotalValue);
                                tafCol.Item().Padding(4).Background(Colors.Grey.Lighten4).Text(tafqeet).Bold().FontSize(9);
                            });

                            row.RelativeItem(1.5f).Column(totCol =>
                            {
                                totCol.Item().Border(1).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4).Padding(6).Row(r => 
                                { 
                                    r.RelativeItem().Text("إجمالي القيمة:").Bold(); 
                                    r.RelativeItem().AlignLeft().Text($"{openingBalance.TotalValue:N2}").Bold(); 
                                });
                            });
                        });
                    });

                    page.Footer().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("أمين المستودع: __________________").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("المدير المالي: __________________").FontSize(9);
                        });
                        col.Item().PaddingTop(8).AlignCenter().Text(x =>
                        {
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    public async Task<byte[]> GenerateJournalEntryPdfAsync(JournalEntry entry)
    {
        EnsureFontLoaded();
        var settings = await _db.StoreInfo.FirstOrDefaultAsync();

        return await Task.Run(() =>
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(_activeFont));
                    page.ContentFromRightToLeft();

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(storeCol =>
                            {
                                storeCol.Item().Text(settings?.StoreBrandName ?? "SPORTIVE").FontSize(16).Bold().FontColor(Colors.Black);
                                if (!string.IsNullOrEmpty(settings?.StoreSlogan))
                                    storeCol.Item().Text(settings.StoreSlogan).FontSize(9).FontColor(Colors.Grey.Medium);
                            });

                            row.RelativeItem().AlignLeft().Column(titleCol =>
                            {
                                titleCol.Item().Text("قيد يومية").FontSize(18).Bold().FontColor(Colors.Black);
                                titleCol.Item().PaddingTop(2).Text($"رقم القيد: {entry.EntryNumber}").Bold().FontSize(10);
                                titleCol.Item().Text($"تاريخ القيد: {entry.EntryDate:yyyy/MM/dd}").FontSize(9);
                            });
                        });

                        col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingVertical(15).Column(col =>
                    {
                        // Metadata Block
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(infoCol =>
                            {
                                infoCol.Item().Text($"نوع القيد: {entry.Type}");
                                infoCol.Item().Text($"حالة القيد: {entry.Status}");
                                if (!string.IsNullOrEmpty(entry.Reference))
                                    infoCol.Item().Text($"المرجع: {entry.Reference}");
                                if (entry.CostCenter.HasValue)
                                    infoCol.Item().Text($"مركز التكلفة: {entry.CostCenter}");
                            });

                            row.RelativeItem().AlignLeft().Column(descCol =>
                            {
                                if (!string.IsNullOrEmpty(entry.Description))
                                    descCol.Item().Text($"البيان العام: {entry.Description}").LineHeight(1.2f);
                            });
                        });

                        col.Item().PaddingTop(15);

                        // Double Entry Table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30); // #
                                columns.RelativeColumn(1.2f); // Account Code
                                columns.RelativeColumn(3);    // Account Name
                                columns.RelativeColumn(1.2f); // Debit
                                columns.RelativeColumn(1.2f); // Credit
                                columns.RelativeColumn(2.5f); // Line Description / Entity
                            });

                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("#").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("رقم الحساب").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("اسم الحساب").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("مدين").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignCenter().Text("دائن").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("البيان / الجهة").Bold();
                            });

                            int index = 1;
                            foreach (var line in entry.Lines)
                            {
                                var cellStyle = TextStyle.Default.FontSize(8.5f);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(index++.ToString()).Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(line.Account?.Code ?? "—").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(line.Account?.NameAr ?? "حساب").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(line.Debit > 0 ? line.Debit.ToString("N2") : "—").Style(cellStyle);
                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text(line.Credit > 0 ? line.Credit.ToString("N2") : "—").Style(cellStyle);

                                // Line Description + Entity (Supplier/Customer/Employee)
                                var details = new List<string>();
                                if (!string.IsNullOrEmpty(line.Description)) details.Add(line.Description);
                                if (line.Supplier != null) details.Add($"المورد: {line.Supplier.Name}");
                                else if (line.Customer != null) details.Add($"العميل: {line.Customer.FullName}");
                                else if (line.Employee != null) details.Add($"الموظف: {line.Employee.Name}");

                                table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text(string.Join(" | ", details)).Style(cellStyle);
                            }
                        });

                        col.Item().PaddingTop(15);

                        // Totals Summary and Balance status
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(balCol =>
                            {
                                if (entry.IsBalanced)
                                    balCol.Item().Padding(4).Background(Colors.Green.Lighten5).Text("حالة القيد: متزن").Bold().FontSize(9).FontColor(Colors.Green.Darken3);
                                else
                                    balCol.Item().Padding(4).Background(Colors.Red.Lighten5).Text("حالة القيد: غير متزن").Bold().FontSize(9).FontColor(Colors.Red.Darken3);
                                
                                var totalWords = CurrencyHelper.ToArabicWords(entry.TotalDebit);
                                balCol.Item().PaddingTop(8).Text(totalWords).FontSize(8.5f).Bold();
                            });

                            row.RelativeItem(1.5f).Column(totCol =>
                            {
                                totCol.Item().Row(r => { r.RelativeItem().Text("إجمالي المدين:"); r.RelativeItem().AlignLeft().Text(entry.TotalDebit.ToString("N2")).Bold(); });
                                totCol.Item().Row(r => { r.RelativeItem().Text("إجمالي الدائن:"); r.RelativeItem().AlignLeft().Text(entry.TotalCredit.ToString("N2")).Bold(); });
                            });
                        });
                    });

                    page.Footer().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().AlignCenter().Text("مدخل البيانات: __________________").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("المراجع: __________________").FontSize(9);
                            row.RelativeItem().AlignCenter().Text("المدير المالي: __________________").FontSize(9);
                        });
                        col.Item().PaddingTop(8).AlignCenter().Text(x =>
                        {
                            x.CurrentPageNumber();
                            x.Span(" / ");
                            x.TotalPages();
                        });
                    });
                });
            }).GeneratePdf();
        });
    }

    private string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // Sanitize: Limit length
        if (input.Length > 500) input = input.Substring(0, 500);
        // Replace problematic control chars, leaving standard text intact
        return new string(input.Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray());
    }

    private static string CleanKashida(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return System.Text.RegularExpressions.Regex.Replace(input, @"ـ{2,}", "ـ");
    }
}
