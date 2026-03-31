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
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.ParentId).ThenBy(c => c.NameAr)
            .ToListAsync();

        return cats.Select(c => MapToDto(c, includeChildren: false)).ToList();
    }

    // Tree — only root categories, building the entire structure recursively in memory
    public async Task<List<CategoryDto>> GetTreeAsync()
    {
        // Fetch everything to build the tree in memory (efficient for usual category amounts)
        var allCats = await _db.Categories
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .Where(c => !c.IsDeleted)
            .ToListAsync();

        // Recursively build tree starting from roots
        var roots = allCats.Where(c => c.ParentId == null).OrderBy(x => x.NameAr).ToList();
        return roots.Select(r => BuildTreeRecursive(r, allCats)).ToList();
    }

    private CategoryDto BuildTreeRecursive(Category current, List<Category> all)
    {
        var children = all.Where(c => c.ParentId == current.Id).OrderBy(x => x.NameAr).ToList();
        var subDtos = children.Select(c => BuildTreeRecursive(c, all)).ToList();

        return new CategoryDto(
            current.Id,
            current.NameAr,
            current.NameEn,
            current.DescriptionAr,
            current.DescriptionEn,
            current.ImageUrl,
            current.IsActive,
            current.Products?.Count(p => !p.IsDeleted) ?? 0,
            current.CreatedAt,
            current.ParentId,
            current.Parent?.NameAr,
            current.Parent?.NameEn,
            subDtos.Any() ? subDtos : null
        );
    }

    public async Task<CategoryDto?> GetByIdAsync(int id)
    {
        var c = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.Children.Where(ch => !ch.IsDeleted))
            .Include(c => c.Products.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

        return c == null ? null : MapToDto(c, includeChildren: true);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto dto)
    {
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

        if (dto.ParentId.HasValue && dto.ParentId.Value == id)
            throw new ArgumentException("لا يمكن تعيين القسم كقسم فرعي من نفسه");

        cat.NameAr        = dto.NameAr;
        cat.NameEn        = dto.NameEn;
        cat.DescriptionAr = dto.DescriptionAr;
        cat.DescriptionEn = dto.DescriptionEn;
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

    private static CategoryDto MapToDto(Category c, bool includeChildren)
    {
        var subCategories = includeChildren && (c.Children != null && c.Children.Any())
            ? c.Children
                .Where(ch => !ch.IsDeleted)
                .OrderBy(ch => ch.NameAr)
                .Select(ch => MapToDto(ch, includeChildren: true))
                .ToList()
            : null;

        return new CategoryDto(
            c.Id, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn,
            c.ImageUrl, c.IsActive,
            c.Products?.Count(p => !p.IsDeleted) ?? 0, c.CreatedAt,
            c.ParentId,
            c.Parent?.NameAr,
            c.Parent?.NameEn,
            subCategories
        );
    }
}
