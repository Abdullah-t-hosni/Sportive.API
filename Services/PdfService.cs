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

namespace Sportive.API.Services;

public class PdfService : IPdfService
{
    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        
        // 🌍 UNIVERSAL FIX (Linux/Railway): Download Arabic font if not found
        try {
            string fontName = "Cairo";
            string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cairo.ttf");
            
            if (!File.Exists(fontPath))
            {
                using var client = new System.Net.Http.HttpClient();
                // Download Cairo-Regular from a reliable source (GitHub/Google)
                var bytes = client.GetByteArrayAsync("https://github.com/Gue3bara/Cairo/raw/master/Cairo-Regular.ttf").Result;
                File.WriteAllBytes(fontPath, bytes);
            }

            if (File.Exists(fontPath))
            {
                using var fontStream = File.OpenRead(fontPath);
                QuestPDF.Drawing.FontManager.RegisterFont(fontStream);
                QuestPDF.Settings.DefaultFont = fontName;
            }
        } catch {
            // Fallback for Windows local development
            try {
                if (File.Exists(@"C:\Windows\Fonts\tahoma.ttf"))
                {
                    using var s = File.OpenRead(@"C:\Windows\Fonts\tahoma.ttf");
                    QuestPDF.Drawing.FontManager.RegisterFont(s);
                    QuestPDF.Settings.DefaultFont = "Tahoma";
                }
            } catch {}
        }
    }

    public Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Logic: 80mm is standard for Thermal Receipt Printers
                page.Size(80, 297, Unit.Millimetre);
                page.Margin(5, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
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
                    // Customer Info (Compact)
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

                    // Items Table
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Product
                            columns.RelativeColumn(1); // Qty
                            columns.RelativeColumn(1); // Price
                            columns.RelativeColumn(1); // Total
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
                            table.Cell().Element(ContentStyle).AlignRight().Text(item.TotalPrice.ToString("N0"));

                            static IContainer ContentStyle(IContainer container)
                            {
                                return container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                            }
                        }
                    });

                    // Totals & Notes
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(c => {
                            if (!string.IsNullOrEmpty(order.CustomerNotes))
                                c.Item().Text(Reshape($"ملاحظات العميل: {order.CustomerNotes}")).FontSize(8).Italic();
                            if (!string.IsNullOrEmpty(order.AdminNotes))
                                c.Item().Text(Reshape($"ملاحظات إدارية: {order.AdminNotes}")).FontSize(8).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text(Reshape($"الإجمالي: {order.SubTotal:N0} L.E"));
                            if (order.TemporalDiscount > 0)
                                c.Item().Text(Reshape($"خصم العروض: -{order.TemporalDiscount:N0} L.E")).FontColor(Colors.Indigo.Medium);
                            if (order.DiscountAmount > 0)
                                c.Item().Text(Reshape($"خصم إضافي: -{order.DiscountAmount:N0} L.E")).FontColor(Colors.Red.Medium);
                            
                            c.Item().PaddingTop(5).Text(Reshape($"الصافي: {order.TotalAmount:N0} L.E")).FontSize(13).Bold();
                            
                            if (order.PreviousBalance != 0)
                                c.Item().Text(Reshape($"رصيد سابق: {order.PreviousBalance:N0} L.E")).FontSize(9).FontColor(Colors.Grey.Medium);
                            
                            c.Item().Text(Reshape($"المدفوع: {order.PaidAmount:N0} L.E")).FontSize(10).FontColor(Colors.Green.Medium);
                            
                            var remaining = (order.TotalAmount + order.PreviousBalance) - order.PaidAmount;
                            if (remaining > 0.01m)
                                c.Item().Text(Reshape($"المتبقي ذمة: {remaining:N0} L.E")).FontSize(10).Bold().FontColor(Colors.Red.Medium);
                        });
                    });

                    // Payments Detail
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

                    // Words
                    if (!string.IsNullOrEmpty(order.TotalAmountInWords))
                        col.Item().PaddingTop(5).AlignRight().Text(Reshape(order.TotalAmountInWords)).FontSize(8).Italic();

                    // Final Footnote
                    col.Item().PaddingTop(10).AlignCenter().Column(c => {
                       c.Item().Text(Reshape("شكراً لزيارتكم سبورتيف")).FontSize(10).Bold();
                       c.Item().Text("www.sportive-equipment.com").FontSize(7).FontColor(Colors.Grey.Medium);
                    });
                });

                page.Footer().PaddingBottom(5).AlignCenter().Text(x => {
                    x.Span("POS Terminal • ").FontSize(6);
                    x.CurrentPageNumber();
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> GenerateJournalEntryPdfAsync(JournalEntry entry)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
                page.ContentFromRightToLeft();

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("SPORTIVE").FontSize(20).Bold();
                        col.Item().Text(Reshape("سند قيد محاسبي")).FontSize(12).SemiBold();
                    });
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(Reshape($"رقم القيد: {entry.EntryNumber}")).Bold();
                        col.Item().Text(Reshape($"التاريخ: {entry.EntryDate:yyyy/MM/dd}")).FontSize(9);
                        col.Item().Text(Reshape($"الحالة: {entry.Status}")).FontSize(9).FontColor(entry.Status == JournalEntryStatus.Posted ? Colors.Green.Medium : Colors.Grey.Medium);
                    });
                });

                page.Content().PaddingVertical(15).Column(col =>
                {
                    col.Item().Border(0.5f).Padding(10).Column(c => {
                        c.Item().Row(r => {
                            r.RelativeItem().Text(Reshape($"البيان: {entry.Description}")).Bold();
                            r.RelativeItem().AlignRight().Text(Reshape($"المرجع: {entry.Reference ?? "SYSTEM"}"));
                        });
                        if (entry.CostCenter.HasValue)
                            c.Item().PaddingTop(5).Text(Reshape($"مركز التكلفة: {entry.CostCenter}")).FontSize(9);
                    });

                    col.Item().PaddingTop(15).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3); // Account
                            columns.RelativeColumn(1); // Debit
                            columns.RelativeColumn(1); // Credit
                            columns.RelativeColumn(2); // Line Note
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text(Reshape("الحساب"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("مدين"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("دائن"));
                            header.Cell().Element(CellStyle).Text(Reshape("البيان الفرعي"));

                            static IContainer CellStyle(IContainer container) => container.DefaultTextStyle(x => x.Bold()).PaddingVertical(5).BorderBottom(1);
                        });

                        foreach (var line in entry.Lines)
                        {
                            var accName = line.Account?.NameAr ?? "---";
                            var entity = line.Supplier?.Name ?? line.Customer?.FullName ?? line.Employee?.Name;
                            if (!string.IsNullOrEmpty(entity)) accName += $" ({entity})";

                            table.Cell().Element(CS).Text(Reshape(accName));
                            table.Cell().Element(CS).AlignRight().Text(line.Debit > 0 ? line.Debit.ToString("N2") : "-");
                            table.Cell().Element(CS).AlignRight().Text(line.Credit > 0 ? line.Credit.ToString("N2") : "-");
                            table.Cell().Element(CS).Text(Reshape(line.Description ?? ""));

                            static IContainer CS(IContainer container) => container.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                        }
                    });

                    col.Item().AlignRight().PaddingTop(15).Row(row => {
                        row.RelativeItem(2).AlignCenter().Column(c => {
                            c.Item().Text(Reshape("المحاسب")).Bold();
                            c.Item().PaddingTop(20).Text("------------------");
                        });
                        row.RelativeItem(2).AlignCenter().Column(c => {
                            c.Item().Text(Reshape("الاعتماد")).Bold();
                            c.Item().PaddingTop(20).Text("------------------");
                        });
                        row.RelativeItem(3).Column(c => {
                             c.Item().Border(1).Padding(8).Row(r => {
                                r.RelativeItem().Text(Reshape("الإجمالي:")).Bold();
                                r.RelativeItem().AlignRight().Text($"{entry.TotalDebit:N2} L.E").Bold();
                             });
                        });
                    });
                });

                page.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
            });
        });
        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> GeneratePurchaseInvoicePdfAsync(Sportive.API.Models.PurchaseInvoice invoice)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
                page.ContentFromRightToLeft();

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("SPORTIVE").FontSize(24).Bold().FontColor(Colors.Blue.Medium);
                        col.Item().Text(Reshape("فاتورة مشتريات")).FontSize(12).SemiBold();
                    });
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(Reshape($"رقم الفاتورة: #{invoice.InvoiceNumber}")).FontSize(14).Bold();
                        col.Item().Text(Reshape($"التاريخ: {invoice.InvoiceDate:yyyy/MM/dd}")).FontSize(10);
                        col.Item().Text(Reshape($"الحالة: {invoice.Status}")).FontSize(10).Bold().FontColor(invoice.Status == PurchaseInvoiceStatus.Paid ? Colors.Green.Medium : Colors.Blue.Medium);
                    });
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    // Supplier & Store Info
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Border(0.5f).Padding(10).Column(c =>
                        {
                            c.Item().Text(Reshape("بيانات المورد")).FontSize(9).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Text(Reshape(invoice.Supplier?.Name ?? "---")).FontSize(12).Bold();
                            if (!string.IsNullOrEmpty(invoice.Supplier?.Phone))
                                c.Item().Text(Reshape($"الهاتف: {invoice.Supplier.Phone}"));
                            if (!string.IsNullOrEmpty(invoice.Supplier?.TaxNumber))
                                c.Item().Text(Reshape($"السجل الضريبي: {invoice.Supplier.TaxNumber}"));
                        });
                        row.ConstantItem(20);
                        row.RelativeItem().Border(0.5f).Padding(10).Column(c =>
                        {
                            c.Item().Text(Reshape("مركز التكلفة")).FontSize(9).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Text(Reshape(invoice.CostCenter == OrderSource.POS ? "المحل (POS)" : "الموقع (Web)")).FontSize(12).Bold();
                            c.Item().Text(Reshape("الاستلام بواسطة: نظام المشتريات"));
                        });
                    });

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);   // #
                            columns.RelativeColumn(4);    // Item
                            columns.RelativeColumn(1);    // Qty
                            columns.RelativeColumn(1.5f); // Unit Cost
                            columns.RelativeColumn(1.5f); // Total
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("#");
                            header.Cell().Element(CellStyle).Text(Reshape("الصنف / الوصف"));
                            header.Cell().Element(CellStyle).AlignCenter().Text(Reshape("الكمية"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("السعر"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("الإجمالي"));

                            static IContainer CellStyle(IContainer container) => container.DefaultTextStyle(x => x.Bold()).PaddingVertical(5).BorderBottom(1);
                        });

                        int i = 1;
                        foreach (var item in invoice.Items)
                        {
                            table.Cell().Element(CS).Text($"{i++}");
                            table.Cell().Element(CS).Text(Reshape(item.Description));
                            table.Cell().Element(CS).AlignCenter().Text(item.Quantity.ToString("G29"));
                            table.Cell().Element(CS).AlignRight().Text(item.UnitCost.ToString("N2"));
                            table.Cell().Element(CS).AlignRight().Text(item.TotalCost.ToString("N2"));

                            static IContainer CS(IContainer container) => container.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                        }
                    });

                    // Totals
                    col.Item().PaddingTop(15).Row(row =>
                    {
                        row.RelativeItem(3).Column(c => {
                            if (!string.IsNullOrEmpty(invoice.Notes))
                                c.Item().Padding(10).Background(Colors.Grey.Lighten4).Text(Reshape($"ملاحظات: {invoice.Notes}")).FontSize(9).Italic();
                        });
                        row.RelativeItem(2).AlignRight().Column(c =>
                        {
                            c.Item().Text(Reshape($"الإجمالي قبل الضريبة: {invoice.SubTotal:N2} ج.م")).FontSize(10);
                            if (invoice.TaxAmount > 0)
                                c.Item().Text(Reshape($"ضريبة القيمة المضافة {invoice.TaxPercent:N0}%: {invoice.TaxAmount:N2} ج.م")).FontSize(10);
                            if (invoice.DiscountAmount > 0)
                                c.Item().Text(Reshape($"إجمالي الخصومات: -{invoice.DiscountAmount:N2} ج.م")).FontSize(10).FontColor(Colors.Red.Medium);
                            
                            c.Item().PaddingTop(10).BorderTop(1).PaddingTop(5).Text(Reshape($"إجمالي الفاتورة: {invoice.TotalAmount:N2} ج.م")).FontSize(16).Bold();
                            c.Item().Text(Reshape(CurrencyHelper.ToArabicWords(invoice.TotalAmount))).FontSize(8).Italic();
                            
                            c.Item().PaddingTop(5).Text(Reshape($"المبلغ المدفوع: {invoice.PaidAmount:N2} ج.م")).FontSize(11).FontColor(Colors.Green.Medium);
                            if (invoice.RemainingAmount > 0)
                                c.Item().Text(Reshape($"المتبقي للمورد: {invoice.RemainingAmount:N2} ج.م")).FontSize(11).FontColor(Colors.Red.Medium).Bold();
                        });
                    });
                });

                page.Footer().AlignCenter().Column(c => {
                    c.Item().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
                    c.Item().PaddingTop(5).Text(Reshape("SPORTIVE - نظام إدارة المشتريات والمخازن الذكي")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> GenerateVoucherPdfAsync(ReceiptVoucher? rv, PaymentVoucher? pv)
    {
        var isReceipt = rv != null;
        var amount = isReceipt ? rv!.Amount : pv!.Amount;
        var date   = isReceipt ? rv!.VoucherDate : pv!.VoucherDate;
        var num    = isReceipt ? rv!.VoucherNumber : pv!.VoucherNumber;
        var person = isReceipt ? (rv!.Customer?.FullName ?? rv.Employee?.Name ?? "عميل عام") : (pv!.Supplier?.Name ?? pv.Employee?.Name ?? "مورد عام");
        var notes  = isReceipt ? (rv!.Description ?? rv.Reference) : (pv!.Description ?? pv.Reference);
        var method = isReceipt ? rv!.PaymentMethod.ToString() : pv!.PaymentMethod.ToString();
        var account = isReceipt ? (rv!.CashAccount?.NameAr ?? "خزينة") : (pv!.CashAccount?.NameAr ?? "خزينة");

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5.Landscape());
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
                page.ContentFromRightToLeft();

                page.Header().Border(1).Padding(5).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("SPORTIVE").FontSize(16).Bold();
                        col.Item().Text(Reshape(isReceipt ? "سند قبض نقدية" : "سند صرف نقدية")).FontSize(12).SemiBold();
                    });
                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(Reshape($"رقم السند: {num}")).Bold();
                        col.Item().Text(Reshape($"التاريخ: {date:yyyy/MM/dd}"));
                    });
                });

                page.Content().PaddingVertical(15).Border(1).Padding(10).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text(Reshape(isReceipt ? "استلمنا من السيد:" : "صرفنا إلى السيد:")).Bold();
                        row.RelativeItem(3).BorderBottom(0.5f).Text(Reshape(person));
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(Reshape("مبلغ وقدره:")).Bold();
                        row.RelativeItem(3).BorderBottom(0.5f).Text(Reshape(CurrencyHelper.ToArabicWords(amount)));
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(Reshape("وذلك عن:")).Bold();
                        row.RelativeItem(3).BorderBottom(0.5f).Text(Reshape(notes ?? "---"));
                    });

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Text(Reshape("طريقة الدفع:")).Bold();
                        row.RelativeItem().Text(Reshape(method));
                        row.RelativeItem().Text(Reshape("الحساب:")).Bold();
                        row.RelativeItem().Text(Reshape(account));
                    });

                    col.Item().PaddingTop(20).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Text(Reshape("المحاسب")).Bold();
                        row.RelativeItem().AlignCenter().Text(Reshape("المستلم")).Bold();
                    });
                    
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().AlignCenter().Text("------------------");
                        row.RelativeItem().AlignCenter().Text("------------------");
                    });
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> GenerateOpeningBalancePdfAsync(InventoryOpeningBalance op)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
                page.ContentFromRightToLeft();

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(c => {
                        c.Item().Text("SPORTIVE").FontSize(16).Bold();
                        c.Item().Text(Reshape("سند رصيد افتتاحي للمخزون")).Bold();
                    });
                    row.RelativeItem().AlignRight().Column(c => {
                        c.Item().Text(Reshape($"الرقم: {op.Reference}"));
                        c.Item().Text(Reshape($"التاريخ: {op.Date:yyyy/MM/dd}"));
                    });
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(3); // Name
                            columns.RelativeColumn(1); // Size/Color
                            columns.RelativeColumn(1); // Qty
                            columns.RelativeColumn(1); // Cost
                            columns.RelativeColumn(1); // Total
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("#");
                            header.Cell().Element(CellStyle).Text(Reshape("الصنف"));
                            header.Cell().Element(CellStyle).Text(Reshape("المتغيرات"));
                            header.Cell().Element(CellStyle).AlignCenter().Text(Reshape("الكمية"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("التكلفة"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("الإجمالي"));
                            static IContainer CellStyle(IContainer container) => container.DefaultTextStyle(x => x.Bold()).PaddingVertical(5).BorderBottom(1);
                        });

                        int i = 1;
                        foreach (var item in op.Items)
                        {
                            table.Cell().Element(ContentStyle).Text(i++.ToString());
                            table.Cell().Element(ContentStyle).Text(Reshape(item.Product?.NameAr ?? "---"));
                            table.Cell().Element(ContentStyle).Text(Reshape($"{item.ProductVariant?.Size} {item.ProductVariant?.ColorAr}".Trim()));
                            table.Cell().Element(ContentStyle).AlignCenter().Text(item.Quantity.ToString());
                            table.Cell().Element(ContentStyle).AlignRight().Text(item.CostPrice.ToString("N2"));
                            table.Cell().Element(ContentStyle).AlignRight().Text((item.Quantity * item.CostPrice).ToString("N2"));
                            static IContainer ContentStyle(IContainer container) => container.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                        }
                    });

                    col.Item().AlignRight().PaddingTop(10).Column(c => {
                        c.Item().Text(Reshape($"إجمالي القيمة: {op.TotalValue:N2} ج.م")).FontSize(12).Bold();
                        c.Item().Text(Reshape(CurrencyHelper.ToArabicWords(op.TotalValue))).FontSize(9).Italic();
                    });
                });
            });
        });
        return Task.FromResult(document.GeneratePdf());
    }

    public Task<byte[]> GeneratePurchaseReturnPdfAsync(PurchaseReturn pReturn)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(QuestPDF.Settings.DefaultFont ?? "Arial"));
                page.ContentFromRightToLeft();

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col => {
                        col.Item().Text("SPORTIVE").FontSize(24).Bold().FontColor(Colors.Red.Medium);
                        col.Item().Text(Reshape("إشعار مرتجع مشتريات")).FontSize(12).SemiBold();
                    });
                    row.RelativeItem().AlignRight().Column(col => {
                        col.Item().Text(Reshape($"رقم الإشعار: #{pReturn.ReturnNumber}")).FontSize(14).Bold();
                        col.Item().Text(Reshape($"الفاتورة الأصلية: #{pReturn.Invoice?.InvoiceNumber}")).FontSize(10);
                        col.Item().Text(Reshape($"التاريخ: {pReturn.ReturnDate:yyyy/MM/dd}")).FontSize(10);
                    });
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    // Entity Info
                    col.Item().Border(0.5f).Padding(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(Reshape("المورد")).FontSize(9).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Text(Reshape(pReturn.Supplier?.Name ?? "---")).FontSize(12).Bold();
                        });
                        row.RelativeItem().AlignRight().Column(c =>
                        {
                            c.Item().Text(Reshape("البيان")).FontSize(9).SemiBold().FontColor(Colors.Grey.Medium);
                            c.Item().Text(Reshape("مرتجع بضائع موردة")).FontSize(10);
                        });
                    });

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns => {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.5f);
                        });

                        table.Header(header => {
                            header.Cell().Element(CellStyle).Text("#");
                            header.Cell().Element(CellStyle).Text(Reshape("الصنف / الوصف"));
                            header.Cell().Element(CellStyle).AlignCenter().Text(Reshape("الكمية"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("التكلفة"));
                            header.Cell().Element(CellStyle).AlignRight().Text(Reshape("الإجمالي"));
                            static IContainer CellStyle(IContainer container) => container.DefaultTextStyle(x => x.Bold()).PaddingVertical(5).BorderBottom(1);
                        });

                        int i = 1;
                        foreach (var item in pReturn.Items) {
                            table.Cell().Element(CS).Text($"{i++}");
                            table.Cell().Element(CS).Text(Reshape($"{item.Product?.NameAr} {item.ProductVariant?.Size} {item.ProductVariant?.ColorAr}".Trim()));
                            table.Cell().Element(CS).AlignCenter().Text(item.Quantity.ToString("G29"));
                            table.Cell().Element(CS).AlignRight().Text(item.UnitCost.ToString("N2"));
                            table.Cell().Element(CS).AlignRight().Text(item.TotalCost.ToString("N2"));
                            static IContainer CS(IContainer container) => container.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                        }
                    });

                    col.Item().PaddingTop(15).Row(row => {
                        row.RelativeItem(3).Column(c => {
                             if (!string.IsNullOrEmpty(pReturn.Notes))
                                c.Item().Padding(10).Background(Colors.Grey.Lighten4).Text(Reshape($"سبب الارتجاع: {pReturn.Notes}")).FontSize(9).Italic();
                        });
                        row.RelativeItem(2).AlignRight().Column(c => {
                            c.Item().Text(Reshape($"إجمالي القيمة المرتجعة: {pReturn.TotalAmount:N2} ج.م")).FontSize(16).Bold().FontColor(Colors.Red.Medium);
                            c.Item().Text(Reshape(CurrencyHelper.ToArabicWords(pReturn.TotalAmount))).FontSize(8).Italic();
                        });
                    });
                    
                    // Signatures
                    col.Item().PaddingTop(40).Row(row => {
                        row.RelativeItem().AlignCenter().Column(c => {
                            c.Item().Text(Reshape("أمين المخزن")).Bold();
                            c.Item().PaddingTop(20).Text("..................");
                        });
                        row.RelativeItem().AlignCenter().Column(c => {
                            c.Item().Text(Reshape("الاعتماد")).Bold();
                            c.Item().PaddingTop(20).Text("..................");
                        });
                    });
                });

                page.Footer().AlignCenter().Column(c => {
                    c.Item().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
                    c.Item().PaddingTop(5).Text(Reshape("SPORTIVE - إشعار خصم مرتجع مشتريات")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        });
        return Task.FromResult(document.GeneratePdf());
    }

    private string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        bool hasArabic = Regex.IsMatch(input, @"\p{IsArabic}");
        if (!hasArabic) return input;

        // 1. Handle glyph shaping for Arabic
        var reshaped = ArabicAdapter.Reshape(input);
        
        // 2. SMART BIDI: Split the string and only reverse segments that are Arabic
        // For QuestPDF without native shaping, we usually reverse the WHOLE line
        // but we can restore English words/Numbers to their original order.
        
        char[] charArray = reshaped.ToCharArray();
        Array.Reverse(charArray);
        string reversed = new string(charArray);

        // 3. FIX: Restore English words and numbers that were reversed
        // Regex to find segments that were originally English/Numbers and are now reversed
        return Regex.Replace(reversed, @"[a-zA-Z0-9\-\.\/]+", m => {
            char[] a = m.Value.ToCharArray();
            Array.Reverse(a);
            return new string(a);
        });
    }
}
