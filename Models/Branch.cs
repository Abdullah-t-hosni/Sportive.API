using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class Branch : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}
