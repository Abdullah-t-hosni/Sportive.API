using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models
{
    public class DailyStat
    {
        public int TenantId { get; set; } = 1;
        public DateTime Date { get; set; }
        public OrderSource Source { get; set; } // Global for All, or specific source
        public decimal TotalSales { get; set; }
        public int OrdersCount { get; set; }
        public decimal TotalCollections { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal Profit { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
