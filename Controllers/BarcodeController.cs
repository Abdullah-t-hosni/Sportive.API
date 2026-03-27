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

        // البحث عن المنتج بالـ SKU أو الـ ID أو السعر
        var product = await _db.Products
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => 
                p.SKU.ToLower() == queryVal || 
                (isInt && p.Id == id) ||
                (isDecimal && (p.Price == price || p.DiscountPrice == price)) ||
                p.NameAr.ToLower().Contains(queryVal) ||
                p.NameEn.ToLower().Contains(queryVal)
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
            variants = product.Variants.Where(v => !v.IsDeleted).Select(v => new
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
}
