// ============================================================
// DTOs/CatalogDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Categories, Brands, Products
// ============================================================
using Sportive.API.Models;
using System.Text.Json.Serialization;

namespace Sportive.API.DTOs;

// ========== CATEGORY ==========
public record CategoryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("descriptionAr")] string? DescriptionAr,
    [property: JsonPropertyName("descriptionEn")] string? DescriptionEn,
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("productCount")] int ProductCount,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("parentId")] int? ParentId = null,
    [property: JsonPropertyName("parentCategoryNameAr")] string? ParentCategoryNameAr = null,
    [property: JsonPropertyName("parentCategoryNameEn")] string? ParentCategoryNameEn = null,
    [property: JsonPropertyName("subCategories")] List<CategoryDto>? SubCategories = null
);

public record CreateCategoryDto(
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("descriptionAr")] string? DescriptionAr,
    [property: JsonPropertyName("descriptionEn")] string? DescriptionEn,
    [property: JsonPropertyName("imageUrl")] string? ImageUrl,
    [property: JsonPropertyName("parentId")] int? ParentId = null
);

// ========== BRAND ==========
public record BrandDto(
    int Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    bool IsActive,
    int? ParentId = null,
    string? ParentNameAr = null,
    string? ParentNameEn = null,
    int ProductCount = 0,
    DateTime? CreatedAt = null
);

public record CreateBrandDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    int? ParentId = null
);

public record UpdateBrandDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string? ImageUrl,
    bool IsActive,
    int? ParentId = null
);

// ========== PRODUCT ==========
public record ProductSummaryDto(
    int Id,
    string NameAr,
    string NameEn,
    decimal Price,
    decimal DiscountPrice,
    string? MainImageUrl,
    string CategoryNameAr,
    string CategoryNameEn,
    string? BrandNameAr,
    string? BrandNameEn,
    int? BrandId,
    string Status,
    double AverageRating,
    int ReviewCount,
    int TotalStock,
    int ReorderLevel,
    string SKU,
    bool HasVariants,
    bool HasTax,
    decimal? VatRate,
    DateTime CreatedAt
);

public record ProductDetailDto(
    int Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal DiscountPrice,
    decimal? CostPrice,
    string SKU,
    string? BrandNameAr,
    string? BrandNameEn,
    int? BrandId,
    string Status,
    bool IsFeatured,
    int CategoryId,
    string CategoryNameAr,
    string CategoryNameEn,
    List<ProductVariantDto> Variants,
    List<ProductImageDto> Images,
    double AverageRating,
    int ReviewCount,
    int TotalStock,
    int ReorderLevel,
    bool HasTax,
    decimal? VatRate,
    DateTime CreatedAt
);

public record ProductVariantDto(
    int Id,
    string? Size,
    string? Color,
    string? ColorAr,
    int StockQuantity,
    int ReorderLevel,
    decimal PriceAdjustment,
    string? ImageUrl,
    string? ImagePublicId = null
);

public record ProductImageDto(int Id, string ImageUrl, string? ImagePublicId = null, bool IsMain = false, int SortOrder = 0, string? ColorAr = null);

public record CreateProductDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal? DiscountPrice,
    decimal? CostPrice,
    string SKU,
    int? BrandId,
    int CategoryId,
    bool IsFeatured,
    List<CreateVariantDto>? Variants,
    int ReorderLevel = 0,
    bool HasTax = true,
    decimal? VatRate = null
);

public record UpdateProductDto(
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    decimal Price,
    decimal? DiscountPrice,
    decimal? CostPrice,
    int? BrandId,
    string SKU,
    int CategoryId,
    bool IsFeatured,
    int ReorderLevel,
    ProductStatus Status,
    bool HasTax = true,
    decimal? VatRate = null
);

public record CreateVariantDto(
    string? Size,
    string? Color,
    string? ColorAr,
    int StockQuantity,
    decimal? PriceAdjustment,
    int ReorderLevel = 0
);

// ========== REVIEWS & WISHLIST ==========
public record AddReviewDto(int ProductId, int Rating, string? Comment);
public record AddToWishlistDto(int ProductId);

// ========== PRODUCT FILTER ==========
public class ProductFilterDto
{
    public int? CategoryId { get; set; }
    public string? Search { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? BrandId { get; set; }
    public string? Size { get; set; }
    public string? Color { get; set; }
    public bool? IsFeatured { get; set; }
    public ProductStatus? Status { get; set; }
    public string SortBy { get; set; } = "createdAt";
    public string SortDir { get; set; } = "desc";
    public int Page { get; set; } = 1;

    private int _pageSize = 12;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }
}
