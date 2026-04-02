// ============================================================
// DTOs/CustomerDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Customers, Addresses
// ============================================================
namespace Sportive.API.DTOs;

// ========== CUSTOMER ==========
public record CustomerBasicDto(
    int Id,
    string FullName,
    string Email,
    string? Phone
);

public record CustomerDetailDto(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int TotalOrders,
    decimal TotalSpent,
    DateTime CreatedAt,
    List<AddressDto> Addresses,
    string? AppUserId = null,
    decimal Balance = 0,
    int? MainAccountId = null
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
    string FirstName,
    string? LastName = null,
    string? Email = null,
    string? Phone = null
);

public record UpdateCustomerDto(
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool IsActive = true,
    int? MainAccountId = null
);
