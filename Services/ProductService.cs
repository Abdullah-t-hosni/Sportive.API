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

    private readonly IInventoryService _inventory;

    public ProductService(AppDbContext db, INotificationService notifications, IInventoryService inventory)
    {
        _db = db;
        _notifications = notifications;
        _inventory = inventory;
    }

    public async Task<PaginatedResult<ProductSummaryDto>> GetProductsAsync(ProductFilterDto filter)
    {
        var query = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
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
                (p.Brand != null && (p.Brand.NameAr.ToLower().Contains(s) || p.Brand.NameEn.ToLower().Contains(s))));
        }

        if (filter.MinPrice.HasValue)
            query = query.Where(p => p.Price >= filter.MinPrice);

        if (filter.MaxPrice.HasValue)
            query = query.Where(p => p.Price <= filter.MaxPrice);

        if (filter.BrandId.HasValue)
            query = query.Where(p => p.BrandId == filter.BrandId);

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

        var page = Math.Max(1, filter.Page);
        var items = await query
            .Skip((page - 1) * filter.PageSize)
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
                p.Brand != null ? p.Brand.NameAr : null,
                p.Brand != null ? p.Brand.NameEn : null,
                p.BrandId,
                p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.ReorderLevel,
                p.SKU,
                p.Variants.Any(),
                p.HasTax,
                p.VatRate,
                p.CostPrice,
                p.CreatedAt
            ))
            .ToListAsync();

        return new PaginatedResult<ProductSummaryDto>(
            items, total, page, filter.PageSize,
            (int)Math.Ceiling((double)total / filter.PageSize)
        );
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(int id)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .Include(x => x.Brand)
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

        if (!dto.Price.HasValue || dto.Price <= 0)
            throw new ArgumentException("سعر المنتج مطلوب ويجب أن يكون أكبر من صفر.");

        var product = new Product
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Price = dto.Price.Value,
            DiscountPrice = dto.DiscountPrice,
            CostPrice = dto.CostPrice,
            SKU = dto.SKU,
            BrandId = dto.BrandId,
            CategoryId = dto.CategoryId,
            IsFeatured = dto.IsFeatured,
            ReorderLevel = dto.ReorderLevel ?? 0,
            HasTax = dto.HasTax,
            VatRate = dto.VatRate,
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
                    StockQuantity = v.StockQuantity ?? 0,
                    ReorderLevel = v.ReorderLevel ?? 0,
                    PriceAdjustment = v.PriceAdjustment
                });
            }
            product.TotalStock = product.Variants.Sum(v => v.StockQuantity);
        }
        else
        {
             // For simple products (like many Equipment/Tools), we can set initial stock directly
             product.TotalStock = dto.InitialStock ?? 0;
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // 3. Log initial movements
        if (product.Variants.Any())
        {
            foreach (var v in product.Variants)
            {
                if (v.StockQuantity != 0)
                {
                    await _inventory.LogMovementAsync(
                        InventoryMovementType.OpeningBalance,
                        v.StockQuantity,
                        product.Id,
                        v.Id,
                        "INIT-PRODUCT",
                        "رصيد افتتاحي عند إنشاء المنتج",
                        null
                    );
                }
            }
        }
        else if (product.TotalStock != 0)
        {
            // For simple products without variants (Tools/Equipment)
            await _inventory.LogMovementAsync(
                InventoryMovementType.OpeningBalance,
                product.TotalStock,
                product.Id,
                null,
                "INIT-PRODUCT",
                "رصيد افتتاحي عند إنشاء المنتج",
                null
            );
        }
        
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

        if (!dto.Price.HasValue || dto.Price <= 0)
            throw new ArgumentException("سعر المنتج مطلوب ويجب أن يكون أكبر من صفر.");

        product.NameAr = dto.NameAr;
        product.NameEn = dto.NameEn;
        product.DescriptionAr = dto.DescriptionAr;
        product.DescriptionEn = dto.DescriptionEn;
        product.Price = dto.Price.Value;
        product.DiscountPrice = dto.DiscountPrice;
        product.CostPrice = dto.CostPrice;
        product.BrandId = dto.BrandId;
        product.SKU = dto.SKU;
        product.CategoryId = dto.CategoryId;
        product.IsFeatured = dto.IsFeatured;
        product.ReorderLevel = dto.ReorderLevel ?? 0;
        product.Status = dto.Status;
        product.HasTax = dto.HasTax;
        product.VatRate = dto.VatRate;
        product.UpdatedAt = DateTime.UtcNow;

        // إعادة حساب إجمالي المخزون للتأكد من الدقة
        if (product.Variants.Any())
        {
             product.TotalStock = product.Variants.Sum(v => v.StockQuantity);
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

        // Hard delete — variants and images cascade via DB
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> UpdateStockAsync(int variantId, int quantity)
    {
        var variant = await _db.ProductVariants.FindAsync(variantId);
        if (variant == null) return false;
        
        var diff = quantity - variant.StockQuantity;
        if (diff == 0) return true;

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            diff,
            variant.ProductId,
            variant.Id,
            "MANUAL-UPDATE",
            "تحديث يدوي من صفحة المنتج",
            null
        );

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(variant.ProductId, variantId, quantity);
        return true;
    }

    public async Task<bool> UpdateProductStockAsync(int productId, int quantity)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return false;

        var diff = quantity - product.TotalStock;
        if (diff == 0) return true;

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            diff,
            product.Id,
            null,
            "MANUAL-UPDATE",
            "تحديث يدوي من صفحة المنتج",
            null
        );

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(product.Id, 0, quantity);
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
            if (product.Variants.Any())
            {
                product.TotalStock = product.Variants.Sum(v => v.StockQuantity);
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
            StockQuantity = dto.StockQuantity ?? 0,
            ReorderLevel = dto.ReorderLevel ?? 0,
            PriceAdjustment = dto.PriceAdjustment
        };
        _db.ProductVariants.Add(v);
        await _db.SaveChangesAsync(); // Save to get the ID

        if (dto.StockQuantity != 0)
        {
            await _inventory.LogMovementAsync(
                InventoryMovementType.OpeningBalance,
                dto.StockQuantity ?? 0,
                productId,
                v.Id,
                "INIT-VARIANT",
                "رصيد افتتاحي للموديل الجديد",
                null
            );
            await _db.SaveChangesAsync();
        }

        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, v.StockQuantity);
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId);
    }

    public async Task<ProductVariantDto> UpdateVariantAsync(int variantId, CreateVariantDto dto)
    {
        var v = await _db.ProductVariants.FindAsync(variantId)
            ?? throw new KeyNotFoundException("Variant not found");
        v.Size = dto.Size;
        v.Color = dto.Color;
        v.ColorAr = dto.ColorAr;
        v.StockQuantity = dto.StockQuantity ?? 0;
        v.ReorderLevel = dto.ReorderLevel ?? 0;
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
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId);
    }

    public async Task<bool> DeleteVariantAsync(int variantId)
    {
        var v = await _db.ProductVariants.FindAsync(variantId);
        if (v == null) return false;

        var productId = v.ProductId;
        _db.ProductVariants.Remove(v);
        await _db.SaveChangesAsync();

        await UpdateTotalStockAsync(productId);
        var product = await _db.Products.FindAsync(productId);
        if (product != null) { product.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }

        await _notifications.BroadcastStockUpdateAsync(productId, variantId, 0);
        return true;
    }

    public async Task<List<ProductSummaryDto>> GetFeaturedProductsAsync(int count = 8)
    {
        return await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Where(p => p.IsFeatured && p.Status == ProductStatus.Active)
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .Select(p => new ProductSummaryDto(
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice ?? 0,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category != null ? p.Category.NameAr : "Category Missing", 
                p.Category != null ? p.Category.NameEn : "Category Missing", 
                p.Brand != null ? p.Brand.NameAr : null, 
                p.Brand != null ? p.Brand.NameEn : null,
                p.BrandId,
                p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.ReorderLevel,
                p.SKU,
                p.Variants != null && p.Variants.Any(),
                p.HasTax,
                p.VatRate,
                p.CostPrice,
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
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Reviews)
            .Where(p => p.CategoryId == product.CategoryId && p.Id != productId && p.Status == ProductStatus.Active)
            .OrderBy(_ => Guid.NewGuid())
            .Take(count)
            .Select(p => new ProductSummaryDto(
                p.Id, p.NameAr, p.NameEn, p.Price, p.DiscountPrice ?? 0,
                p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                p.Category != null ? p.Category.NameAr : "Category Missing", 
                p.Category != null ? p.Category.NameEn : "Category Missing", 
                p.Brand != null ? p.Brand.NameAr : null, 
                p.Brand != null ? p.Brand.NameEn : null,
                p.BrandId,
                p.Status.ToString(),
                p.Reviews.Any() ? p.Reviews.Average(r => r.Rating) : 0,
                p.Reviews.Count,
                p.TotalStock,
                p.ReorderLevel,
                p.SKU,
                p.Variants != null && p.Variants.Any(),
                p.HasTax,
                p.VatRate,
                p.CostPrice,
                p.CreatedAt
            ))
            .ToListAsync();
    }

    private static ProductDetailDto MapToDetail(Product p) => new(
        p.Id, p.NameAr, p.NameEn, p.DescriptionAr, p.DescriptionEn,
        p.Price, p.DiscountPrice ?? 0, p.CostPrice, p.SKU, 
        p.Brand != null ? p.Brand.NameAr : null,
        p.Brand != null ? p.Brand.NameEn : null,
        p.BrandId,
        p.Status.ToString(), p.IsFeatured,
        p.CategoryId, p.Category?.NameAr ?? "Category Missing", p.Category?.NameEn ?? "Category Missing",
        p.Variants?.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId)).ToList() ?? new List<ProductVariantDto>(),
        p.Images?.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.ImagePublicId, i.IsMain, i.SortOrder, i.ColorAr)).ToList() ?? new List<ProductImageDto>(),
        p.Reviews?.Any() == true ? p.Reviews.Average(r => r.Rating) : 0,
        p.Reviews?.Count ?? 0,
        p.TotalStock,
        p.ReorderLevel,
        p.HasTax,
        p.VatRate,
        p.CreatedAt
    );
}
