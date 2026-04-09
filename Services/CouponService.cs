using Sportive.API.Utils;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.DTOs;

namespace Sportive.API.Services;

public interface ICouponService
{
    Task<(bool Valid, decimal Discount, string? Error)> ValidateAsync(string code, decimal orderTotal);
    Task<Coupon> CreateAsync(CreateCouponDto dto);
    Task<CouponListDto?> UpdateAsync(int id, CreateCouponDto dto);
    Task<List<CouponListDto>> GetAllAsync();
    Task<bool> DeactivateAsync(int id);
    Task<bool> ToggleAsync(int id);
    Task<bool> DeleteAsync(int id);
}

public class CouponService : ICouponService
{
    private readonly AppDbContext _db;
    public CouponService(AppDbContext db) => _db = db;

    public async Task<(bool Valid, decimal Discount, string? Error)> ValidateAsync(
        string code, decimal orderTotal)
    {
        var coupon = await _db.Coupons
            .FirstOrDefaultAsync(c => c.Code.ToUpper() == code.ToUpper() && c.IsActive);

        if (coupon == null)
            return (false, 0, "كوبون الخصم غير صحيح أو منتهي الصلاحية");

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt < TimeHelper.GetEgyptTime())
            return (false, 0, "انتهت صلاحية كوبون الخصم");

        if (coupon.MaxUsageCount.HasValue && coupon.CurrentUsageCount >= coupon.MaxUsageCount)
            return (false, 0, "تم استخدام هذا الكوبون بالحد الأقصى");

        if (coupon.MinOrderAmount.HasValue && orderTotal < coupon.MinOrderAmount)
            return (false, 0, $"الحد الأدنى للطلب {coupon.MinOrderAmount:N2} ج.م");

        decimal discount;
        if (coupon.DiscountType == DiscountType.Percentage)
        {
            discount = orderTotal * (coupon.DiscountValue / 100);
            if (coupon.MaxDiscountAmount.HasValue)
                discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
        }
        else
        {
            discount = coupon.DiscountValue;
        }

        return (true, Math.Round(discount, 2), null);
    }

    public async Task<Coupon> CreateAsync(CreateCouponDto dto)
    {
        var exists = await _db.Coupons.AnyAsync(c => c.Code.ToUpper() == dto.Code.ToUpper());
        if (exists) throw new InvalidOperationException("كود الكوبون موجود مسبقاً");

        var coupon = new Coupon
        {
            Code              = dto.Code.ToUpper(),
            DescriptionAr     = dto.DescriptionAr,
            DescriptionEn     = dto.DescriptionEn,
            DiscountType      = dto.DiscountType,
            DiscountValue     = dto.DiscountValue,
            MinOrderAmount    = dto.MinOrderAmount,
            MaxDiscountAmount = dto.MaxDiscountAmount,
            MaxUsageCount     = dto.MaxUsageCount,
            ExpiresAt         = dto.ExpiresAt,
            IsActive          = true
        };
        _db.Coupons.Add(coupon);
        await _db.SaveChangesAsync();
        return coupon;
    }

    public async Task<List<CouponListDto>> GetAllAsync() =>
        await _db.Coupons
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CouponListDto(
                c.Id, c.Code, c.DescriptionAr, c.DescriptionEn,
                c.DiscountType.ToString(), c.DiscountValue,
                c.MinOrderAmount, c.MaxDiscountAmount,
                c.MaxUsageCount, c.CurrentUsageCount,
                c.ExpiresAt, c.IsActive))
            .ToListAsync();

    public async Task<CouponListDto?> UpdateAsync(int id, CreateCouponDto dto)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon == null) return null;

        // Check code uniqueness if changed
        if (!coupon.Code.Equals(dto.Code, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.Coupons.AnyAsync(c => c.Code.ToUpper() == dto.Code.ToUpper() && c.Id != id);
            if (exists) throw new InvalidOperationException("كود الكوبون موجود مسبقاً");
        }

        coupon.Code              = dto.Code.ToUpper();
        coupon.DescriptionAr     = dto.DescriptionAr;
        coupon.DescriptionEn     = dto.DescriptionEn;
        coupon.DiscountType      = dto.DiscountType;
        coupon.DiscountValue     = dto.DiscountValue;
        coupon.MinOrderAmount    = dto.MinOrderAmount;
        coupon.MaxDiscountAmount = dto.MaxDiscountAmount;
        coupon.MaxUsageCount     = dto.MaxUsageCount;
        coupon.ExpiresAt         = dto.ExpiresAt;
        coupon.UpdatedAt         = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();

        return new CouponListDto(
            coupon.Id, coupon.Code, coupon.DescriptionAr, coupon.DescriptionEn,
            coupon.DiscountType.ToString(), coupon.DiscountValue,
            coupon.MinOrderAmount, coupon.MaxDiscountAmount,
            coupon.MaxUsageCount, coupon.CurrentUsageCount,
            coupon.ExpiresAt, coupon.IsActive);
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon == null) return false;
        coupon.IsActive  = false;
        coupon.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleAsync(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon == null) return false;
        coupon.IsActive  = !coupon.IsActive;
        coupon.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var coupon = await _db.Coupons.FindAsync(id);
        if (coupon == null) return false;
        _db.Coupons.Remove(coupon);
        await _db.SaveChangesAsync();
        return true;
    }
}
