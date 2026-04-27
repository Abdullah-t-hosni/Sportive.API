// ============================================================
// DTOs/CustomerDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Customers, Addresses
// ============================================================
using System.Text.Json.Serialization;

namespace Sportive.API.DTOs;

// ========== CUSTOMER CATEGORY ==========
public record CustomerCategoryDto(
    int Id,
    string NameAr,
    string NameEn,
    string? Description,
    decimal DefaultDiscount,
    decimal MinimumSpending,
    bool IsActive,
    int CustomerCount
);

public record CreateCustomerCategoryDto(
    string NameAr,
    string NameEn,
    string? Description,
    decimal DefaultDiscount = 0,
    decimal MinimumSpending = 0
);

public record UpdateCustomerCategoryDto(
    string NameAr,
    string NameEn,
    string? Description,
    decimal DefaultDiscount,
    decimal MinimumSpending,
    bool IsActive
);

// ========== CUSTOMER ==========
public record CustomerBasicDto(
    int Id,
    string FullName,
    string Email,
    string? Phone,
    decimal FixedDiscount = 0
);

public record CustomerDetailDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("totalOrders")] int TotalOrders,
    [property: JsonPropertyName("totalSpent")] decimal TotalSpent,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("addresses")] List<AddressDto> Addresses,
    [property: JsonPropertyName("appUserId")] string? AppUserId = null,
    [property: JsonPropertyName("balance")] decimal Balance = 0,
    [property: JsonPropertyName("mainAccountId")] int? MainAccountId = null,
    [property: JsonPropertyName("categoryId")] int? CategoryId = null,
    [property: JsonPropertyName("categoryName")] string? CategoryName = null,
    [property: JsonPropertyName("fixedDiscount")] decimal FixedDiscount = 0,
    [property: JsonPropertyName("tags")] List<string>? Tags = null
);

// ========== RFM (lightweight — no addresses, no balance) ==========
public record CustomerRfmDto(
    [property: JsonPropertyName("id")]          int      Id,
    [property: JsonPropertyName("fullName")]    string   FullName,
    [property: JsonPropertyName("phone")]       string?  Phone,
    [property: JsonPropertyName("totalOrders")] int      TotalOrders,
    [property: JsonPropertyName("totalSpent")]  decimal  TotalSpent,
    [property: JsonPropertyName("createdAt")]   DateTime CreatedAt
);

// ========== ADDRESS ==========
public record AddressDto(
    int Id,
    string TitleAr,
    string TitleEn,
    string Street,
    string City,
    string? District,
    string? BuildingNo,
    string? Floor,
    string? ApartmentNo,
    bool IsDefault,
    double? Latitude = null,
    double? Longitude = null
);

public record CreateAddressDto(
    string TitleAr,
    string TitleEn,
    string Street,
    string City,
    string? District,
    string? BuildingNo,
    string? Floor,
    string? ApartmentNo,
    string? AdditionalInfo,
    double? Latitude,
    double? Longitude
);

public record CreateCustomerDto(
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("phone")] string? Phone = null,
    [property: JsonPropertyName("fixedDiscount")] decimal FixedDiscount = 0,
    [property: JsonPropertyName("tags")] List<string>? Tags = null
);

public record UpdateCustomerDto(
    [property: JsonPropertyName("fullName")] string FullName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("isActive")] bool IsActive = true,
    [property: JsonPropertyName("mainAccountId")] int? MainAccountId = null,
    [property: JsonPropertyName("fixedDiscount")] decimal FixedDiscount = 0,
    [property: JsonPropertyName("tags")] List<string>? Tags = null
);
