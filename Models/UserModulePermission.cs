using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class UserModulePermission : BaseEntity
{
    [Required]
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;

    [Required]
    public string ModuleKey { get; set; } = string.Empty; // dashboard, orders, products, customers, etc.

    public bool CanView { get; set; } = true;
    public bool CanEdit { get; set; } = false;
}
