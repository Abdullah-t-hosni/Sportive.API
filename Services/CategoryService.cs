using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;
    public CategoryService(AppDbContext db) => _db = db;

    public async Task<List<CategoryDto>> GetAllAsync() =>
        await _db.Categories
            .OrderBy(c => c.Type)
            .Select(c => new CategoryDto(
                c.Id, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn,
                c.Type.ToString(), c.ImageUrl, c.IsActive,
                c.Products.Count(p => !p.IsDeleted), c.CreatedAt))
            .ToListAsync();

    public async Task<CategoryDto?> GetByIdAsync(int id) =>
        await _db.Categories
            .Where(c => c.Id == id)
            .Select(c => new CategoryDto(
                c.Id, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn,
                c.Type.ToString(), c.ImageUrl, c.IsActive,
                c.Products.Count(p => !p.IsDeleted)))
            .FirstOrDefaultAsync();

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto dto)
    {
        var cat = new Category
        {
            NameAr        = dto.NameAr,
            NameEn        = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Type          = dto.Type,
            ImageUrl      = dto.ImageUrl
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(cat.Id))!;
    }

    public async Task<CategoryDto> UpdateAsync(int id, CreateCategoryDto dto)
    {
        var cat = await _db.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException($"Category {id} not found");

        cat.NameAr        = dto.NameAr;
        cat.NameEn        = dto.NameEn;
        cat.DescriptionAr = dto.DescriptionAr;
        cat.DescriptionEn = dto.DescriptionEn;
        cat.Type          = dto.Type;
        cat.ImageUrl      = dto.ImageUrl;
        cat.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public async Task DeleteAsync(int id)
    {
        var cat = await _db.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException($"Category {id} not found");
        cat.IsDeleted = true;
        cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
