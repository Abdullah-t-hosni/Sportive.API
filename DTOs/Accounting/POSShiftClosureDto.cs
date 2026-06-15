using System.ComponentModel.DataAnnotations;

namespace Sportive.API.DTOs
{
    public class CreatePOSShiftClosureDto
    {
        [Required]
        [MaxLength(50)]
        public string StationId { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string ClosureDate { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ClosedBy { get; set; }

        public decimal StartingBalance { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal ActualCash { get; set; }
        public decimal Variance { get; set; }
        public decimal GrossSales { get; set; }
        public decimal NetSales { get; set; }
        public decimal CashSales { get; set; }
        public decimal CardSales { get; set; }
        public decimal VodafoneCashSales { get; set; }
        public decimal InstapaySales { get; set; }
        public decimal WalletSales { get; set; }
        public decimal CreditSales { get; set; }
        public decimal Expenses { get; set; }
        public decimal SafeDrops { get; set; }
        public decimal Returns { get; set; }
        public decimal Discounts { get; set; }

        [MaxLength(100)]
        public string? JournalEntryReference { get; set; }

        public int? BranchId { get; set; }
    }

    public class UpdatePOSShiftClosureDto
    {
        public decimal StartingBalance { get; set; }
        public decimal ActualCash { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal Variance { get; set; }
    }
}
