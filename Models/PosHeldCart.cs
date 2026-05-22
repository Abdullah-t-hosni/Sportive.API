using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sportive.API.Models
{
    public class PosHeldCart
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ReferenceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public string ItemsJson { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
