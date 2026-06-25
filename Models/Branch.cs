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

    // ── Punch Constraints ──
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int AllowedPunchRadiusMeters { get; set; } = 50;
    
    [MaxLength(100)]
    public string? AllowedIpAddress { get; set; }

    // Navigation
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();

    public int? LinkedWarehouseId { get; set; }
    public Warehouse? LinkedWarehouse { get; set; }
}
