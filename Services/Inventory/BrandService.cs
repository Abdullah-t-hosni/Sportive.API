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
    public BrandService(AppDbContext db) => _db = db;

    public async Task<List<BrandDto>> GetAllAsync()
    {
        var brands = await _db.Set<Brand>()
            .Include(b => b.Products)
            .Include(b => b.Parent)
            .OrderBy(b => b.NameAr)
            .ToListAsync();

        return brands.Select(MapToDto).ToList();
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
        return MapToDto(brand);
    }

    public async Task DeleteAsync(int id)
    {
        var allBrands = await _db.Set<Brand>().ToListAsync();
        var brand = allBrands.FirstOrDefault(b => b.Id == id)
            ?? throw new KeyNotFoundException($"Brand {id} not found");

        // أعد تعيين الأبناء المباشرين ليرثوا الجد (إذا وجد) لتجنب حدوث خطأ عند الحذف
        var directChildren = allBrands.Where(b => b.ParentId == id).ToList();
        foreach (var child in directChildren)
        {
            child.ParentId = brand.ParentId;
            child.UpdatedAt = TimeHelper.GetEgyptTime();
        }
        await _db.SaveChangesAsync();

        _db.Set<Brand>().Remove(brand);
        await _db.SaveChangesAsync();
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
