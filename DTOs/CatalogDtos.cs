// ============================================================
// DTOs/CatalogDtos.cs
// تم الفصل من Dtos.cs الكبير — يشمل Categories, Brands, Products
// ============================================================
using Sportive.API.Models;
using Sportive.API.Utils;
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
    [property: JsonPropertyName("type")] CategoryType Type,
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
    [property: JsonPropertyName("type")] CategoryType Type = CategoryType.Men,
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
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("discountPrice")] decimal DiscountPrice,
    [property: JsonPropertyName("mainImageUrl")] string? MainImageUrl,
    [property: JsonPropertyName("categoryNameAr")] string CategoryNameAr,
    [property: JsonPropertyName("categoryNameEn")] string CategoryNameEn,
    [property: JsonPropertyName("brandNameAr")] string? BrandNameAr,
    [property: JsonPropertyName("brandNameEn")] string? BrandNameEn,
    [property: JsonPropertyName("brandId")] int? BrandId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("averageRating")] double AverageRating,
    [property: JsonPropertyName("reviewCount")] int ReviewCount,
    [property: JsonPropertyName("totalStock")] int TotalStock,
    [property: JsonPropertyName("reorderLevel")] int ReorderLevel,
    [property: JsonPropertyName("sku")] string SKU,
    [property: JsonPropertyName("hasVariants")] bool HasVariants,
    [property: JsonPropertyName("variants")] List<ProductVariantDto>? Variants = null,
    [property: JsonPropertyName("hasTax")] bool HasTax = true,
    [property: JsonPropertyName("vatRate")] decimal? VatRate = null,
    [property: JsonPropertyName("costPrice")] decimal? CostPrice = null,
    [property: JsonPropertyName("unitId")] int? UnitId = null,
    [property: JsonPropertyName("unitNameAr")] string? UnitNameAr = null,
    [property: JsonPropertyName("unitNameEn")] string? UnitNameEn = null,
    [property: JsonPropertyName("unitSymbol")] string? UnitSymbol = null,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt = default,
    [property: JsonPropertyName("activeDiscountLabel")] string? ActiveDiscountLabel = null
);

public record ProductDetailDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("descriptionAr")] string? DescriptionAr,
    [property: JsonPropertyName("descriptionEn")] string? DescriptionEn,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("discountPrice")] decimal DiscountPrice,
    [property: JsonPropertyName("costPrice")] decimal? CostPrice,
    [property: JsonPropertyName("sku")] string SKU,
    [property: JsonPropertyName("brandNameAr")] string? BrandNameAr,
    [property: JsonPropertyName("brandNameEn")] string? BrandNameEn,
    [property: JsonPropertyName("brandId")] int? BrandId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("isFeatured")] bool IsFeatured,
    [property: JsonPropertyName("categoryId")] int? CategoryId,
    [property: JsonPropertyName("categoryNameAr")] string? CategoryNameAr,
    [property: JsonPropertyName("categoryNameEn")] string? CategoryNameEn,
    [property: JsonPropertyName("variants")] List<ProductVariantDto> Variants,
    [property: JsonPropertyName("images")] List<ProductImageDto> Images,
    [property: JsonPropertyName("averageRating")] double AverageRating,
    [property: JsonPropertyName("reviewCount")] int ReviewCount,
    [property: JsonPropertyName("totalStock")] int TotalStock,
    [property: JsonPropertyName("reorderLevel")] int ReorderLevel,
    [property: JsonPropertyName("hasTax")] bool HasTax,
    [property: JsonPropertyName("vatRate")] decimal? VatRate,
    [property: JsonPropertyName("unitId")] int? UnitId,
    [property: JsonPropertyName("unitNameAr")] string? UnitNameAr,
    [property: JsonPropertyName("unitNameEn")] string? UnitNameEn,
    [property: JsonPropertyName("unitSymbol")] string? UnitSymbol,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("reviews")] List<ReviewListItemDto>? Reviews = null,
    [property: JsonPropertyName("activeDiscountLabel")] string? ActiveDiscountLabel = null
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
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("descriptionAr")] string? DescriptionAr,
    [property: JsonPropertyName("descriptionEn")] string? DescriptionEn,
    [property: JsonPropertyName("price")] decimal? Price = null,
    [property: JsonPropertyName("discountPrice")] decimal? DiscountPrice = null,
    [property: JsonPropertyName("costPrice")] decimal? CostPrice = null,
    [property: JsonPropertyName("sku")] string SKU = "",
    [property: JsonPropertyName("brandId")] int? BrandId = null,
    [property: JsonPropertyName("categoryId")] int? CategoryId = null,
    [property: JsonPropertyName("isFeatured")] bool IsFeatured = false,
    [property: JsonPropertyName("initialStock")] int? InitialStock = 0,
    [property: JsonPropertyName("variants")] List<CreateVariantDto>? Variants = null,
    [property: JsonPropertyName("reorderLevel")] int? ReorderLevel = 0,
    [property: JsonPropertyName("unitId")] int? UnitId = null,
    [property: JsonPropertyName("hasTax")] bool HasTax = true,
    [property: JsonPropertyName("vatRate")] decimal? VatRate = null
);

public record UpdateProductDto(
    [property: JsonPropertyName("nameAr")] string NameAr,
    [property: JsonPropertyName("nameEn")] string NameEn,
    [property: JsonPropertyName("descriptionAr")] string? DescriptionAr,
    [property: JsonPropertyName("descriptionEn")] string? DescriptionEn,
    [property: JsonPropertyName("price")] decimal? Price = null,
    [property: JsonPropertyName("discountPrice")] decimal? DiscountPrice = null,
    [property: JsonPropertyName("costPrice")] decimal? CostPrice = null,
    [property: JsonPropertyName("brandId")] int? BrandId = null,
    [property: JsonPropertyName("sku")] string SKU = "",
    [property: JsonPropertyName("categoryId")] int? CategoryId = null,
    [property: JsonPropertyName("isFeatured")] bool IsFeatured = false,
    [property: JsonPropertyName("reorderLevel")] int? ReorderLevel = null,
    [property: JsonPropertyName("status")] ProductStatus Status = ProductStatus.Active,
    [property: JsonPropertyName("unitId")] int? UnitId = null,
    [property: JsonPropertyName("hasTax")] bool HasTax = true,
    [property: JsonPropertyName("vatRate")] decimal? VatRate = null
);

public record CreateVariantDto(
    string? Size,
    string? Color,
    string? ColorAr,
    int? StockQuantity,
    decimal? PriceAdjustment,
    int? ReorderLevel = 0
);

// ========== REVIEWS & WISHLIST ==========
public record ReviewListItemDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("customerName")] string CustomerName,
    [property: JsonPropertyName("rating")] int Rating,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt
);

public record AddReviewDto(int ProductId, int Rating, string? Comment);
public record AddToWishlistDto(int ProductId);

// ========== PRODUCT FILTER ==========
public class ProductFilterDto
{
    public int? CategoryId { get; set; }
    public CategoryType? Section { get; set; }
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
    public int? SupplierId { get; set; }
    public bool? OnlyInStock { get; set; }
    public int Page { get; set; } = 1;

    private int _pageSize = 12;
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = AppConstants.ClampPrecacheSize(value);
    }
}

// ========== COUPON ==========
public record CreateCouponDto(
    string Code,
    string? DescriptionAr,
    string? DescriptionEn,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinOrderAmount,
    decimal? MaxDiscountAmount,
    int? MaxUsageCount,
    DateTime? ExpiresAt
);

public record CouponListDto(
    int Id,
    string Code,
    string? DescriptionAr,
    string? DescriptionEn,
    string DiscountType,
    decimal DiscountValue,
    decimal? MinOrderAmount,
    decimal? MaxDiscountAmount,
    int? MaxUsageCount,
    int CurrentUsageCount,
    DateTime? ExpiresAt,
    bool IsActive
);

public record ApplyCouponRequest(string Code, decimal OrderTotal);
