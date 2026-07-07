using ClosedXML.Excel;
using System;
using System.Linq;

namespace Sportive.API.Utils
{
    public static class ExcelThemeHelper
    {
        public static void ApplyElegantTheme(XLWorkbook wb, string storeName = "Sportive")
        {
            if (wb == null) return;

            // Define premium colors
            var headerBgColor = XLColor.FromHtml("#0f172a"); // Slate 900
            var headerFontColor = XLColor.White;
            var subHeaderBgColor = XLColor.FromHtml("#334155"); // Slate 700
            var alternateRowBg = XLColor.FromHtml("#f8fafc"); // Slate 50
            var borderColor = XLColor.FromHtml("#e2e8f0"); // Slate 200

            foreach (var ws in wb.Worksheets)
            {
                // 1. Force RTL for Arabic support
                ws.RightToLeft = true;
                ws.Style.Font.FontName = "Segoe UI"; // Modern font that looks good in Arabic

                var usedRange = ws.RangeUsed();
                if (usedRange == null) continue;

                int lastCol = usedRange.ColumnCount();
                if (lastCol < 4) lastCol = 4; // Ensure minimum width for the header

                // Extract potential existing title from A1, A2
                string reportTitle = ws.Cell(1, 1).GetString();
                string reportSubtitle = ws.Cell(2, 1).GetString();

                // 2. Insert rows for premium header
                ws.Row(1).InsertRowsAbove(4);

                // 3. Draw Premium Header
                var headerRange = ws.Range(1, 1, 3, lastCol);
                headerRange.Merge();
                headerRange.Style.Fill.BackgroundColor = headerBgColor;
                headerRange.Style.Font.FontColor = headerFontColor;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                
                // Add title
                ws.Cell(1, 1).Value = string.IsNullOrWhiteSpace(reportTitle) ? ws.Name : reportTitle;
                ws.Cell(1, 1).Style.Font.FontSize = 20;
                ws.Cell(1, 1).Style.Font.Bold = true;
                
                // Draw Sub-header bar
                var subHeaderRange = ws.Range(4, 1, 4, lastCol);
                subHeaderRange.Merge();
                subHeaderRange.Style.Fill.BackgroundColor = subHeaderBgColor;
                subHeaderRange.Style.Font.FontColor = headerFontColor;
                subHeaderRange.Style.Font.FontSize = 10;
                subHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                subHeaderRange.Value = $"تاريخ الاستخراج: {DateTime.Now:yyyy-MM-dd HH:mm} | بواسطة نظام {storeName}";

                // 4. Format existing data (which now starts at row 5)
                usedRange = ws.RangeUsed(); // Recalculate used range
                if (usedRange != null)
                {
                    int rowCount = usedRange.RowCount();
                    int colCount = usedRange.ColumnCount();
                    
                    bool foundTableHeader = false;

                    for (int r = 5; r <= rowCount; r++)
                    {
                        var row = ws.Row(r);
                        
                        // Heuristic: If the row was historically colored or has bold text in multiple cells, it's probably the table header
                        bool isHeader = row.Cell(1).Style.Fill.BackgroundColor.HasValue && 
                                        row.Cell(1).Style.Fill.BackgroundColor.Color.R == 26 && 
                                        row.Cell(1).Style.Fill.BackgroundColor.Color.G == 35 && 
                                        row.Cell(1).Style.Fill.BackgroundColor.Color.B == 126 || 
                                        (row.Cell(1).Style.Font.Bold && row.Cell(2).Style.Font.Bold);

                        if (isHeader && !foundTableHeader)
                        {
                            foundTableHeader = true;
                            // Beautify the table header
                            var rowRange = ws.Range(r, 1, r, colCount);
                            rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b"); // Slate 800
                            rowRange.Style.Font.FontColor = XLColor.White;
                            rowRange.Style.Font.Bold = true;
                            rowRange.Style.Font.FontSize = 11;
                            rowRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            
                            // Remove any old filter and add new
                            ws.AutoFilter.Clear();
                            rowRange.SetAutoFilter();
                        }
                        else if (foundTableHeader)
                        {
                            // Alternating row colors for data rows
                            var rowRange = ws.Range(r, 1, r, colCount);
                            
                            // Clear old background if any (except total rows which might be bold)
                            if (row.Cell(1).Style.Font.Bold && row.Cell(1).GetString().Contains("إجمالي"))
                            {
                                rowRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9"); // Slate 100
                                rowRange.Style.Font.FontColor = XLColor.FromHtml("#0f172a");
                                rowRange.Style.Font.Bold = true;
                            }
                            else
                            {
                                if (r % 2 == 0)
                                    rowRange.Style.Fill.BackgroundColor = alternateRowBg;
                                else
                                    rowRange.Style.Fill.BackgroundColor = XLColor.White;
                                
                                rowRange.Style.Font.FontColor = XLColor.FromHtml("#334155");
                                rowRange.Style.Font.FontSize = 10;
                            }
                        }

                        // Apply soft borders to all used cells from row 5 onwards
                        for (int c = 1; c <= colCount; c++)
                        {
                            var cell = ws.Cell(r, c);
                            if (!cell.IsEmpty())
                            {
                                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                                cell.Style.Border.OutsideBorderColor = borderColor;
                            }
                        }
                    }
                }

                // 5. Cleanup the shifted title/subtitle if they were extracted from A5/A6
                if (!string.IsNullOrWhiteSpace(reportTitle)) ws.Cell(5, 1).Value = "";
                if (!string.IsNullOrWhiteSpace(reportSubtitle)) ws.Cell(6, 1).Value = "";

                // 6. Auto-fit columns with a reasonable maximum width
                ws.Columns().AdjustToContents();
                foreach (var col in ws.Columns())
                {
                    if (col.Width > 50) col.Width = 50; // Cap width for extremely long strings
                }
            }
        }
    }
}
