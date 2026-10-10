using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Barcode)]
public class BarcodeController : ControllerBase
{
    private readonly AppDbContext _db;
    public BarcodeController(AppDbContext db) => _db = db;

    [HttpGet("scan")]
    public async Task<IActionResult> Scan(
        [FromQuery] string q, 
        [FromQuery] bool byPrice = false, 
        [FromQuery] int? warehouseId = null,
        [FromQuery] DiscountApplyTo? source = null)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest();

        var queryVal = q.Trim().ToLower();
        bool isInt = int.TryParse(queryVal, out int id);
        bool isDecimal = decimal.TryParse(queryVal, out decimal price);

        // البحث عن المنتج بناءً على نمط البحث المختار
        var product = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => 
                byPrice ? (isDecimal && (p.Price == price || p.DiscountPrice == price)) : (
                    p.SKU.ToLower() == queryVal || 
                    (isInt && p.Id == id) ||
                    (p.EgyptianProductCode != null && p.EgyptianProductCode.ToLower() == queryVal) ||
                    (p.SaudiProductCode != null && p.SaudiProductCode.ToLower() == queryVal) ||
                    p.Variants.Any(v => (p.SKU + "-" + v.Size + "-" + v.Color).ToLower() == queryVal || (isInt && v.Id == id)) ||
                    p.NameAr.ToLower().Contains(queryVal) ||
                    (p.NameEn != null && p.NameEn.ToLower().Contains(queryVal))
                )
            );

        if (product == null) return NotFound(new { message = $"لا يوجد منتج بالكود: {q}" });

        // تحديد المتغير الذي طابق البحث (إن وجد)
        var matchedVariant = product.Variants.FirstOrDefault(v => 
            (product.SKU + "-" + v.Size + "-" + v.Color).ToLower() == queryVal || (isInt && v.Id == id)
        );

        var warehouseStocks = new Dictionary<int, int>();
        if (warehouseId.HasValue)
        {
            var variantIds = product.Variants.Select(v => v.Id).ToList();
            warehouseStocks = await _db.ProductWarehouseStocks
                .AsNoTracking()
                .Where(w => w.WarehouseId == warehouseId.Value && variantIds.Contains(w.ProductVariantId))
                .ToDictionaryAsync(w => w.ProductVariantId, w => w.Quantity);
        }

        int totalStock = warehouseId.HasValue 
            ? warehouseStocks.Values.Sum() 
            : product.Variants.Sum(v => v.StockQuantity);

        bool isStoreSource = source == DiscountApplyTo.Store;
        decimal basePrice = (isStoreSource && product.OnlinePrice.HasValue && product.OnlinePrice > 0)
            ? product.OnlinePrice.Value
            : product.Price;
        decimal? discountPrice = isStoreSource
            ? (product.OnlineDiscountPrice.HasValue && product.OnlineDiscountPrice > 0 ? product.OnlineDiscountPrice : product.DiscountPrice)
            : product.DiscountPrice;

        // التنسيق المطلوب للـ Frontend (POS & Inventory Count)
        return Ok(new
        {
            id = product.Id,
            nameAr = product.NameAr,
            nameEn = product.NameEn,
            sku = product.SKU,
            price = basePrice,
            discountPrice = discountPrice,
            onlinePrice = product.OnlinePrice,
            onlineDiscountPrice = product.OnlineDiscountPrice,
            costPrice = product.CostPrice,
            image = product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? product.Images.FirstOrDefault()?.ImageUrl,
            totalStock = totalStock,
            matchedVariantId = matchedVariant?.Id,
            variants = product.Variants.Select(v =>
            {
                decimal adj = isStoreSource
                    ? (v.OnlinePriceAdjustment ?? v.PriceAdjustment ?? 0)
                    : (v.PriceAdjustment ?? 0);
                decimal pieceSellingPrice = ((discountPrice.HasValue && discountPrice > 0) ? discountPrice.Value : basePrice) + adj;
                return new
                {
                    id = v.Id,
                    size = v.Size,
                    color = v.Color,
                    colorAr = v.ColorAr,
                    priceAdjustment = v.PriceAdjustment ?? 0,
                    onlinePriceAdjustment = v.OnlinePriceAdjustment,
                    stockQuantity = warehouseId.HasValue ? (warehouseStocks.TryGetValue(v.Id, out var qty) ? qty : 0) : v.StockQuantity,
                    finalPrice = pieceSellingPrice
                };
            })
        });
    }

    [HttpGet("product/{id}")]
    public async Task<IActionResult> GetProductStickers(int id)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (product == null) return NotFound(new { message = "المنتج غير موجود" });

        var stickers = new List<object>();
        var originalBasePrice = product.Price;
        var basePrice = (product.DiscountPrice.HasValue && product.DiscountPrice.Value > 0) ? product.DiscountPrice.Value : product.Price;

        var originalOnlineBasePrice = (product.OnlinePrice.HasValue && product.OnlinePrice.Value > 0) ? product.OnlinePrice.Value : product.Price;
        var onlineBasePrice = (product.OnlineDiscountPrice.HasValue && product.OnlineDiscountPrice.Value > 0)
            ? product.OnlineDiscountPrice.Value
            : originalOnlineBasePrice;

        var activeVariants = product.Variants.ToList();
        
        if (!activeVariants.Any())
        {
            stickers.Add(new
            {
                code = product.SKU,
                productName = product.NameAr,
                price = basePrice,
                originalPrice = originalBasePrice,
                onlinePrice = onlineBasePrice,
                originalOnlinePrice = originalOnlineBasePrice,
                sku = product.SKU
            });
        }
        else
        {
            foreach (var v in activeVariants)
            {
                stickers.Add(new
                {
                    code = product.SKU,
                    productName = $"{product.NameAr} - {v.Size ?? ""} {v.ColorAr ?? v.Color ?? ""}".Trim(),
                    size = v.Size,
                    color = v.ColorAr ?? v.Color,
                    price = basePrice + (v.PriceAdjustment ?? 0),
                    originalPrice = originalBasePrice + (v.PriceAdjustment ?? 0),
                    onlinePrice = onlineBasePrice + (v.OnlinePriceAdjustment ?? v.PriceAdjustment ?? 0),
                    originalOnlinePrice = originalOnlineBasePrice + (v.OnlinePriceAdjustment ?? v.PriceAdjustment ?? 0),
                    sku = product.SKU
                });
            }
        }

        return Ok(new { stickers });
    }

    [HttpGet("invoice/{id}")]
    public async Task<IActionResult> GetInvoiceStickers(int id)
    {
        var invoice = await _db.PurchaseInvoices
            .Include(i => i.Items)
            .ThenInclude(item => item.Product)
            .Include(i => i.Items)
            .ThenInclude(item => item.ProductVariant)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound(new { message = "الفاتورة غير موجودة" });

        var stickers = invoice.Items.Where(i => i.Product != null).OrderBy(i => i.Id).Select(item => {
            var origBase = item.Product!.Price;
            var itemBase = (item.Product.DiscountPrice.HasValue && item.Product.DiscountPrice.Value > 0) ? item.Product.DiscountPrice.Value : item.Product.Price;

            var origOnline = (item.Product.OnlinePrice.HasValue && item.Product.OnlinePrice.Value > 0) ? item.Product.OnlinePrice.Value : item.Product.Price;
            var itemOnline = (item.Product.OnlineDiscountPrice.HasValue && item.Product.OnlineDiscountPrice.Value > 0)
                ? item.Product.OnlineDiscountPrice.Value
                : origOnline;

            var adj = item.ProductVariant?.PriceAdjustment ?? 0;
            var onlineAdj = item.ProductVariant?.OnlinePriceAdjustment ?? adj;

            return new
            {
                code = item.Product.SKU, 
                productName = item.ProductVariantId != null
                    ? $"{item.Product.NameAr} - {item.ProductVariant!.Size ?? ""} {item.ProductVariant.ColorAr ?? item.ProductVariant.Color ?? ""}".Trim()
                    : item.Product.NameAr,
                size = item.ProductVariant != null ? item.ProductVariant.Size : null,
                color = item.ProductVariant != null ? (item.ProductVariant.ColorAr ?? item.ProductVariant.Color) : null,
                price = itemBase + adj,
                originalPrice = origBase + adj,
                onlinePrice = itemOnline + onlineAdj,
                originalOnlinePrice = origOnline + onlineAdj,
                sku = item.Product.SKU,
                qty = item.Quantity
            };
        }).ToList();

        return Ok(new { stickers });
    }
}

