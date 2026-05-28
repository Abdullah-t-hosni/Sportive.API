using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models
{
    public class POSShiftClosure : BaseEntity
    {
        [Required]
        [MaxLength(50)]
        public string StationId { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string ClosureDate { get; set; } = string.Empty; // format: "yyyy-MM-dd"

        [Required]
        [MaxLength(100)]
        public string ClosedBy { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal StartingBalance { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ExpectedCash { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ActualCash { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Variance { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal GrossSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal NetSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CashSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CardSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VodafoneCashSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal InstapaySales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal WalletSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditSales { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Expenses { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SafeDrops { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Returns { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Discounts { get; set; }

        [MaxLength(100)]
        public string? JournalEntryReference { get; set; }
    }
}
