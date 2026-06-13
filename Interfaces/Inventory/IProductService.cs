using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IProductService
{
    Task<PaginatedResult<ProductSummaryDto>> GetProductsAsync(ProductFilterDto filter);
    Task<ProductDetailDto?> GetProductByIdAsync(int id, DiscountApplyTo? source = null, int? warehouseId = null);
    Task<ProductDetailDto?> GetProductBySlugAsync(string slug, DiscountApplyTo? source = null, int? warehouseId = null);
    Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto);
    Task<ProductDetailDto> UpdateProductAsync(int id, UpdateProductDto dto);
    Task DeleteProductAsync(int id);
    Task<bool> UpdateStockAsync(int variantId, int quantity);
    Task<bool> UpdateProductStockAsync(int productId, int quantity);
    Task<ProductVariantDto> AddVariantAsync(int productId, CreateVariantDto dto);
    Task<ProductVariantDto> UpdateVariantAsync(int variantId, CreateVariantDto dto);
    Task<bool> DeleteVariantAsync(int variantId);
    Task<bool> UpdateCostPriceAsync(int productId, decimal? costPrice);
    Task<bool> UpdateSizeChartAsync(int productId, string? sizeChartJson, string? sizeChartImageUrl);
    Task UpdateTotalStockAsync(int productId);
    Task SyncAllProductsStatusAndStockAsync();
    Task SyncAllProductRatingsAsync();
    Task<List<ProductSummaryDto>> GetFeaturedProductsAsync(int count = 8, int? warehouseId = null);
    Task<List<ProductSummaryDto>> GetRelatedProductsAsync(int productId, int count = 4, int? warehouseId = null);
}
