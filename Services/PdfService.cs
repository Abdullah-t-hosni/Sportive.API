using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Previewer;
using Sportive.API.DTOs;
using Sportive.API.Models;
using ArabicReshaper;
using System.Text.RegularExpressions;

namespace Sportive.API.Services;

using Sportive.API.Interfaces;

public class PdfService : IPdfService
{
    static PdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                
                // Use a common Arabic font on Windows
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Segoe UI"));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("SPORTIVE").FontSize(20).Bold().FontColor(Colors.Black);
                        col.Item().Text("Official Sales Invoice").FontSize(10).FontColor(Colors.Grey.Medium);
                    });

                    row.RelativeItem().AlignRight().Column(col =>
                    {
                        col.Item().Text(Reshape($"فاتورة رقم: {order.Id}")).Bold();
                        col.Item().Text(Reshape($"التاريخ: {order.CreatedAt:yyyy/MM/dd}")).FontSize(9);
                    });
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    // Customer Info
                    col.Item().BorderBottom(1).PaddingBottom(5).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(Reshape($"العميل: {order.Customer.FullName}")).Bold();
                            c.Item().Text(Reshape($"الهاتف: {order.Customer.Phone}"));
                        });
                    });

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
                            table.Cell().Element(ContentStyle).Text(Reshape(item.ProductNameAr));
                            table.Cell().Element(ContentStyle).AlignCenter().Text(item.Quantity.ToString());
                            table.Cell().Element(ContentStyle).AlignRight().Text(item.UnitPrice.ToString("N0"));
                            table.Cell().Element(ContentStyle).AlignRight().Text(item.TotalPrice.ToString("N0"));

                            static IContainer ContentStyle(IContainer container)
                            {
                                return container.PaddingVertical(5).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);
                            }
                        }
                    });

                    // Totals
                    col.Item().AlignRight().PaddingTop(10).Column(c =>
                    {
                        c.Item().Text(Reshape($"الإجمالي: {order.SubTotal:N0} L.E"));
                        if (order.DiscountAmount > 0)
                            c.Item().Text(Reshape($"الخصم: -{order.DiscountAmount:N0} L.E")).FontColor(Colors.Red.Medium);
                        
                        c.Item().PaddingTop(5).Text(Reshape($"الصافي: {order.TotalAmount:N0} L.E")).FontSize(14).Bold();
                        c.Item().Text(Reshape(order.TotalAmountInWords ?? "")).FontSize(9).Italic();
                    });

                    // QR Code or Footer
                    col.Item().PaddingTop(20).AlignCenter().Column(c => {
                       c.Item().Text(Reshape("شكراً لتعاملكم مع سبورتيف")).FontSize(12).Bold();
                       c.Item().Text("www.sportive-equipment.com").FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                });
            });
        });

        return Task.FromResult(document.GeneratePdf());
    }

    private string Reshape(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        // Check if contains Arabic characters
        bool hasArabic = Regex.IsMatch(input, @"\p{IsArabic}");
        if (!hasArabic) return input;

        // Simple Arabic Reshaper + BiDi reverse (using local Utils/ArabicReshaper.cs)
        var reshaped = ArabicAdapter.Reshape(input);
        
        // Split and reverse for correct RTL display in most PDF viewers
        var lines = reshaped.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            char[] charArray = lines[i].ToCharArray();
            Array.Reverse(charArray);
            lines[i] = new string(charArray);
        }
        
        return string.Join('\n', lines);
    }
}
