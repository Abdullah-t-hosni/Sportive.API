namespace Sportive.API.DTOs.Reports;

public class InvoiceProfitabilityDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public decimal Margin { get; set; }
}

public class InvoiceProfitabilityReportDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalInvoices { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal AverageMargin { get; set; }
    public List<InvoiceProfitabilityDto> Invoices { get; set; } = new();
}
