using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public class ProductDiscountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductDiscountsController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════
    // GET /api/productdiscounts — كل الخصومات
    // ══════════════════════════════════════════════════
    [HttpGet]
    [AllowAnonymous] // المتجر يحتاج يقرأ الخصومات الفعالة
    public async Task<IActionResult> GetAll(
        [FromQuery] int?  productId = null,
        [FromQuery] bool  activeOnly = false)
    {
        var now = TimeHelper.GetEgyptTime();
        var q = _db.ProductDiscounts
            .AsNoTracking()
            .Include(d => d.Product)
            .Include(d => d.Category)
            .Include(d => d.Brand)
            .AsQueryable();

        if (productId.HasValue)
            q = q.Where(d => d.ProductId == productId);

        if (activeOnly)
            q = q.Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now);

        var items = await q
            .OrderByDescending(d => d.ValidFrom)
            .Select(d => new {
                d.Id, d.ProductId,
                ProductNameAr = d.Product != null ? d.Product.NameAr : null,
                ProductNameEn = d.Product != null ? d.Product.NameEn : null,
                d.CategoryId,
                CategoryNameAr = d.Category != null ? d.Category.NameAr : null,
                CategoryNameEn = d.Category != null ? d.Category.NameEn : null,
                d.BrandId,
                BrandNameAr = d.Brand != null ? d.Brand.NameAr : null,
                BrandNameEn = d.Brand != null ? d.Brand.NameEn : null,
                d.DiscountType, d.DiscountValue, d.MinQty,
                d.ValidFrom, d.ValidTo, d.IsActive, d.Label, d.ApplyTo,
                IsCurrentlyActive = d.IsActive && d.ValidFrom <= now && d.ValidTo >= now
            })
            .ToListAsync();

        return Ok(items);
    }

    // ══════════════════════════════════════════════════
    // GET /api/productdiscounts/active — الخصومات الفعالة الآن
    // ══════════════════════════════════════════════════
    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive()
    {
        var now = TimeHelper.GetEgyptTime();
        var items = await _db.ProductDiscounts
            .AsNoTracking()
            .Where(d => d.IsActive && d.ValidFrom <= now && d.ValidTo >= now)
            .Include(d => d.Product)
            .Include(d => d.Category)
            .Include(d => d.Brand)
            .Select(d => new {
                d.Id, d.ProductId,
                ProductNameAr = d.Product != null ? d.Product.NameAr : null,
                ProductNameEn = d.Product != null ? d.Product.NameEn : null,
                d.CategoryId,
                CategoryNameAr = d.Category != null ? d.Category.NameAr : null,
                CategoryNameEn = d.Category != null ? d.Category.NameEn : null,
                d.BrandId,
                BrandNameAr = d.Brand != null ? d.Brand.NameAr : null,
                BrandNameEn = d.Brand != null ? d.Brand.NameEn : null,
                d.DiscountType, d.DiscountValue, d.MinQty,
                d.ValidFrom, d.ValidTo, d.Label, d.ApplyTo
            })
            .ToListAsync();

        return Ok(items);
    }

    // ══════════════════════════════════════════════════
    // POST /api/productdiscounts — إنشاء خصم
    // ══════════════════════════════════════════════════
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductDiscountDto dto)
    {
        if (dto.ProductId.HasValue && !await _db.Products.AnyAsync(p => p.Id == dto.ProductId))
            return BadRequest(new { message = "المنتج غير موجود" });

        if (dto.CategoryId.HasValue && !await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest(new { message = "القسم غير موجود" });

        if (dto.BrandId.HasValue && !await _db.Brands.AnyAsync(b => b.Id == dto.BrandId))
            return BadRequest(new { message = "الماركة غير موجودة" });

        if (!dto.ProductId.HasValue && !dto.CategoryId.HasValue && !dto.BrandId.HasValue)
            return BadRequest(new { message = "يجب اختيار منتج أو قسم أو ماركة" });

        if (dto.ValidFrom >= dto.ValidTo)
            return BadRequest(new { message = "تاريخ البداية يجب أن يكون قبل تاريخ النهاية" });

        if (dto.DiscountValue <= 0)
            return BadRequest(new { message = "قيمة الخصم يجب أن تكون موجبة" });

        var discount = new ProductDiscount
        {
            ProductId     = dto.ProductId,
            CategoryId    = dto.CategoryId,
            BrandId       = dto.BrandId,
            DiscountType  = dto.DiscountType,
            DiscountValue = dto.DiscountValue,
            MinQty        = dto.MinQty,
            ValidFrom     = dto.ValidFrom,
            ValidTo       = dto.ValidTo,
            IsActive      = dto.IsActive,
            Label         = dto.Label,
            ApplyTo       = dto.ApplyTo
        };

        _db.ProductDiscounts.Add(discount);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { productId = discount.ProductId }, discount);
    }

    // ══════════════════════════════════════════════════
    // PUT /api/productdiscounts/{id} — تعديل
    // ══════════════════════════════════════════════════
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ProductDiscountDto dto)
    {
        var discount = await _db.ProductDiscounts.FindAsync(id);
        if (discount == null) return NotFound();

        discount.ProductId     = dto.ProductId;
        discount.CategoryId    = dto.CategoryId;
        discount.BrandId       = dto.BrandId;
        discount.DiscountType  = dto.DiscountType;
        discount.DiscountValue = dto.DiscountValue;
        discount.MinQty        = dto.MinQty;
        discount.ValidFrom     = dto.ValidFrom;
        discount.ValidTo       = dto.ValidTo;
        discount.IsActive      = dto.IsActive;
        discount.Label         = dto.Label;
        discount.ApplyTo       = dto.ApplyTo;
        discount.UpdatedAt     = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(discount);
    }

    // ══════════════════════════════════════════════════
    // DELETE /api/productdiscounts/{id} — حذف
    // ══════════════════════════════════════════════════
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var discount = await _db.ProductDiscounts.FindAsync(id);
        if (discount == null) return NotFound();
        _db.ProductDiscounts.Remove(discount);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record ProductDiscountDto(
    int? ProductId,
    int? CategoryId,
    int? BrandId,
    DiscountType DiscountType,
    decimal DiscountValue,
    int MinQty,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IsActive,
    string? Label,
    DiscountApplyTo ApplyTo
);
