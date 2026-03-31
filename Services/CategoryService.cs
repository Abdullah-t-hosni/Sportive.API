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

    // Flat list — includes parent info for each category
    public async Task<List<CategoryDto>> GetAllAsync()
    {
        var cats = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.Children.Where(ch => !ch.IsDeleted))
                .ThenInclude(ch => ch.Products.Where(p => !p.IsDeleted))
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.ParentId).ThenBy(c => c.Type).ThenBy(c => c.NameAr)
            .ToListAsync();

        return cats.Select(c => MapToDto(c, includeChildren: true)).ToList();
    }

    // Tree — only root categories, already containing nested children
    public async Task<List<CategoryDto>> GetTreeAsync()
    {
        var roots = await _db.Categories
            .Include(c => c.Children.Where(ch => !ch.IsDeleted))
                .ThenInclude(ch => ch.Children.Where(gch => !gch.IsDeleted))
                    .ThenInclude(gch => gch.Products.Where(p => !p.IsDeleted))
            .Include(c => c.Children.Where(ch => !ch.IsDeleted))
                .ThenInclude(ch => ch.Products.Where(p => !p.IsDeleted))
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .Where(c => c.ParentId == null && !c.IsDeleted)
            .OrderBy(c => c.Type).ThenBy(c => c.NameAr)
            .ToListAsync();

        return roots.Select(c => MapToDto(c, includeChildren: true)).ToList();
    }

    public async Task<CategoryDto?> GetByIdAsync(int id)
    {
        var c = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.Children.Where(ch => !ch.IsDeleted))
                .ThenInclude(ch => ch.Products.Where(p => !p.IsDeleted))
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        return c == null ? null : MapToDto(c, includeChildren: true);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto dto)
    {
        // Validate ParentId if given
        if (dto.ParentId.HasValue)
        {
            var parent = await _db.Categories.FindAsync(dto.ParentId.Value);
            if (parent == null || parent.IsDeleted)
                throw new ArgumentException("القسم الرئيسي غير موجود");
        }

        var cat = new Category
        {
            NameAr        = dto.NameAr,
            NameEn        = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            Type          = dto.Type,
            ImageUrl      = dto.ImageUrl,
            ParentId      = dto.ParentId,
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(cat.Id))!;
    }

    public async Task<CategoryDto> UpdateAsync(int id, CreateCategoryDto dto)
    {
        var cat = await _db.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException($"Category {id} not found");

        // Prevent setting a category as its own parent
        if (dto.ParentId.HasValue && dto.ParentId.Value == id)
            throw new ArgumentException("لا يمكن تعيين القسم كقسم فرعي من نفسه");

        cat.NameAr        = dto.NameAr;
        cat.NameEn        = dto.NameEn;
        cat.DescriptionAr = dto.DescriptionAr;
        cat.DescriptionEn = dto.DescriptionEn;
        cat.Type          = dto.Type;
        cat.ImageUrl      = dto.ImageUrl;
        cat.ParentId      = dto.ParentId;
        cat.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public async Task DeleteAsync(int id)
    {
        var cat = await _db.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Category {id} not found");

        // Move children to parent's parent (or make them root)
        foreach (var child in cat.Children)
        {
            child.ParentId  = cat.ParentId;
            child.UpdatedAt = DateTime.UtcNow;
        }

        cat.IsDeleted = true;
        cat.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    // ─── Private Helper ──────────────────────────────────────────────
    private static CategoryDto MapToDto(Category c, bool includeChildren)
    {
        var children = includeChildren && (c.Children != null && c.Children.Any())
            ? c.Children
                .Where(ch => !ch.IsDeleted)
                .OrderBy(ch => ch.NameAr)
                .Select(ch => MapToDto(ch, includeChildren: true))
                .ToList()
            : null;

        return new CategoryDto(
            c.Id, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn,
            c.Type.ToString(), c.ImageUrl, c.IsActive,
            c.Products?.Count(p => !p.IsDeleted) ?? 0, c.CreatedAt,
            c.ParentId,
            c.Parent?.NameAr,
            c.Parent?.NameEn,
            children
        );
    }
}
