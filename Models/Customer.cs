using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations.Schema;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public DateTime CreatedAt { get; set; } = TimeHelper.GetEgyptTime();
    public bool IsActive { get; set; } = true;
    public decimal FixedDiscount { get; set; } = 0;

    // Refresh Token — مخزن في DB لدعم التجديد والإلغاء
    public string?   RefreshTokenHash   { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    // User preferences (sidebar pinned items and favorite reports)
    public string PinnedSidebarItems { get; set; } = "[]";
    public string FavoriteReports { get; set; } = "[\"trial\", \"sales\"]";
    public string UiPreferences { get; set; } = "{}";

    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
}

public class Customer : BaseEntity
{
    public static EncryptionHelper? EncryptionHelper { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string EmailEncrypted { get; set; } = string.Empty;
    public string EmailHash { get; set; } = string.Empty;
    public int EmailKeyVersion { get; set; } = 1;

    public string? PhoneEncrypted { get; set; }
    public string? PhoneHash { get; set; }
    public int PhoneKeyVersion { get; set; } = 1;

    [NotMapped]
    public string Email
    {
        get => EncryptionHelper != null ? EncryptionHelper.Decrypt(EmailEncrypted) : EmailEncrypted;
        set
        {
            EmailEncrypted = EncryptionHelper != null ? EncryptionHelper.Encrypt(value) : value;
            EmailHash = EncryptionHelper != null ? EncryptionHelper.ComputeSearchHash(value) : "";
            EmailKeyVersion = 1;
        }
    }

    [NotMapped]
    public string? Phone
    {
        get => EncryptionHelper != null && PhoneEncrypted != null ? EncryptionHelper.Decrypt(PhoneEncrypted) : PhoneEncrypted;
        set
        {
            if (value == null)
            {
                PhoneEncrypted = null;
                PhoneHash = null;
            }
            else
            {
                PhoneEncrypted = EncryptionHelper != null ? EncryptionHelper.Encrypt(value) : value;
                PhoneHash = EncryptionHelper != null ? EncryptionHelper.ComputeSearchHash(value) : "";
                PhoneKeyVersion = 1;
            }
        }
    }
    public string? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal FixedDiscount { get; set; } = 0;
    public string Tags { get; set; } = "[]"; 
    public string? Notes { get; set; }
    public int? MainAccountId { get; set; }
    public Account? MainAccount { get; set; }

    public int? CategoryId { get; set; }
    public CustomerCategory? Category { get; set; }

    // Financial Tracking
    public decimal TotalSales { get; set; } = 0;
    public decimal TotalPaid  { get; set; } = 0;
    public decimal Balance    => TotalSales - TotalPaid;

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
