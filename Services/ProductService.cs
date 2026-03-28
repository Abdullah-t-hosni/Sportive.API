using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public ProductService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

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
            var s = filter.Search.Trim().ToLower();
            bool isInt = int.TryParse(s, out int searchId);
            bool isDecimal = decimal.TryParse(s, out decimal searchPrice);
            
            query = query.Where(p =>
                p.NameAr.ToLower().Contains(s) ||
                p.NameEn.ToLower().Contains(s) ||
                p.SKU.ToLower().Contains(s) ||
                (isInt && p.Id == searchId) ||
                (isDecimal && (p.Price == searchPrice || p.DiscountPrice == searchPrice)) ||
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

        if (!string.IsNullOrWhiteSpace(filter.Color))
            query = query.Where(p => p.Variants.Any(v => v.Color == filter.Color || v.ColorAr == filter.Color));

        if (filter.IsFeatured.HasValue)
            query = query.Where(p => p.IsFeatured == filter.IsFeatured);

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status);

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
                p.DiscountPrice ?? 0,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category != null ? p.Category.NameAr : "Category Missing",
                p.Category != null ? p.Category.NameEn : "Category Missing",
                p.Brand,
                p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.SKU,
                p.CreatedAt
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
        // التحقق من تكرار الكود
        if (await _db.Products.AnyAsync(p => p.SKU == dto.SKU))
            throw new ArgumentException($"كود المنتج {dto.SKU} مستخدم بالفعل لمنتج آخر.");

        var product = new Product
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Price = dto.Price,
            DiscountPrice = dto.DiscountPrice,
            CostPrice = dto.CostPrice,
            SKU = dto.SKU,
            Brand = dto.Brand,
            CategoryId = dto.CategoryId,
            IsFeatured = dto.IsFeatured,
            Status = ProductStatus.Active
        };

        if (dto.Variants != null && dto.Variants.Any())
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
            product.TotalStock = product.Variants.Sum(v => v.StockQuantity);
        }
        else
        {
             // For simple products, we rely on the purchase invoices or manual set (if exposed)
             // Initial creation might have 0 stock.
             product.TotalStock = 0;
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        return await GetProductByIdAsync(product.Id)
            ?? throw new Exception("Product not found after creation");
    }

    public async Task<ProductDetailDto> UpdateProductAsync(int id, UpdateProductDto dto)
    {
        // التحقق من تكرار الكود مع استبعاد المنتج الحالي
        if (await _db.Products.AnyAsync(p => p.SKU == dto.SKU && p.Id != id))
            throw new ArgumentException($"كود المنتج {dto.SKU} مستخدم بالفعل لمنتج آخر.");

        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException($"Product {id} not found");

        product.NameAr = dto.NameAr;
        product.NameEn = dto.NameEn;
        product.DescriptionAr = dto.DescriptionAr;
        product.DescriptionEn = dto.DescriptionEn;
        product.Price = dto.Price;
        product.DiscountPrice = dto.DiscountPrice;
        product.CostPrice = dto.CostPrice;
        product.Brand = dto.Brand;
        product.SKU = dto.SKU;
        product.CategoryId = dto.CategoryId;
        product.IsFeatured = dto.IsFeatured;
        product.Status = dto.Status;
        product.UpdatedAt = DateTime.UtcNow;

        // إعادة حساب إجمالي المخزون للتأكد من الدقة
        if (product.Variants.Any(v => !v.IsDeleted))
        {
             product.TotalStock = product.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity);
        }
        // If simple product (no variants), we don't zero out TotalStock as it might have manual/purchase stock

        await _db.SaveChangesAsync();
        return await GetProductByIdAsync(id) ?? throw new KeyNotFoundException($"Product {id} not found after update");
    }

    public async Task DeleteProductAsync(int id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException($"Product {id} not found");

        product.IsDeleted = true;
        product.UpdatedAt = DateTime.UtcNow;

        foreach (var v in product.Variants) { v.IsDeleted = true; v.UpdatedAt = DateTime.UtcNow; }
        foreach (var i in product.Images)   { i.IsDeleted = true; i.UpdatedAt = DateTime.UtcNow; }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> UpdateStockAsync(int variantId, int quantity)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return false;
        variant.StockQuantity = quantity;
        variant.UpdatedAt = DateTime.UtcNow;

        await UpdateTotalStockAsync(variant.ProductId);

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(variant.ProductId, variantId, quantity);
        return true;
    }

    public async Task<bool> UpdateCostPriceAsync(int productId, decimal? costPrice)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return false;
        product.CostPrice = costPrice;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateTotalStockAsync(int productId)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product != null)
        {
            if (product.Variants.Any(v => !v.IsDeleted))
            {
                product.TotalStock = product.Variants.Where(v => !v.IsDeleted).Sum(v => v.StockQuantity);
            }
            else if (product.Variants.Any())
            {
                 // Has variants but all are deleted
                 product.TotalStock = 0;
            }
            product.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<ProductVariantDto> AddVariantAsync(int productId, CreateVariantDto dto)
    {
        var v = new ProductVariant
        {
            ProductId = productId,
            Size = dto.Size,
            Color = dto.Color,
            ColorAr = dto.ColorAr,
            StockQuantity = dto.StockQuantity,
            PriceAdjustment = dto.PriceAdjustment
        };
        _db.ProductVariants.Add(v);
        
        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == productId);
        if (product != null)
        {
            await UpdateTotalStockAsync(productId);
            product.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, v.StockQuantity);
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId);
    }

    public async Task<ProductVariantDto> UpdateVariantAsync(int variantId, CreateVariantDto dto)
    {
        var v = await _db.ProductVariants.FindAsync(variantId)
            ?? throw new KeyNotFoundException("Variant not found");
        v.Size = dto.Size;
        v.Color = dto.Color;
        v.ColorAr = dto.ColorAr;
        v.StockQuantity = dto.StockQuantity;
        v.PriceAdjustment = dto.PriceAdjustment;
        v.UpdatedAt = DateTime.UtcNow;

        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == v.ProductId);
        if (product != null)
        {
            await UpdateTotalStockAsync(v.ProductId);
            product.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, v.StockQuantity);
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId);
    }

    public async Task<bool> DeleteVariantAsync(int variantId)
    {
        var v = await _db.ProductVariants.FindAsync(variantId);
        if (v == null) return false;
        v.IsDeleted = true;
        v.UpdatedAt = DateTime.UtcNow;

        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == v.ProductId);
        if (product != null)
        {
            await UpdateTotalStockAsync(v.ProductId);
            product.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, 0);
        
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
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice ?? 0,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category.NameAr, p.Category.NameEn, p.Brand, p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.SKU,
                p.CreatedAt
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
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice ?? 0,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category.NameAr, p.Category.NameEn, p.Brand, p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.SKU,
                p.CreatedAt
            ))
            .ToListAsync();
    }

    private static ProductDetailDto MapToDetail(Product p) => new(
        p.Id, p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn,
        p.Price, p.DiscountPrice ?? 0, p.CostPrice, p.SKU, p.Brand, p.Status.ToString(), p.IsFeatured,
        p.CategoryId, p.Category?.NameAr ?? "Category Missing", p.Category?.NameEn ?? "Category Missing",
        p.Variants?.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId)).ToList() ?? new List<ProductVariantDto>(),
        p.Images?.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.ImagePublicId, i.IsMain, i.SortOrder, i.ColorAr)).ToList() ?? new List<ProductImageDto>(),
        p.Reviews?.Any() == true ? p.Reviews.Average(r => r.Rating) : 0,
        p.Reviews?.Count ?? 0,
        p.TotalStock,
        p.CreatedAt
    );
}
