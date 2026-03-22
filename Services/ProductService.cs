using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;

    public ProductService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<ProductSummaryDto>> GetProductsAsync(ProductFilterDto filter)
    {
        var query = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Include(p => p.Variants)
            .AsQueryable();

        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == filter.CategoryId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            query = query.Where(p =>
                p.NameAr.ToLower().Contains(s) ||
                p.NameEn.ToLower().Contains(s) ||
                (p.Brand != null && p.Brand.ToLower().Contains(s)));
        }

        if (filter.MinPrice.HasValue)
            query = query.Where(p => p.Price >= filter.MinPrice);

        if (filter.MaxPrice.HasValue)
            query = query.Where(p => p.Price <= filter.MaxPrice);

        if (!string.IsNullOrWhiteSpace(filter.Brand))
            query = query.Where(p => p.Brand == filter.Brand);

        if (!string.IsNullOrWhiteSpace(filter.Size))
            query = query.Where(p => p.Variants.Any(v => v.Size == filter.Size));

        if (filter.IsFeatured.HasValue)
            query = query.Where(p => p.IsFeatured == filter.IsFeatured);

        // Sorting
        query = filter.SortBy switch
        {
            "price" => filter.SortDir == "asc" ? query.OrderBy(p => p.Price) : query.OrderByDescending(p => p.Price),
            "name" => filter.SortDir == "asc" ? query.OrderBy(p => p.NameEn) : query.OrderByDescending(p => p.NameEn),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        var total = await query.CountAsync();

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(p => new ProductSummaryDto(
                p.Id,
                p.NameAr,
                p.NameEn,
                p.Price,
                p.DiscountPrice,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category.NameAr,
                p.Category.NameEn,
                p.Brand,
                p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : null,
                p.Reviews.Count
            ))
            .ToListAsync();

        return new PaginatedResult<ProductSummaryDto>(
            items, total, filter.Page, filter.PageSize,
            (int)Math.Ceiling((double)total / filter.PageSize)
        );
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(int id)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.Variants)
            .Include(x => x.Reviews)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return null;
        return MapToDetail(p);
    }

    public async Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto)
    {
        var product = new Product
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Price = dto.Price,
            DiscountPrice = dto.DiscountPrice,
            SKU = dto.SKU,
            Brand = dto.Brand,
            CategoryId = dto.CategoryId,
            IsFeatured = dto.IsFeatured,
            Status = ProductStatus.Active
        };

        if (dto.Variants != null)
        {
            foreach (var v in dto.Variants)
            {
                product.Variants.Add(new ProductVariant
                {
                    Size = v.Size,
                    Color = v.Color,
                    ColorAr = v.ColorAr,
                    StockQuantity = v.StockQuantity,
                    PriceAdjustment = v.PriceAdjustment
                });
            }
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return await GetProductByIdAsync(product.Id)
            ?? throw new Exception("Product not found after creation");
    }

    public async Task<ProductDetailDto> UpdateProductAsync(int id, UpdateProductDto dto)
    {
        var product = await _db.Products.FindAsync(id)
            ?? throw new KeyNotFoundException($"Product {id} not found");

        product.NameAr = dto.NameAr;
        product.NameEn = dto.NameEn;
        product.DescriptionAr = dto.DescriptionAr;
        product.DescriptionEn = dto.DescriptionEn;
        product.Price = dto.Price;
        product.DiscountPrice = dto.DiscountPrice;
        product.Brand = dto.Brand;
        product.CategoryId = dto.CategoryId;
        product.IsFeatured = dto.IsFeatured;
        product.Status = dto.Status;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await GetProductByIdAsync(id)
            ?? throw new InvalidOperationException($"Product {id} not found after update");
    }

    public async Task DeleteProductAsync(int id)
    {
        var product = await _db.Products.FindAsync(id)
            ?? throw new KeyNotFoundException($"Product {id} not found");
        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<bool> UpdateStockAsync(int variantId, int quantity)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return false;
        variant.StockQuantity = quantity;
        variant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<ProductSummaryDto>> GetFeaturedProductsAsync(int count = 8)
    {
        return await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Where(p => p.IsFeatured && p.Status == ProductStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .Select(p => new ProductSummaryDto(
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category.NameAr, p.Category.NameEn, p.Brand, p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : null,
                p.Reviews.Count
            ))
            .ToListAsync();
    }

    public async Task<List<ProductSummaryDto>> GetRelatedProductsAsync(int productId, int count = 4)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return new List<ProductSummaryDto>();

        return await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != productId && p.Status == ProductStatus.Active)
            .OrderBy(_ => Guid.NewGuid())
            .Take(count)
            .Select(p => new ProductSummaryDto(
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category.NameAr, p.Category.NameEn, p.Brand, p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : null,
                p.Reviews.Count
            ))
            .ToListAsync();
    }

    private static ProductDetailDto MapToDetail(Product p) => new(
        p.Id, p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn,
        p.Price, p.DiscountPrice, p.SKU, p.Brand, p.Status.ToString(), p.IsFeatured,
        p.CategoryId, p.Category.NameAr, p.Category.NameEn,
        p.Variants.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.PriceAdjustment, v.ImageUrl)).ToList(),
        p.Images.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.IsMain, i.SortOrder)).ToList(),
        p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : null,
        p.Reviews.Count
    );
}
