using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class BrandService : IBrandService
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;
    private const string CacheKeyAll = "Brands_All";

    public BrandService(AppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<List<BrandDto>> GetAllAsync()
    {
        var cached = await _cache.GetAsync<List<BrandDto>>(CacheKeyAll);
        if (cached != null) return cached;

        var brands = await _db.Set<Brand>()
            .AsNoTracking()
            .Include(b => b.Products)
            .Include(b => b.Parent)
            .OrderBy(b => b.NameAr)
            .ToListAsync();

        var result = brands.Select(MapToDto).ToList();
        await _cache.SetAsync(CacheKeyAll, result, TimeSpan.FromHours(1));
        return result;
    }

    public async Task<BrandDto?> GetByIdAsync(int id)
    {
        var b = await _db.Set<Brand>()
            .Include(b => b.Products)
            .Include(b => b.Parent)
            .FirstOrDefaultAsync(x => x.Id == id);

        return b == null ? null : MapToDto(b);
    }

    public async Task<BrandDto> CreateAsync(CreateBrandDto dto)
    {
        var brand = new Brand
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            ImageUrl = dto.ImageUrl,
            ParentId = dto.ParentId,
            IsActive = true
        };
        _db.Set<Brand>().Add(brand);
        await _db.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKeyAll);
        return MapToDto(brand);
    }

    public async Task<BrandDto> UpdateAsync(int id, UpdateBrandDto dto)
    {
        var brand = await _db.Set<Brand>().FindAsync(id)
            ?? throw new KeyNotFoundException($"Brand {id} not found");

        brand.NameAr = dto.NameAr;
        brand.NameEn = dto.NameEn;
        brand.DescriptionAr = dto.DescriptionAr;
        brand.DescriptionEn = dto.DescriptionEn;
        brand.ImageUrl = dto.ImageUrl;
        brand.IsActive = dto.IsActive;
        brand.ParentId = dto.ParentId;
        brand.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKeyAll);
        return MapToDto(brand);
    }

    public async Task DeleteAsync(int id)
    {
        // ✅ Optimized: fetch only the target brand and its direct children — not the full table
        var brand = await _db.Set<Brand>().FindAsync(id)
            ?? throw new KeyNotFoundException($"Brand {id} not found");

        // أعد تعيين الأبناء المباشرين ليرثوا الجد (إذا وجد) لتجنب حدوث خطأ عند الحذف
        var directChildren = await _db.Set<Brand>().Where(b => b.ParentId == id).ToListAsync();
        foreach (var child in directChildren)
        {
            child.ParentId = brand.ParentId;
            child.UpdatedAt = TimeHelper.GetEgyptTime();
        }
        await _db.SaveChangesAsync();

        _db.Set<Brand>().Remove(brand);
        await _db.SaveChangesAsync();
        await _cache.RemoveAsync(CacheKeyAll);
    }

    private static BrandDto MapToDto(Brand b) => new BrandDto(
        b.Id,
        b.NameAr,
        b.NameEn,
        b.DescriptionAr,
        b.DescriptionEn,
        b.ImageUrl,
        b.IsActive,
        b.ParentId,
        b.Parent?.NameAr,
        b.Parent?.NameEn,
        b.Products?.Count ?? 0,
        b.CreatedAt
    );
}
