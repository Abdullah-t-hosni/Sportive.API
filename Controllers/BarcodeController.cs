using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Cashier")]
public class BarcodeController : ControllerBase
{
    private readonly AppDbContext _db;
    public BarcodeController(AppDbContext db) => _db = db;

    [HttpGet("scan")]
    public async Task<IActionResult> Scan([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return BadRequest();

        var queryVal = q.Trim().ToLower();
        bool isInt = int.TryParse(queryVal, out int id);
        bool isDecimal = decimal.TryParse(queryVal, out decimal price);

        // البحث عن المنتج بالـ SKU أو الـ ID أو البحث في المتغيرات
        var product = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => 
                p.SKU.ToLower() == queryVal || 
                (isInt && p.Id == id) ||
                (isDecimal && (p.Price == price || p.DiscountPrice == price)) ||
                p.NameAr.ToLower().Contains(queryVal) ||
                // البحث في المتغيرات (مثلاً لو كان الكود هو SKU-Size-Color)
                p.Variants.Any(v => (p.SKU + "-" + v.Size + "-" + v.Color).ToLower() == queryVal)
            );

        if (product == null) return NotFound(new { message = $"لا يوجد منتج بالكود: {q}" });

        // التنسيق المطلوب للـ Frontend (POS)
        return Ok(new
        {
            id = product.Id,
            nameAr = product.NameAr,
            nameEn = product.NameEn,
            sku = product.SKU,
            price = product.Price,
            discountPrice = product.DiscountPrice,
            image = product.Images.FirstOrDefault(i => i.IsMain)?.ImageUrl ?? product.Images.FirstOrDefault()?.ImageUrl,
            totalStock = product.Variants.Sum(v => v.StockQuantity),
            variants = product.Variants.Select(v => new
            {
                id = v.Id,
                size = v.Size,
                color = v.Color,
                colorAr = v.ColorAr,
                stockQuantity = v.StockQuantity,
                finalPrice = (product.DiscountPrice ?? product.Price) + (v.PriceAdjustment ?? 0)
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
        var basePrice = product.DiscountPrice ?? product.Price;

        var activeVariants = product.Variants.ToList();
        
        if (!activeVariants.Any())
        {
            stickers.Add(new
            {
                code = product.SKU,
                productName = product.NameAr,
                price = basePrice,
                sku = product.SKU
            });
        }
        else
        {
            foreach (var v in activeVariants)
            {
                var variantSku = $"{product.SKU}-{v.Size ?? ""}-{v.Color ?? ""}".Trim('-').Replace("--", "-");
                // Fallback to product SKU if variant doesn't have a distinct one in the system layout, but since we generate Code128, a unique string is preferred, or just the product SKU if that's what's printed
                stickers.Add(new
                {
                    code = product.SKU, // Usually barcodes scan the base SKU or a specific Variant SKU.
                    productName = $"{product.NameAr} - {v.Size ?? ""} {v.ColorAr ?? v.Color ?? ""}".Trim(),
                    size = v.Size,
                    color = v.ColorAr ?? v.Color,
                    price = basePrice + (v.PriceAdjustment ?? 0),
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

        var stickers = invoice.Items.Where(i => i.Product != null).Select(item => new
        {
            code = item.Product!.SKU, 
            productName = item.ProductVariantId != null
                ? $"{item.Product.NameAr} - {item.ProductVariant!.Size ?? ""} {item.ProductVariant.ColorAr ?? item.ProductVariant.Color ?? ""}".Trim()
                : item.Product.NameAr,
            price = item.Product.DiscountPrice ?? item.Product.Price + (item.ProductVariant?.PriceAdjustment ?? 0),
            sku = item.Product.SKU,
            qty = item.Quantity
        }).ToList();

        return Ok(new { stickers });
    }
}
