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
    private readonly ICacheService _cache;
    private readonly ITranslator _t;

    public ProductService(AppDbContext db, INotificationService notifications, IInventoryService inventory, ICacheService cache, ITranslator t)
    {
        _db = db;
        _notifications = notifications;
        _inventory = inventory;
        _cache = cache;
        _t = t;
    }


    public async Task<PaginatedResult<ProductSummaryDto>> GetProductsAsync(ProductFilterDto filter)
    {
        var query = _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category!).ThenInclude(c => c.Parent!).ThenInclude(c => c.Parent!)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Unit)
            .AsQueryable();
        
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        if (store != null && store.HideOutOfStock && filter.OnlyPublic == true && !filter.Status.HasValue && string.IsNullOrWhiteSpace(filter.Search))
        {
            if (filter.WarehouseId.HasValue)
            {
                query = query.Where(p => _db.ProductWarehouseStocks
                    .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == filter.WarehouseId.Value)
                    .Sum(w => (int?)w.Quantity) > 0);
            }
            else
            {
                query = query.Where(p => p.TotalStock > 0);
            }
        }

        if (filter.Section.HasValue)
        {
            var section = filter.Section.Value;
            var rootCategoryIds = await _db.Categories
                .AsNoTracking()
                .Where(c => c.Type == section || 
                    (section == CategoryType.Men && (c.NameAr == "رجالي" || c.NameEn == "Men")) ||
                    (section == CategoryType.Women && (c.NameAr == "حريمي" || c.NameEn == "Women")) ||
                    (section == CategoryType.Kids && (c.NameAr == "أطفال" || c.NameEn == "Kids")) ||
                    (section == CategoryType.Equipment && (c.NameAr.Contains("أدوات") || c.NameEn == "Equipment")) ||
                    (section == CategoryType.SpecialSizes && (c.NameAr == "مقاسات خاصة" || c.NameEn == "Special Sizes")))
                .Select(c => c.Id)
                .ToListAsync();

            var allCategoryIds = new List<int>();
            foreach (var id in rootCategoryIds)
            {
                var descendants = await GetCategoryDescendants(id);
                allCategoryIds.AddRange(descendants);
            }
            
            var distinctIds = allCategoryIds.Distinct().ToList();
            query = query.Where(p => (p.CategoryId.HasValue && distinctIds.Contains(p.CategoryId.Value)) || _db.ProductSecondaryCategories.Any(sc => sc.ProductId == p.Id && distinctIds.Contains(sc.CategoryId)));
        }

        if (filter.CategoryId.HasValue)
        {
            var categoryIds = await GetCategoryDescendants(filter.CategoryId.Value);
            query = query.Where(p => (p.CategoryId.HasValue && categoryIds.Contains(p.CategoryId.Value)) || _db.ProductSecondaryCategories.Any(sc => sc.ProductId == p.Id && categoryIds.Contains(sc.CategoryId)));

            if (string.IsNullOrWhiteSpace(filter.SortBy) || filter.SortBy.Equals("random", StringComparison.OrdinalIgnoreCase) || filter.SortBy.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                var cat = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == filter.CategoryId.Value);
                if (cat != null && !string.IsNullOrWhiteSpace(cat.DefaultSortBy))
                {
                    filter.SortBy = cat.DefaultSortBy;
                    filter.SortDir = cat.DefaultSortDir ?? "desc";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim().ToLower();
            bool isDecimal = decimal.TryParse(s, out decimal searchPrice);
            
            if (filter.ByPrice == true)
            {
                // Strict price search
                query = query.Where(p => isDecimal && (p.Price == searchPrice || p.DiscountPrice == searchPrice));
            }
            else
            {
                bool isId = int.TryParse(s, out int searchId);
                query = query.Where(p =>
                    p.NameAr.ToLower().Contains(s) ||
                    p.NameEn.ToLower().Contains(s) ||
                    p.SKU.ToLower().Contains(s) ||
                    (p.EgyptianProductCode != null && p.EgyptianProductCode.ToLower().Contains(s)) ||
                    (p.SaudiProductCode != null && p.SaudiProductCode.ToLower().Contains(s)) ||
                    (isId && p.Id == searchId) ||
                    (p.Brand != null && (p.Brand.NameAr.ToLower().Contains(s) || p.Brand.NameEn.ToLower().Contains(s))) ||
                    p.Variants.Any(v => 
                        (v.Size != null && v.Size.ToLower().Contains(s)) || 
                        (v.Color != null && v.Color.ToLower().Contains(s)) || 
                        (v.ColorAr != null && v.ColorAr.ToLower().Contains(s)) ||
                        (p.SKU + "-" + v.Size + "-" + v.Color).ToLower().Contains(s)
                    )
                );
            }
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

        if (filter.OnlyInStock == true)
        {
            if (filter.WarehouseId.HasValue)
            {
                query = query.Where(p => _db.ProductWarehouseStocks
                    .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == filter.WarehouseId.Value)
                    .Sum(w => (int?)w.Quantity) > 0);
            }
            else
            {
                query = query.Where(p => p.TotalStock > 0);
            }
        }

        if (filter.OnlyPublic == true)
        {
            // Only show sellable Active products on the store front (and OutOfStock if not hidden in settings)
            if (store != null && store.HideOutOfStock)
            {
                query = query.Where(p => p.Status == ProductStatus.Active);
            }
            else
            {
                query = query.Where(p => p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock);
            }
        }

        if (filter.SupplierId.HasValue)
        {
            var supplierId = filter.SupplierId.Value;
            // 💡 STRATEGY: Filter products that have been purchased from this supplier at least once
            query = query.Where(p => _db.PurchaseInvoiceItems
                .Any(pi => pi.ProductId == p.Id && pi.Invoice.SupplierId == supplierId));
        }

        // Sorting
        if (filter.OnlyPublic == true)
        {
            query = filter.SortBy?.ToLower() switch
            {
                "price" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.Price).ThenByDescending(p => p.Id) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.Price).ThenByDescending(p => p.Id),
                "name" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.NameAr).ThenByDescending(p => p.Id) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.NameAr).ThenByDescending(p => p.Id),
                "id" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.Id) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.Id),
                "sku" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.SKU) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.SKU),
                "stock" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.TotalStock).ThenByDescending(p => p.Id) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.TotalStock).ThenByDescending(p => p.Id),
                "createdat" or "newest" => filter.SortDir == "asc" ? query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id) : query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
                _ => query.OrderBy(p => p.Category != null ? p.Category.SortOrder : 9999).ThenByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            };
        }
        else
        {
            query = filter.SortBy?.ToLower() switch
            {
                "price" => filter.SortDir == "asc" ? query.OrderBy(p => p.Price).ThenByDescending(p => p.Id) : query.OrderByDescending(p => p.Price).ThenByDescending(p => p.Id),
                "name" => filter.SortDir == "asc" ? query.OrderBy(p => p.NameAr).ThenByDescending(p => p.Id) : query.OrderByDescending(p => p.NameAr).ThenByDescending(p => p.Id),
                "id" => filter.SortDir == "asc" ? query.OrderBy(p => p.Id) : query.OrderByDescending(p => p.Id),
                "sku" => filter.SortDir == "asc" ? query.OrderBy(p => p.SKU) : query.OrderByDescending(p => p.SKU),
                "stock" => filter.SortDir == "asc" ? query.OrderBy(p => p.TotalStock).ThenByDescending(p => p.Id) : query.OrderByDescending(p => p.TotalStock).ThenByDescending(p => p.Id),
                "createdat" or "newest" => filter.SortDir == "asc" ? query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id) : query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
                _ => query.OrderByDescending(p => p.Id)
            };
        }

        var total = await query.CountAsync();

        var page = Math.Max(1, filter.Page);
        var rawProducts = await query
            .Skip((page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        var items = await MapToSummaryListAsync(rawProducts, filter.Source, filter.WarehouseId);

        return new PaginatedResult<ProductSummaryDto>(
            items, total, page, filter.PageSize,
            (int)Math.Ceiling((double)total / filter.PageSize)
        );
    }

    public async Task<ProductDetailDto?> GetProductByIdAsync(int id, DiscountApplyTo? source = null, int? warehouseId = null, bool rawPricing = false)
    {
        var p = await _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Category)
            .Include(x => x.SecondaryCategories).ThenInclude(sc => sc.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.Variants)
            .Include(x => x.Unit)
            // ❌ Do NOT include Reviews here - can cause OutOfMemoryException for products with many reviews
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Category)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Brand)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Images)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Variants)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Unit)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (p == null) return null;

        // ✅ Fetch only latest 10 approved reviews with minimal data — avoids OOM
        var recentReviews = await _db.Set<Review>()
            .AsNoTracking()
            .Where(r => r.ProductId == id && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .Select(r => new { r.Id, CustomerName = r.Customer != null ? r.Customer.FullName : null, r.Rating, r.Comment, r.CreatedAt })
            .ToListAsync();

        var reviewDtos = recentReviews
            .Select(r => new ReviewListItemDto(r.Id, r.CustomerName ?? _t.Get("Products.AnonymousReviewer"), r.Rating, r.Comment, r.CreatedAt))
            .ToList();

        if (warehouseId.HasValue)
        {
            var warehouseStocks = await _db.ProductWarehouseStocks
                .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == warehouseId.Value)
                .ToDictionaryAsync(w => w.ProductVariantId, w => w.Quantity);

            bool hasWarehouseRecords = await _db.ProductWarehouseStocks.AnyAsync(w => w.WarehouseId == warehouseId.Value);

            foreach (var v in p.Variants)
            {
                if (warehouseStocks.TryGetValue(v.Id, out var qty))
                {
                    v.StockQuantity = qty;
                }
                else if (hasWarehouseRecords)
                {
                    v.StockQuantity = 0;
                }
            }
            p.TotalStock = p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock;
        }
        else
        {
            p.TotalStock = p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock;
        }

        ProductDiscount? d = null;
        if (!rawPricing)
        {
            var now = TimeHelper.GetEgyptTime();
            d = await _db.ProductDiscounts
                .Where(x => (x.ProductId == id || 
                             (p.CategoryId != null && (x.CategoryId == p.CategoryId || x.CategoryId == p.Category!.ParentId || (p.Category!.Parent != null && x.CategoryId == p.Category!.Parent!.ParentId))) || 
                             (p.BrandId != null && x.BrandId == p.BrandId)) 
                        && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now)
                .Where(x => x.ApplyTo == DiscountApplyTo.All || (source.HasValue ? x.ApplyTo == source.Value : x.ApplyTo == DiscountApplyTo.Store))
                .OrderByDescending(x => x.ProductId != null ? 4 : (x.CategoryId != null ? 3 : (x.BrandId != null ? 2 : 1)))
                .FirstOrDefaultAsync();

            if (d != null && IsCategoryExcluded(d.ExcludedCategoryIds, p.CategoryId, new[] { p.Category?.ParentId, p.Category?.Parent?.ParentId }.Where(x => x.HasValue).Select(x => x!.Value)))
            {
                d = null;
            }
        }

        var (bundleSummaries, bundleIds, bundleConfigs, linkedSummary) = await LoadBundleSummariesAsync(p, source, warehouseId);

        return MapToDetail(p, d, linkedSummary, reviewDtos, source, rawPricing, bundleSummaries, bundleIds, bundleConfigs);
    }

    public async Task<ProductDetailDto?> GetProductBySlugAsync(string slug, DiscountApplyTo? source = null, int? warehouseId = null, bool rawPricing = false)
    {
        var p = await _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Category)
            .Include(x => x.SecondaryCategories).ThenInclude(sc => sc.Category)
            .Include(x => x.Brand)
            .Include(x => x.Images.OrderBy(i => i.SortOrder))
            .Include(x => x.Variants)
            .Include(x => x.Unit)
            // ❌ Do NOT include Reviews here - can cause OutOfMemoryException for products with many reviews
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Category)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Brand)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Images)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Variants)
            .Include(x => x.LinkedProduct).ThenInclude(lp => lp!.Unit)
            .FirstOrDefaultAsync(x => x.Slug == slug);

        if (p == null) return null;

        // ✅ Fetch only latest 10 approved reviews with minimal data — avoids OOM
        var recentReviews = await _db.Set<Review>()
            .AsNoTracking()
            .Where(r => r.ProductId == p.Id && r.IsApproved)
            .OrderByDescending(r => r.CreatedAt)
            .Take(10)
            .Select(r => new { r.Id, CustomerName = r.Customer != null ? r.Customer.FullName : null, r.Rating, r.Comment, r.CreatedAt })
            .ToListAsync();

        var reviewDtos = recentReviews
            .Select(r => new ReviewListItemDto(r.Id, r.CustomerName ?? _t.Get("Products.AnonymousReviewer"), r.Rating, r.Comment, r.CreatedAt))
            .ToList();

        if (warehouseId.HasValue)
        {
            var warehouseStocks = await _db.ProductWarehouseStocks
                .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == warehouseId.Value)
                .ToDictionaryAsync(w => w.ProductVariantId, w => w.Quantity);

            bool hasWarehouseRecords = await _db.ProductWarehouseStocks.AnyAsync(w => w.WarehouseId == warehouseId.Value);

            foreach (var v in p.Variants)
            {
                if (warehouseStocks.TryGetValue(v.Id, out var qty))
                {
                    v.StockQuantity = qty;
                }
                else if (hasWarehouseRecords)
                {
                    v.StockQuantity = 0;
                }
            }
            p.TotalStock = p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock;
        }
        else
        {
            p.TotalStock = p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock;
        }

        ProductDiscount? d = null;
        if (!rawPricing)
        {
            var now = TimeHelper.GetEgyptTime();
            d = await _db.ProductDiscounts
                .Where(x => (x.ProductId == p.Id || 
                             (p.CategoryId != null && (x.CategoryId == p.CategoryId || x.CategoryId == p.Category!.ParentId || (p.Category!.Parent != null && x.CategoryId == p.Category!.Parent!.ParentId))) || 
                             (p.BrandId != null && x.BrandId == p.BrandId)) 
                        && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now)
                .Where(x => x.ApplyTo == DiscountApplyTo.All || (source.HasValue ? x.ApplyTo == source.Value : x.ApplyTo == DiscountApplyTo.Store))
                .OrderByDescending(x => x.ProductId != null ? 4 : (x.CategoryId != null ? 3 : (x.BrandId != null ? 2 : 1)))
                .FirstOrDefaultAsync();

            if (d != null && IsCategoryExcluded(d.ExcludedCategoryIds, p.CategoryId, new[] { p.Category?.ParentId, p.Category?.Parent?.ParentId }.Where(x => x.HasValue).Select(x => x!.Value)))
            {
                d = null;
            }
        }

        var (bundleSummaries, bundleIds, bundleConfigs, linkedSummary) = await LoadBundleSummariesAsync(p, source, warehouseId);

        return MapToDetail(p, d, linkedSummary, reviewDtos, source, rawPricing, bundleSummaries, bundleIds, bundleConfigs);
    }

    public async Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto)
    {
        // التحقق من تكرار الكود
        if (await _db.Products.AnyAsync(p => p.SKU == dto.SKU))
            throw new ArgumentException(_t.Get("Products.SKUInUse", dto.SKU));

        if (!dto.Price.HasValue || dto.Price <= 0)
            throw new ArgumentException(_t.Get("Products.PriceRequired"));

        var (bundleJson, linkedId) = await ProcessAndValidateBundleItemsAsync(dto.BundleItems, dto.BundleProductIds, dto.LinkedProductId, 0);

        var product = new Product
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Price = dto.Price.Value,
            DiscountPrice = dto.DiscountPrice,
            OnlinePrice = dto.OnlinePrice,
            OnlineDiscountPrice = dto.OnlineDiscountPrice,
            CostPrice = dto.CostPrice,
            SKU = dto.SKU,
            BrandId = dto.BrandId,
            CategoryId = dto.CategoryId,
            IsFeatured = dto.IsFeatured,
            ReorderLevel = dto.ReorderLevel ?? 0,
            HasTax = dto.HasTax,
            IsTaxInclusive = dto.IsTaxInclusive,
            VatRate = dto.VatRate,
            UnitId = dto.UnitId,
            SizeGroupId = dto.SizeGroupId,
            SizeChartImageUrl = dto.SizeChartImageUrl,
            SizeChartJson = dto.SizeChartJson,
            Status = dto.Status ?? ProductStatus.Active,
            Slug = GenerateSlug(dto.NameEn ?? dto.NameAr) + "-" + Guid.NewGuid().ToString().Substring(0, 4),
            LinkedProductId = linkedId,
            BundleProductIds = bundleJson,
            BundleDiscountType = dto.BundleDiscountType,
            BundleDiscountValue = dto.BundleDiscountValue,
            BundleTitle = dto.BundleTitle
        };

        if (dto.SecondaryCategoryIds != null && dto.SecondaryCategoryIds.Any())
        {
            foreach (var catId in dto.SecondaryCategoryIds.Distinct())
            {
                product.SecondaryCategories.Add(new ProductSecondaryCategory { CategoryId = catId });
            }
        }

        if (dto.Variants != null && dto.Variants.Any())
        {
            foreach (var v in dto.Variants)
            {
                product.Variants.Add(new ProductVariant
                {
                    Size = v.Size,
                    Color = v.Color,
                    ColorAr = v.ColorAr,
                    StockQuantity = 0, // Start with 0, let LogMovement handle it
                    ReorderLevel = v.ReorderLevel ?? 0,
                    PriceAdjustment = v.PriceAdjustment,
                    IsActive = v.IsActive ?? true,
                    MaxOnlineStock = v.MaxOnlineStock
                });
            }
            product.TotalStock = 0;
        }
        else
        {
             // For simple products (like many Equipment/Tools), we can set initial stock directly
             product.TotalStock = 0;
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // 3. Log initial movements
        if (dto.Variants != null && dto.Variants.Any())
        {
            foreach (var vDto in dto.Variants)
            {
                if (vDto.StockQuantity.HasValue && vDto.StockQuantity.Value != 0)
                {
                    var variant = product.Variants.FirstOrDefault(v => v.Size == vDto.Size && v.Color == vDto.Color);
                    if (variant != null)
                    {
                        await _inventory.LogMovementAsync(
                            InventoryMovementType.OpeningBalance,
                            vDto.StockQuantity.Value,
                            product.Id,
                            variant.Id,
                            "INIT-PRODUCT",
                            _t.Get("Products.OpeningBalance"),
                            null
                        );
                    }
                }
            }
        }
        else if (dto.InitialStock.HasValue && dto.InitialStock.Value != 0)
        {
            // For simple products without variants (Tools/Equipment)
            await _inventory.LogMovementAsync(
                InventoryMovementType.OpeningBalance,
                dto.InitialStock.Value,
                product.Id,
                null,
                "INIT-PRODUCT",
                _t.Get("Products.OpeningBalance"),
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
            throw new ArgumentException(_t.Get("Products.SKUInUse", dto.SKU));

        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new KeyNotFoundException($"Product {id} not found");

        if (!dto.Price.HasValue || dto.Price <= 0)
            throw new ArgumentException(_t.Get("Products.PriceRequired"));

        product.NameAr = dto.NameAr;
        product.NameEn = dto.NameEn;
        product.DescriptionAr = dto.DescriptionAr;
        product.DescriptionEn = dto.DescriptionEn;
        product.Price = dto.Price.Value;
        product.DiscountPrice = (dto.DiscountPrice.HasValue && dto.DiscountPrice.Value < product.Price && dto.DiscountPrice.Value > 0) ? dto.DiscountPrice : null;
        product.OnlinePrice = dto.OnlinePrice;
        product.OnlineDiscountPrice = (dto.OnlineDiscountPrice.HasValue && product.OnlinePrice.HasValue && dto.OnlineDiscountPrice.Value < product.OnlinePrice.Value && dto.OnlineDiscountPrice.Value > 0) ? dto.OnlineDiscountPrice : null;
        product.CostPrice = dto.CostPrice;
        product.BrandId = dto.BrandId;
        product.SKU = dto.SKU;
        product.CategoryId = dto.CategoryId;
        product.IsFeatured = dto.IsFeatured;
        product.ReorderLevel = dto.ReorderLevel ?? 0;
        product.Status = dto.Status;
        product.HasTax = dto.HasTax;
        product.IsTaxInclusive = dto.IsTaxInclusive;
        product.VatRate = dto.VatRate;
        product.UnitId = dto.UnitId;
        product.SizeGroupId = dto.SizeGroupId;
        product.SizeChartImageUrl = dto.SizeChartImageUrl;
        product.SizeChartJson = dto.SizeChartJson;
        var (bundleJson, linkedId) = await ProcessAndValidateBundleItemsAsync(dto.BundleItems, dto.BundleProductIds, dto.LinkedProductId, id);
        product.BundleProductIds = bundleJson;
        product.LinkedProductId = linkedId;
        product.BundleDiscountType = dto.BundleDiscountType;
        product.BundleDiscountValue = dto.BundleDiscountValue;
        product.BundleTitle = dto.BundleTitle;
        product.UpdatedAt = TimeHelper.GetEgyptTime();

        // تحديث الأقسام الإضافية للمنتج
        var existingSec = await _db.ProductSecondaryCategories.Where(sc => sc.ProductId == id).ToListAsync();
        _db.ProductSecondaryCategories.RemoveRange(existingSec);
        if (dto.SecondaryCategoryIds != null && dto.SecondaryCategoryIds.Any())
        {
            foreach (var catId in dto.SecondaryCategoryIds.Distinct())
            {
                _db.ProductSecondaryCategories.Add(new ProductSecondaryCategory { ProductId = id, CategoryId = catId });
            }
        }

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
        if (diff == 0)
        {
            // StockQuantity is already correct, but ProductWarehouseStocks might be out of sync.
            // Safe to fix only when there's a single warehouse (multi-warehouse: each warehouse
            // holds its own portion — we can't blindly set all of them to the total quantity).
            var whStocksSync = await _db.ProductWarehouseStocks
                .Where(pws => pws.ProductVariantId == variantId)
                .ToListAsync();
            if (whStocksSync.Count == 1 && whStocksSync[0].Quantity != quantity)
            {
                whStocksSync[0].Quantity  = quantity;
                whStocksSync[0].UpdatedAt = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();
            }
            return true;
        }

        await _inventory.LogMovementAsync(
            InventoryMovementType.Adjustment,
            diff,
            variant.ProductId,
            variant.Id,
            "MANUAL-UPDATE",
            _t.Get("Products.ManualStockUpdate"),
            null,
            ignoreIdempotency: true
        );

        // After LogMovementAsync, ProductWarehouseStocks was incremented by `diff` on the
        // default warehouse. But if the variant only has ONE warehouse and that warehouse
        // was out of sync with StockQuantity before this call (e.g. a negative-stock sale
        // reduced StockQuantity while WarehouseStocks was already 0), the result will be
        // wrong. Force-set ONLY when there is exactly one warehouse so the two tables agree.
        // For multi-warehouse variants, LogMovementAsync already handled the correct warehouse.
        var whStocks = await _db.ProductWarehouseStocks
            .Where(pws => pws.ProductVariantId == variantId)
            .ToListAsync();
        if (whStocks.Count == 1)
        {
            whStocks[0].Quantity  = quantity;
            whStocks[0].UpdatedAt = TimeHelper.GetEgyptTime();
        }

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
            _t.Get("Products.ManualStockUpdate"),
            null,
            ignoreIdempotency: true
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

    public async Task<bool> UpdateSizeChartAsync(int productId, string? sizeChartJson, string? sizeChartImageUrl)
    {
        var product = await _db.Products.FindAsync(productId);
        if (product == null) return false;
        product.SizeChartJson = sizeChartJson;
        product.SizeChartImageUrl = sizeChartImageUrl;
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
        // ✅ Incremental sync: only load products that actually need fixing.
        // Criteria:
        //   1. Missing slug (NameEn exists but Slug is empty)
        //   2. Active product with zero/negative stock  → should be OutOfStock
        //   3. OutOfStock product with positive stock   → should be Active
        // This avoids loading the entire products table on every startup.
        var products = await _db.Products
            .Include(p => p.Variants)
            .Where(p =>
                string.IsNullOrEmpty(p.Slug) ||
                (p.Status == ProductStatus.Active    && p.TotalStock <= 0) ||
                (p.Status == ProductStatus.OutOfStock && p.TotalStock > 0))
            .ToListAsync();

        foreach (var p in products)
        {
            // Sync Slug
            if (string.IsNullOrWhiteSpace(p.Slug))
                p.Slug = GenerateSlug(p.NameEn);

            // Recalculate total stock from variants
            if (p.Variants.Any())
                p.TotalStock = p.Variants.Sum(v => v.StockQuantity);

            // Sync Status
            if (p.Status == ProductStatus.Active && p.TotalStock <= 0)
                p.Status = ProductStatus.OutOfStock;
            else if (p.Status == ProductStatus.OutOfStock && p.TotalStock > 0)
                p.Status = ProductStatus.Active;
        }

        if (products.Count > 0)
            await _db.SaveChangesAsync();
    }

    public async Task SyncAllProductRatingsAsync()
    {
        // ✅ Lightweight sync: calculate rating stats via GroupBy query without loading full review entity graphs
        var ratingStats = await _db.Set<Review>()
            .AsNoTracking()
            .Where(r => r.IsApproved)
            .GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Avg = g.Average(r => r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x);

        var products = await _db.Products.ToListAsync();
        foreach (var p in products)
        {
            if (ratingStats.TryGetValue(p.Id, out var stats))
            {
                p.AverageRating = stats.Avg;
                p.ReviewCount = stats.Count;
            }
            else
            {
                p.AverageRating = 0;
                p.ReviewCount = 0;
            }
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
            StockQuantity = 0, // Start with 0, let LogMovement handle it
            ReorderLevel = dto.ReorderLevel ?? 0,
            PriceAdjustment = dto.PriceAdjustment,
            OnlinePriceAdjustment = dto.OnlinePriceAdjustment
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
                _t.Get("Products.VariantOpeningBalance"),
                null
            );
            await _db.SaveChangesAsync();
        }

        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, v.StockQuantity);
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId, v.IsActive, v.MaxOnlineStock, v.OnlinePriceAdjustment);
    }

    public async Task<ProductVariantDto> UpdateVariantAsync(int variantId, CreateVariantDto dto)
    {
        var v = await _db.ProductVariants.FindAsync(variantId)
            ?? throw new KeyNotFoundException("Variant not found");
        v.Size = dto.Size;
        v.Color = dto.Color;
        v.ColorAr = dto.ColorAr;
        // Do NOT overwrite StockQuantity here. Inventory is managed via InventoryService/Adjustments.
        v.ReorderLevel = dto.ReorderLevel ?? 0;
        v.PriceAdjustment = dto.PriceAdjustment;
        v.OnlinePriceAdjustment = dto.OnlinePriceAdjustment;
        v.IsActive = dto.IsActive ?? v.IsActive;
        v.MaxOnlineStock = dto.MaxOnlineStock;
        v.UpdatedAt = TimeHelper.GetEgyptTime();

        var product = await _db.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.Id == v.ProductId);
        if (product != null)
        {
            await UpdateTotalStockAsync(v.ProductId);
            product.UpdatedAt = TimeHelper.GetEgyptTime();
        }

        await _db.SaveChangesAsync();
        await _notifications.BroadcastStockUpdateAsync(v.ProductId, v.Id, v.StockQuantity);
        
        return new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId, v.IsActive, v.MaxOnlineStock, v.OnlinePriceAdjustment);
    }

    public async Task<bool> DeleteVariantAsync(int variantId)
    {
        var v = await _db.ProductVariants.FindAsync(variantId);
        if (v == null) return false;

        var productId = v.ProductId;

        // 1. Remove warehouse stocks for this variant
        var stocks = await _db.ProductWarehouseStocks.Where(s => s.ProductVariantId == variantId).ToListAsync();
        if (stocks.Any()) _db.ProductWarehouseStocks.RemoveRange(stocks);

        // 2. Remove cart items referencing this variant
        var cartItems = await _db.CartItems.Where(ci => ci.ProductVariantId == variantId).ToListAsync();
        if (cartItems.Any()) _db.CartItems.RemoveRange(cartItems);

        // 3. Nullify historical references
        var orderItems = await _db.OrderItems.Where(oi => oi.ProductVariantId == variantId).ToListAsync();
        foreach (var oi in orderItems) oi.ProductVariantId = null;

        var movements = await _db.InventoryMovements.Where(m => m.ProductVariantId == variantId).ToListAsync();
        foreach (var m in movements) m.ProductVariantId = null;

        _db.ProductVariants.Remove(v);
        await _db.SaveChangesAsync();

        await UpdateTotalStockAsync(productId);
        var product = await _db.Products.FindAsync(productId);
        if (product != null) { product.UpdatedAt = TimeHelper.GetEgyptTime(); await _db.SaveChangesAsync(); }

        await _notifications.BroadcastStockUpdateAsync(productId, variantId, 0);
        return true;
    }
    public async Task<List<ProductSummaryDto>> GetFeaturedProductsAsync(int count = 8, int? warehouseId = null)
    {
        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        var query = _db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Unit)
            .Where(p => p.IsFeatured && (p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock));

        if (store != null && store.HideOutOfStock)
        {
            if (warehouseId.HasValue)
            {
                query = query.Where(p => _db.ProductWarehouseStocks
                    .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == warehouseId.Value)
                    .Sum(w => (int?)w.Quantity) > 0);
            }
            else
            {
                query = query.Where(p => p.TotalStock > 0);
            }
        }

        var rawProducts = await query
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
            .Take(count)
            .ToListAsync();

        return await MapToSummaryListAsync(rawProducts, DiscountApplyTo.Store, warehouseId);
    }

    public async Task<List<ProductSummaryDto>> GetRelatedProductsAsync(int productId, int count = 4, int? warehouseId = null)
    {
        var product = await _db.Products.AsNoTracking().Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null) return new List<ProductSummaryDto>();

        var store = await _db.StoreInfo.AsNoTracking().FirstOrDefaultAsync(s => s.StoreConfigId == 1);
        
        IQueryable<Product> query;
        
        var isShoesCategory = false;
        var shoeCategoryIds = new List<int>();
        var equipCategoryIds = new List<int>();
        
        var allCategories = await _db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.Type, c.NameAr, c.NameEn }).ToListAsync();
        
        foreach (var cat in allCategories)
        {
            var currentId = cat.Id;
            var visited = new HashSet<int>();
            while (true)
            {
                var c = allCategories.FirstOrDefault(x => x.Id == currentId);
                if (c == null || visited.Contains(currentId)) break;
                visited.Add(currentId);
                
                if (c.Type == CategoryType.Shoes || c.NameEn.ToLower().Contains("shoes") || c.NameAr.Contains("أحذية") || c.NameAr.Contains("حذاء"))
                {
                    shoeCategoryIds.Add(cat.Id);
                    break;
                }
                if (c.Type == CategoryType.Equipment || c.NameEn.ToLower().Contains("equipment") || c.NameAr.Contains("أدوات"))
                {
                    equipCategoryIds.Add(cat.Id);
                    break;
                }
                
                if (c.ParentId.HasValue)
                    currentId = c.ParentId.Value;
                else
                    break;
            }
        }
        
        if (product.CategoryId.HasValue && shoeCategoryIds.Contains(product.CategoryId.Value))
        {
            isShoesCategory = true;
        }

        if (isShoesCategory)
        {
            // "Complete the Look" (أكمل المظهر): suggest matching apparel and accessories (especially socks!)
            query = _db.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Include(p => p.Unit)
                .Where(p => p.Id != productId && (p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock))
                .Where(p => p.CategoryId == null || (!shoeCategoryIds.Contains(p.CategoryId.Value) && !equipCategoryIds.Contains(p.CategoryId.Value)));
        }
        else
        {
            query = _db.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Images)
                .Include(p => p.Variants)
                .Include(p => p.Unit)
                .Where(p => p.CategoryId == product.CategoryId && p.Id != productId && (p.Status == ProductStatus.Active || p.Status == ProductStatus.OutOfStock));
        }

        if (store != null && store.HideOutOfStock)
        {
            if (warehouseId.HasValue)
            {
                query = query.Where(p => _db.ProductWarehouseStocks
                    .Where(w => w.ProductVariant.ProductId == p.Id && w.WarehouseId == warehouseId.Value)
                    .Sum(w => (int?)w.Quantity) > 0);
            }
            else
            {
                query = query.Where(p => p.TotalStock > 0);
            }
        }

        var rawProducts = await query
            .Take(50) // Limit to avoid large database retrieval
            .ToListAsync();

        if (isShoesCategory)
        {
            rawProducts = rawProducts
                .OrderByDescending(p => p.NameAr.Contains("جوارب") || p.NameAr.Contains("شراب") || 
                                        p.NameEn.Contains("Socks") || p.NameEn.Contains("socks") ||
                                        (p.DescriptionAr != null && (p.DescriptionAr.Contains("جوارب") || p.DescriptionAr.Contains("شراب"))) ||
                                        (p.DescriptionEn != null && (p.DescriptionEn.Contains("Socks") || p.DescriptionEn.Contains("socks"))))
                .ThenByDescending(p => p.IsFeatured)
                .ThenBy(_ => Guid.NewGuid())
                .ToList();
        }
        else
        {
            rawProducts = rawProducts.OrderBy(_ => Guid.NewGuid()).ToList();
        }

        var mapProducts = rawProducts.Take(count).ToList();
        return await MapToSummaryListAsync(mapProducts, DiscountApplyTo.Store, warehouseId);
    }

    private async Task<ProductDiscount?> GetProductDiscountAsync(Product p)
    {
        var now = TimeHelper.GetEgyptTime();
        return await _db.ProductDiscounts
            .Where(x => (x.ProductId == p.Id || (p.CategoryId != null && x.CategoryId == p.CategoryId) || (p.BrandId != null && x.BrandId == p.BrandId) || (x.ProductId == null && x.CategoryId == null && x.BrandId == null)) 
                    && x.IsActive && x.ValidFrom <= now && x.ValidTo >= now)
            .OrderByDescending(d => d.ProductId != null ? 4 : (d.CategoryId != null ? 3 : (d.BrandId != null ? 2 : 1)))
            .FirstOrDefaultAsync();
    }

    private static bool IsCategoryExcluded(string? excludedCategoryIds, int? productCatId, IEnumerable<int>? ancestors)
    {
        if (string.IsNullOrWhiteSpace(excludedCategoryIds) || !productCatId.HasValue) return false;
        var ids = excludedCategoryIds.Split(new[] { ',', ';', ' ', '[', ']', '"' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => int.TryParse(s.Trim(), out int id) ? id : 0)
                                     .Where(id => id > 0)
                                     .ToHashSet();
        if (ids.Count == 0) return false;
        if (ids.Contains(productCatId.Value)) return true;
        if (ancestors != null && ancestors.Any(a => ids.Contains(a))) return true;
        return false;
    }

    public static List<BundleItemConfigDto> ParseBundleConfigs(string? json, int? fallbackLinkedId = null)
    {
        var result = new List<BundleItemConfigDto>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsedObjects = System.Text.Json.JsonSerializer.Deserialize<List<BundleItemConfigDto>>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsedObjects != null && parsedObjects.Any() && parsedObjects.First().ProductId > 0)
                {
                    result.AddRange(parsedObjects.Where(x => x.ProductId > 0).Select(x => new BundleItemConfigDto(x.ProductId, x.Quantity <= 0 ? 1 : x.Quantity)));
                }
            }
            catch { }

            if (result.Count == 0)
            {
                try
                {
                    var parsedInts = System.Text.Json.JsonSerializer.Deserialize<List<int>>(json);
                    if (parsedInts != null && parsedInts.Any())
                    {
                        result.AddRange(parsedInts.Where(id => id > 0).Distinct().Select(id => new BundleItemConfigDto(id, 1)));
                    }
                }
                catch { }
            }
        }

        if (result.Count == 0 && fallbackLinkedId.HasValue && fallbackLinkedId.Value > 0)
        {
            result.Add(new BundleItemConfigDto(fallbackLinkedId.Value, 1));
        }

        return result;
    }

    private async Task<(string? json, int? linkedId)> ProcessAndValidateBundleItemsAsync(
        List<BundleItemConfigDto>? items, List<int>? legacyIds, int? fallbackLinkedId, int currentProductId)
    {
        var rawConfigs = items != null && items.Any()
            ? items
            : (legacyIds != null && legacyIds.Any()
                ? legacyIds.Distinct().Select(id => new BundleItemConfigDto(id, 1)).ToList()
                : (fallbackLinkedId.HasValue ? new List<BundleItemConfigDto> { new BundleItemConfigDto(fallbackLinkedId.Value, 1) } : new List<BundleItemConfigDto>()));

        rawConfigs = rawConfigs.Where(c => c.ProductId != currentProductId && c.Quantity > 0).ToList();
        if (!rawConfigs.Any()) return (null, null);

        var pids = rawConfigs.Select(c => c.ProductId).Distinct().ToList();
        var bundleProducts = await _db.Products
            .Include(p => p.Variants)
            .Where(p => pids.Contains(p.Id))
            .ToListAsync();

        var validConfigs = new List<BundleItemConfigDto>();
        foreach (var c in rawConfigs)
        {
            var bp = bundleProducts.FirstOrDefault(p => p.Id == c.ProductId);
            if (bp == null) continue;

            int maxStock;
            if (c.ProductVariantId.HasValue && c.ProductVariantId.Value > 0)
            {
                var targetVariant = bp.Variants?.FirstOrDefault(v => v.Id == c.ProductVariantId.Value);
                if (targetVariant == null)
                    throw new ArgumentException($"المقاس أو اللون المحدد للمنتج '{bp.NameAr}' غير موجود.");
                maxStock = targetVariant.StockQuantity;
            }
            else
            {
                maxStock = bp.Variants != null && bp.Variants.Any(v => v.IsActive)
                    ? (bp.Variants.Where(v => v.IsActive).Select(v => v.StockQuantity).DefaultIfEmpty(0).Max())
                    : bp.TotalStock;
            }

            if (maxStock <= 0)
            {
                throw new ArgumentException($"المنتج '{bp.NameAr}' غير متوفر في المخزون حالياً ولا يمكن إضافته للبندل.");
            }

            if (c.Quantity > maxStock)
            {
                throw new ArgumentException($"الكمية المحددة للمنتج '{bp.NameAr}' في البندل ({c.Quantity}) تتجاوز المخزون المتاح ({maxStock}).");
            }

            validConfigs.Add(new BundleItemConfigDto(c.ProductId, c.Quantity, c.ProductVariantId));
        }

        if (!validConfigs.Any()) return (null, null);
        return (System.Text.Json.JsonSerializer.Serialize(validConfigs), validConfigs.First().ProductId);
    }

    private async Task<(List<ProductSummaryDto> bundleSummaries, List<int> bundleIds, List<BundleItemConfigDto> bundleConfigs, ProductSummaryDto? linkedSummary)> LoadBundleSummariesAsync(
        Product p, DiscountApplyTo? source = null, int? warehouseId = null)
    {
        var configs = ParseBundleConfigs(p.BundleProductIds, p.LinkedProductId);
        var bundleIds = configs.Select(c => c.ProductId).Where(id => id != p.Id).Distinct().ToList();

        var bundleSummaries = new List<ProductSummaryDto>();
        ProductSummaryDto? linkedSummary = null;

        if (bundleIds.Count > 0)
        {
            var bundleEntities = await _db.Products
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Category)
                .Include(x => x.Brand)
                .Include(x => x.Images.OrderBy(i => i.SortOrder))
                .Include(x => x.Variants)
                .Include(x => x.Unit)
                .Where(x => bundleIds.Contains(x.Id) && x.Id != p.Id)
                .ToListAsync();

            if (bundleEntities.Any())
            {
                var ordered = bundleIds
                    .Select(bid => bundleEntities.FirstOrDefault(e => e.Id == bid))
                    .Where(e => e != null)
                    .Cast<Product>()
                    .ToList();

                var rawSummaries = await MapToSummaryListAsync(ordered, source, warehouseId);
                var configMap = configs.ToDictionary(c => c.ProductId, c => c.Quantity);
                var variantMap = configs.Where(c => c.ProductVariantId.HasValue).ToDictionary(c => c.ProductId, c => c.ProductVariantId);

                bundleSummaries = rawSummaries
                    .Select(s => s with { 
                        BundleQuantity = configMap.GetValueOrDefault(s.Id, 1),
                        BundleVariantId = variantMap.GetValueOrDefault(s.Id)
                    })
                    .ToList();

                linkedSummary = bundleSummaries.FirstOrDefault();
            }
        }

        return (bundleSummaries, bundleIds, configs, linkedSummary);
    }

    private ProductDetailDto MapToDetail(
        Product p,
        ProductDiscount? d = null,
        ProductSummaryDto? linkedProduct = null,
        List<ReviewListItemDto>? reviewDtos = null,
        DiscountApplyTo? source = null,
        bool rawPricing = false,
        List<ProductSummaryDto>? bundleProducts = null,
        List<int>? bundleProductIds = null,
        List<BundleItemConfigDto>? bundleConfigs = null)
    {
        bool isStoreSource = source != DiscountApplyTo.POS;
        decimal effectiveBasePrice = (!rawPricing && isStoreSource && p.OnlinePrice.HasValue && p.OnlinePrice > 0)
            ? p.OnlinePrice.Value
            : p.Price;

        decimal effectiveDiscountPrice;
        if (rawPricing)
        {
            effectiveDiscountPrice = (p.DiscountPrice > 0) ? p.DiscountPrice.Value : p.Price;
        }
        else if (isStoreSource)
        {
            effectiveDiscountPrice = (p.OnlineDiscountPrice.HasValue && p.OnlineDiscountPrice > 0)
                ? p.OnlineDiscountPrice.Value
                : (p.OnlinePrice.HasValue && p.OnlinePrice > 0 ? p.OnlinePrice.Value : ((p.DiscountPrice > 0) ? p.DiscountPrice.Value : p.Price));
        }
        else
        {
            effectiveDiscountPrice = (p.DiscountPrice > 0) ? p.DiscountPrice.Value : p.Price;
        }

        decimal finalDiscountPrice = effectiveDiscountPrice;
        string? activeLabel = null;

        if (d != null && !rawPricing)
        {
            activeLabel = d.Label;
            finalDiscountPrice = d.DiscountType == DiscountType.Percentage 
                ? Math.Round(effectiveBasePrice - (effectiveBasePrice * d.DiscountValue / 100), 2)
                : Math.Round(effectiveBasePrice - d.DiscountValue, 2);
        }

        var reviewsList = reviewDtos ?? p.Reviews?.Where(r => r.IsApproved).OrderByDescending(r => r.CreatedAt).Select(r => new ReviewListItemDto(r.Id, r.Customer?.FullName ?? _t.Get("Products.AnonymousReviewer"), r.Rating, r.Comment, r.CreatedAt)).ToList();

        var configs = bundleConfigs ?? ParseBundleConfigs(p.BundleProductIds, p.LinkedProductId);
        var effectiveBundleIds = bundleProductIds ?? configs.Select(c => c.ProductId).ToList();

        return new ProductDetailDto(
            p.Id, p.NameAr, p.NameEn, p.Slug, p.DescriptionAr, p.DescriptionEn,
            effectiveBasePrice, finalDiscountPrice, p.CostPrice, p.SKU,
            p.Brand != null ? p.Brand.NameAr : null,
            p.Brand != null ? p.Brand.NameEn : null,
            p.BrandId,
            p.Status.ToString(), p.IsFeatured,
            p.CategoryId, p.Category?.NameAr ?? _t.Get("Products.CategoryMissing"), p.Category?.NameEn ?? _t.Get("Products.CategoryMissing"),
            p.Category?.Type.ToString(),
            p.Variants?.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.ColorAr, v.StockQuantity, v.ReorderLevel, v.PriceAdjustment ?? 0, v.ImageUrl, v.ImagePublicId, v.IsActive, v.MaxOnlineStock, v.OnlinePriceAdjustment)).ToList() ?? new List<ProductVariantDto>(),
            p.Images?.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.ImagePublicId, i.IsMain, i.SortOrder, i.ColorAr, i.CategoryId)).ToList() ?? new List<ProductImageDto>(),
            p.AverageRating,
            p.ReviewCount,
            p.TotalStock,
            p.ReorderLevel,
            p.HasTax,
            p.IsTaxInclusive,
            p.VatRate,
            p.UnitId,
            p.Unit?.NameAr,
            p.Unit?.NameEn,
            p.Unit?.Symbol,
            p.CreatedAt,
            reviewsList,
            activeLabel,
            p.SizeChartImageUrl,
            p.SizeChartJson,
            p.LinkedProductId,
            linkedProduct,
            BundleProductIds: effectiveBundleIds,
            BundleItems: configs,
            BundleDiscountType: p.BundleDiscountType,
            BundleDiscountValue: p.BundleDiscountValue,
            BundleTitle: p.BundleTitle,
            BundleProducts: bundleProducts,
            RawDiscountPrice: p.DiscountPrice,
            SecondaryCategoryIds: p.SecondaryCategories?.Select(sc => sc.CategoryId).ToList() ?? new List<int>(),
            SecondaryCategories: p.SecondaryCategories?.Where(sc => sc.Category != null).Select(sc => new CategoryDto(sc.Category.Id, sc.Category.NameAr, sc.Category.NameEn, sc.Category.DescriptionAr, sc.Category.DescriptionEn, sc.Category.ImageUrl, sc.Category.IsActive, sc.Category.Type, 0, sc.Category.CreatedAt)).ToList() ?? new List<CategoryDto>(),
            OnlinePrice: p.OnlinePrice,
            OnlineDiscountPrice: p.OnlineDiscountPrice
        );
    }

    private async Task<List<ProductSummaryDto>> MapToSummaryListAsync(List<Product> products, DiscountApplyTo? source = null, int? warehouseId = null)
    {
        if (products == null || !products.Any()) return new List<ProductSummaryDto>();

        var productIds = products.Select(x => x.Id).ToList();
        var categoryIds = products.Where(x => x.CategoryId.HasValue).Select(x => x.CategoryId!.Value).ToList();
        var brandIds = products.Where(x => x.BrandId.HasValue).Select(x => x.BrandId!.Value).ToList();

        // Load CategoryTree cache to resolve ancestors
        var allCategories = await _cache.GetAsync<List<(int Id, int? ParentId)>>("CategoryTree");
        if (allCategories == null)
        {
            var raw = await _db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync();
            allCategories = raw.Select(c => (c.Id, c.ParentId)).ToList();
            await _cache.SetAsync("CategoryTree", allCategories, TimeSpan.FromMinutes(10));
        }

        var ancestorCategoryIds = new List<int>();
        foreach (var catId in categoryIds)
        {
            var current = allCategories.FirstOrDefault(c => c.Id == catId);
            while (current.ParentId.HasValue)
            {
                ancestorCategoryIds.Add(current.ParentId.Value);
                current = allCategories.FirstOrDefault(c => c.Id == current.ParentId.Value);
            }
        }

        var now = TimeHelper.GetEgyptTime();
        var discounts = await _db.ProductDiscounts
            .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
            .Where(d => source.HasValue ? (d.ApplyTo == DiscountApplyTo.All || d.ApplyTo == source.Value) : true)
            .Where(d => 
                (d.ProductId != null && productIds.Contains(d.ProductId.Value)) ||
                (d.CategoryId != null && (categoryIds.Contains(d.CategoryId.Value) || ancestorCategoryIds.Contains(d.CategoryId.Value))) ||
                (d.BrandId != null && brandIds.Contains(d.BrandId.Value))
            )
            .ToListAsync();

        Dictionary<int, int> variantStocks = new Dictionary<int, int>();
        Dictionary<int, int> productStocks = new Dictionary<int, int>();
        bool hasWarehouseRecords = false;

        if (warehouseId.HasValue)
        {
            var stocks = await _db.ProductWarehouseStocks
                .Where(w => productIds.Contains(w.ProductVariant.ProductId) && w.WarehouseId == warehouseId.Value)
                .Select(w => new { w.ProductVariantId, w.ProductVariant.ProductId, w.Quantity })
                .ToListAsync();

            variantStocks = stocks.ToDictionary(s => s.ProductVariantId, s => s.Quantity);
            productStocks = stocks.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
            hasWarehouseRecords = await _db.ProductWarehouseStocks.AnyAsync(w => w.WarehouseId == warehouseId.Value);
        }
        else
        {
            var stocks = await _db.ProductWarehouseStocks
                .Where(w => productIds.Contains(w.ProductVariant.ProductId))
                .GroupBy(w => new { w.ProductVariantId, w.ProductVariant.ProductId })
                .Select(g => new { g.Key.ProductVariantId, g.Key.ProductId, Quantity = g.Sum(x => x.Quantity) })
                .ToListAsync();

            if (stocks.Count > 0)
            {
                variantStocks = stocks.ToDictionary(s => s.ProductVariantId, s => s.Quantity);
                productStocks = stocks.GroupBy(s => s.ProductId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
            }
        }

        var resultList = new List<ProductSummaryDto>();

        foreach (var p in products)
        {
            // Resolve parent hierarchy for this product
            var pCategoryAncestors = new List<int>();
            if (p.CategoryId.HasValue)
            {
                var current = allCategories.FirstOrDefault(c => c.Id == p.CategoryId.Value);
                while (current.ParentId.HasValue)
                {
                    pCategoryAncestors.Add(current.ParentId.Value);
                    current = allCategories.FirstOrDefault(c => c.Id == current.ParentId.Value);
                }
            }

            var applicableDiscounts = discounts
                .Where(d => !IsCategoryExcluded(d.ExcludedCategoryIds, p.CategoryId, pCategoryAncestors))
                .Where(d => 
                    (d.ProductId == p.Id) ||
                    (p.CategoryId.HasValue && d.CategoryId.HasValue && (d.CategoryId.Value == p.CategoryId.Value || pCategoryAncestors.Contains(d.CategoryId.Value))) ||
                    (p.BrandId.HasValue && d.BrandId.HasValue && d.BrandId.Value == p.BrandId.Value) ||
                    (d.ProductId == null && d.CategoryId == null && d.BrandId == null)
                )
                .OrderByDescending(d => d.ProductId != null ? 4 : (d.CategoryId != null ? 3 : (d.BrandId != null ? 2 : 1)))
                .ToList();

            var posDiscountObj = applicableDiscounts.FirstOrDefault(d => d.ApplyTo == DiscountApplyTo.All || d.ApplyTo == DiscountApplyTo.POS);
            var storeDiscountObj = applicableDiscounts.FirstOrDefault(d => d.ApplyTo == DiscountApplyTo.All || d.ApplyTo == DiscountApplyTo.Store);

            // 1. Calculate POS Pricing
            decimal posBasePrice = p.Price;
            decimal posFinalPrice = posBasePrice;
            if (posDiscountObj != null)
            {
                posFinalPrice = posDiscountObj.DiscountType == DiscountType.Percentage
                    ? Math.Round(posBasePrice - (posBasePrice * posDiscountObj.DiscountValue / 100), 2)
                    : Math.Round(posBasePrice - posDiscountObj.DiscountValue, 2);
            }
            else if (p.DiscountPrice.HasValue && p.DiscountPrice > 0)
            {
                posFinalPrice = p.DiscountPrice.Value;
            }

            // 2. Calculate Online Store Pricing
            decimal onlineBasePrice = (p.OnlinePrice.HasValue && p.OnlinePrice > 0) ? p.OnlinePrice.Value : p.Price;
            decimal onlineFinalPrice = onlineBasePrice;
            if (storeDiscountObj != null)
            {
                onlineFinalPrice = storeDiscountObj.DiscountType == DiscountType.Percentage
                    ? Math.Round(onlineBasePrice - (onlineBasePrice * storeDiscountObj.DiscountValue / 100), 2)
                    : Math.Round(onlineBasePrice - storeDiscountObj.DiscountValue, 2);
            }
            else if (p.OnlineDiscountPrice.HasValue && p.OnlineDiscountPrice > 0)
            {
                onlineFinalPrice = p.OnlineDiscountPrice.Value;
            }
            else if (posDiscountObj != null && posDiscountObj.ApplyTo == DiscountApplyTo.All)
            {
                onlineFinalPrice = posDiscountObj.DiscountType == DiscountType.Percentage
                    ? Math.Round(onlineBasePrice - (onlineBasePrice * posDiscountObj.DiscountValue / 100), 2)
                    : Math.Round(onlineBasePrice - posDiscountObj.DiscountValue, 2);
            }
            else if (p.DiscountPrice.HasValue && p.DiscountPrice > 0 && (!p.OnlinePrice.HasValue || p.OnlinePrice == 0))
            {
                onlineFinalPrice = p.DiscountPrice.Value;
            }

            decimal effectiveBasePrice;
            decimal finalPrice;
            if (source == DiscountApplyTo.POS)
            {
                effectiveBasePrice = posBasePrice;
                finalPrice = posFinalPrice;
            }
            else if (source == DiscountApplyTo.Store)
            {
                effectiveBasePrice = onlineBasePrice;
                finalPrice = onlineFinalPrice;
            }
            else
            {
                effectiveBasePrice = posBasePrice;
                finalPrice = posFinalPrice;
            }

            int totalStock = warehouseId.HasValue
                ? (productStocks.TryGetValue(p.Id, out var ps) ? ps : (hasWarehouseRecords ? 0 : (p.Variants != null && p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock)))
                : (p.Variants != null && p.Variants.Count > 0 ? p.Variants.Sum(v => v.StockQuantity) : p.TotalStock);

            resultList.Add(new ProductSummaryDto(
                p.Id,
                p.NameAr,
                p.NameEn,
                p.Slug,
                effectiveBasePrice,
                finalPrice,
                p.Images?.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? p.Images?.FirstOrDefault()?.ImageUrl,
                p.Category != null ? p.Category.NameAr : _t.Get("Products.CategoryMissing"),
                p.Category != null ? p.Category.NameEn : _t.Get("Products.CategoryMissing"),
                p.Brand != null ? p.Brand.NameAr : null,
                p.Brand != null ? p.Brand.NameEn : null,
                p.BrandId,
                p.Status.ToString(),
                p.AverageRating,
                p.ReviewCount,
                totalStock,
                p.ReorderLevel,
                p.SKU,
                p.Variants != null && p.Variants.Any(),
                p.Variants?.Select(v => {
                    int effectiveStock = v.StockQuantity;
                    if (warehouseId.HasValue)
                    {
                        if (variantStocks.TryGetValue(v.Id, out var qty))
                        {
                            effectiveStock = qty;
                        }
                        else if (hasWarehouseRecords)
                        {
                            effectiveStock = 0;
                        }
                    }
                    return new ProductVariantDto(
                        v.Id, 
                        v.Size, 
                        v.Color, 
                        v.ColorAr, 
                        effectiveStock, 
                        v.ReorderLevel, 
                        v.PriceAdjustment ?? 0, 
                        v.ImageUrl, 
                        v.ImagePublicId,
                        v.IsActive,
                        v.MaxOnlineStock,
                        v.OnlinePriceAdjustment
                    );
                }).ToList() ?? new List<ProductVariantDto>(),
                p.HasTax,
                p.IsTaxInclusive,
                p.VatRate,
                p.CostPrice,
                p.UnitId,
                p.Unit != null ? p.Unit.NameAr : null,
                p.Unit != null ? p.Unit.NameEn : null,
                p.Unit != null ? p.Unit.Symbol : null,
                p.CreatedAt,
                storeDiscountObj?.Label ?? posDiscountObj?.Label,
                p.LinkedProductId,
                p.EgyptianProductCode,
                p.SaudiProductCode,
                p.Images?.Select(i => new ProductImageDto(i.Id, i.ImageUrl, i.ImagePublicId, i.IsMain, i.SortOrder, i.ColorAr, i.CategoryId)).ToList() ?? new List<ProductImageDto>(),
                p.SecondaryCategories?.Select(sc => sc.CategoryId).ToList() ?? new List<int>(),
                p.OnlinePrice,
                onlineFinalPrice < onlineBasePrice ? onlineFinalPrice : p.OnlineDiscountPrice
            ));
        }

        return resultList;
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

    private async Task<List<int>> GetCategoryDescendants(int categoryId)
    {
        // Cache category tree for 10 min — it barely changes and is called on every product filter
        var allCategories = await _cache.GetAsync<List<(int Id, int? ParentId)>>("CategoryTree");
        if (allCategories == null)
        {
            var raw = await _db.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync();
            allCategories = raw.Select(c => (c.Id, c.ParentId)).ToList();
            await _cache.SetAsync("CategoryTree", allCategories, TimeSpan.FromMinutes(10));
        }

        var categoryIds = new List<int> { categoryId };
        var toProcess = new Queue<int>();
        toProcess.Enqueue(categoryId);

        while (toProcess.Count > 0)
        {
            var currentId = toProcess.Dequeue();
            var children = allCategories.Where(c => c.ParentId == currentId).Select(c => c.Id);
            foreach (var childId in children)
            {
                if (!categoryIds.Contains(childId))
                {
                    categoryIds.Add(childId);
                    toProcess.Enqueue(childId);
                }
            }
        }

        return categoryIds;
    }
}
