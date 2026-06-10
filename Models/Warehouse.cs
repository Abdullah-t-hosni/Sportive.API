using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class Warehouse : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Location { get; set; }

    public int BranchId { get; set; }
    public Branch Branch { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
