using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class UserModulePermission
{
    [Key]
    public int PermissionEntryId { get; set; }

    [Required]
    public string UserAccountID { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [Required]
    public string ModuleKey { get; set; } = string.Empty; // dashboard, orders, products, etc.

    public bool CanView { get; set; } = true;
    public bool CanEdit { get; set; } = false;

    // Manually added BaseEntity fields to match the SQL table
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}
