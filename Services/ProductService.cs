using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

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
        
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        if (store != null && store.HideOutOfStock && !filter.Status.HasValue)
        {
            query = query.Where(p => p.TotalStock > 0);
        }

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

        var now = TimeHelper.GetEgyptTime();
        var page = Math.Max(1, filter.Page);
        var items = await query
            .GroupJoin(_db.ProductDiscounts.Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now),
                p => p.Id,
                d => d.ProductId,
                (p, ds) => new { p, d = ds.FirstOrDefault() })
            .Skip((page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new ProductSummaryDto(
                x.p.Id,
                x.p.NameAr,
                x.p.NameEn,
                x.p.Slug,
                x.p.Price,
                x.d != null 
                    ? (x.d.DiscountType == DiscountType.Percentage ? Math.Round(x.p.Price - (x.p.Price * x.d.DiscountValue / 100), 2) : Math.Round(x.p.Price - x.d.DiscountValue, 2)) 
                    : (x.p.DiscountPrice ?? 0),
                x.p.Images.Where(i => i.IsMain).Select(i => i.ImageUrl).FirstOrDefault(),
                x.p.Category != null ? x.p.Category.NameAr : "Category Missing",
                x.p.Category != null ? x.p.Category.NameEn : "Category Missing",
                x.p.Brand != null ? x.p.Brand.NameAr : null,
                x.p.Brand != null ? x.p.Brand.NameEn : null,
                x.p.BrandId,
                x.p.Status.ToString(),
                x.p.Reviews.Any() ? x.p.Reviews.Average(r => r.Rating) : 0,
                x.p.Reviews.Count,
                x.p.TotalStock,
                x.p.ReorderLevel,
                x.p.SKU,
                x.p.Variants.Any(),
                x.p.HasTax,
                x.p.VatRate,
                x.p.CostPrice,
                x.p.CreatedAt,
                x.d != null ? x.d.Label : null
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
            .Include(x => x.Reviews).ThenInclude(r => r.Customer)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return null;
        var now = TimeHelper.GetEgyptTime();
        var d = await _db.ProductDiscounts
            .FirstOrDefaultAsync(x => x.ProductId == id && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now);

        return MapToDetail(p, d);
    }

    public async Task<ProductDetailDto?> GetProductBySlugAsync(string slug)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.Variants)
            .Include(x => x.Reviews).ThenInclude(r => r.Customer)
            .FirstOrDefaultAsync(x => x.Slug == slug);

        if (p == null) return null;
        var now = TimeHelper.GetEgyptTime();
        var d = await _db.ProductDiscounts
            .FirstOrDefaultAsync(x => x.ProductId == p.Id && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now);

        return MapToDetail(p, d);
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
            Status = ProductStatus.Active,
            Slug = GenerateSlug(dto.NameEn ?? dto.NameAr) + "-" + Guid.NewGuid().ToString().Substring(0, 4)
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
            ?? throw new InvalidOperationException($"Product {product.Id} not found after creation");
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
        product.UpdatedAt = TimeHelper.GetEgyptTime();

        // إعادة حساب إجمالي المخزون وتحديث الحالة للتأكد من الدقة
        await UpdateTotalStockAsync(id);

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
        product.UpdatedAt = TimeHelper.GetEgyptTime();
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
            // else: For simple products, we keep TotalStock as is (managed by LogMovement)
            
            // 💡 AUTO-STATUS: Sync status with stock levels
            // Update: Only auto-switch for Active/OutOfStock. Keep Hidden/Draft as is.
            if (product.Status == ProductStatus.Active && product.TotalStock <= 0)
            {
                product.Status = ProductStatus.OutOfStock;
            }
            else if (product.Status == ProductStatus.OutOfStock && product.TotalStock > 0)
            {
                product.Status = ProductStatus.Active;
            }

            product.UpdatedAt = TimeHelper.GetEgyptTime();
            await _db.SaveChangesAsync();
        }
    }

    public async Task SyncAllProductsStatusAndStockAsync()
    {
        var products = await _db.Products.Include(p => p.Variants).ToListAsync();
        foreach (var p in products)
        {
            // Sync Slug
            if (string.IsNullOrWhiteSpace(p.Slug))
            {
                p.Slug = GenerateSlug(p.NameEn);
            }

            int oldStock = p.TotalStock;
            if (p.Variants.Any())
            {
                p.TotalStock = p.Variants.Sum(v => v.StockQuantity);
            }

            // Sync Status
            if (p.Status == ProductStatus.Active && p.TotalStock <= 0)
                p.Status = ProductStatus.OutOfStock;
            else if (p.Status == ProductStatus.OutOfStock && p.TotalStock > 0)
                p.Status = ProductStatus.Active;
        }
        await _db.SaveChangesAsync();
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
        v.UpdatedAt = TimeHelper.GetEgyptTime();

        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == v.ProductId);
        if (product != null)
        {
            await UpdateTotalStockAsync(v.ProductId);
            product.UpdatedAt = TimeHelper.GetEgyptTime();
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
        if (product != null) { product.UpdatedAt = TimeHelper.GetEgyptTime(); await _db.SaveChangesAsync(); }

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
                p.Id, p.NameAr, p.NameEn, p.Slug, p.Price, p.DiscountPrice ?? 0,
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
                p.CreatedAt,
                null
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
                p.Id, p.NameAr, p.NameEn, p.Slug, p.Price, p.DiscountPrice ?? 0,
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
                p.CreatedAt,
                null
            ))
            .ToListAsync();
    }

    private static ProductDetailDto MapToDetail(Product p, ProductDiscount? d = null)
    {
        decimal finalDiscountPrice = p.DiscountPrice ?? 0;
        string? activeLabel = null;

        if (d != null)
        {
            activeLabel = d.Label;
            finalDiscountPrice = d.DiscountType == DiscountType.Percentage 
                ? Math.Round(p.Price - (p.Price * d.DiscountValue / 100), 2)
                : Math.Round(p.Price - d.DiscountValue, 2);
        }

        return new ProductDetailDto(
            p.Id, p.NameAr, p.NameEn, p.Slug, p.DescriptionAr, p.DescriptionEn,
            p.Price, finalDiscountPrice, p.CostPrice, p.SKU,
            p.Brand != null ? p.Brand.NameAr : null,
            p.Brand != null ? p.Brand.NameEn : null,
            p.BrandId,
            p.Status.ToString(), p.IsFeatured,
            p.CategoryId, p.Category?.NameAr ?? "Category Missing", p.Category?.NameEn ?? "Category Missing",
            p.Variants?.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId)).ToList() ?? new List<ProductVariantDto>(),
            p.Images?.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.ImagePublicId, i.IsMain, i.SortOrder, i.ColorAr)).ToList() ?? new List<ProductImageDto>(),
            p.Reviews?.Any(r => r.IsApproved) == true ? p.Reviews.Where(r => r.IsApproved).Average(r => r.Rating) : 0,
            p.Reviews?.Count(r => r.IsApproved) ?? 0,
            p.TotalStock,
            p.ReorderLevel,
            p.HasTax,
            p.VatRate,
            p.CreatedAt,
            p.Reviews?.Where(r => r.IsApproved).OrderByDescending(r => r.CreatedAt).Select(r => new ReviewListItemDto(r.Id, r.Customer?.FullName ?? "عميل", r.Rating, r.Comment, r.CreatedAt)).ToList(),
            activeLabel
        );
    }

    private string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString().Substring(0, 8);
        var s = name.ToLower().Trim();
        // Remove accents and special chars
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\u0600-\u06FF\s-]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "-").Trim('-');
        return s;
    }
}
