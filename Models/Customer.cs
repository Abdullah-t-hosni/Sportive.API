using Microsoft.AspNetCore.Identity;

namespace Sportive.API.Models;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public decimal FixedDiscount { get; set; } = 0;
}

public class Customer : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal FixedDiscount { get; set; } = 0;
    public string Tags { get; set; } = "[]"; 
    public string? Notes { get; set; }
    public int? MainAccountId { get; set; }
    public Account? MainAccount { get; set; }

    // Navigation
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}

public class Address : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public string TitleAr { get; set; } = string.Empty;  // e.g. "المنزل", "العمل"
    public string TitleEn { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? BuildingNo { get; set; }
    public string? Floor { get; set; }
    public string? ApartmentNo { get; set; }
    public string? AdditionalInfo { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsDefault { get; set; } = false;
}
