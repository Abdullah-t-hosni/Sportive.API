using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Utils;

public static class FilterHelper
{
    public static async Task<List<int>> GetCategoryFamilyIds(AppDbContext db, int? categoryId)
    {
        if (!categoryId.HasValue) return new List<int>();

        var allIds = new List<int> { categoryId.Value };
        
        // Level 2
        var subIds = await db.Categories
            .Where(c => c.ParentId == categoryId.Value)
            .Select(c => c.Id)
            .ToListAsync();
        
        if (subIds.Any())
        {
            allIds.AddRange(subIds);
            
            // Level 3
            var subSubIds = await db.Categories
                .Where(c => c.ParentId != null && subIds.Contains(c.ParentId.Value))
                .Select(c => c.Id)
                .ToListAsync();
            
            if (subSubIds.Any())
                allIds.AddRange(subSubIds);
        }

        return allIds;
    }

    public static async Task<List<int>> GetBrandFamilyIds(AppDbContext db, int? brandId)
    {
        if (!brandId.HasValue) return new List<int>();

        var allIds = new List<int> { brandId.Value };
        
        // Level 2
        var subIds = await db.Brands
            .Where(b => b.ParentId == brandId.Value)
            .Select(b => b.Id)
            .ToListAsync();
        
        if (subIds.Any())
        {
            allIds.AddRange(subIds);
            
            // Level 3 (Just in case brands have deeper nesting)
            var subSubIds = await db.Brands
                .Where(b => b.ParentId != null && subIds.Contains(b.ParentId.Value))
                .Select(b => b.Id)
                .ToListAsync();
            
            if (subSubIds.Any())
                allIds.AddRange(subSubIds);
        }

        return allIds;
    }

    public static IQueryable<Product> ApplyGlobalFilters(
        IQueryable<Product> query, 
        List<int>? categoryIds = null, 
        List<int>? brandIds = null, 
        string? color = null, 
        string? size = null)
    {
        if (categoryIds != null && categoryIds.Any())
            query = query.Where(p => p.CategoryId != null && categoryIds.Contains(p.CategoryId.Value));

        if (brandIds != null && brandIds.Any())
            query = query.Where(p => p.BrandId != null && brandIds.Contains(p.BrandId.Value));

        if (!string.IsNullOrEmpty(color))
            query = query.Where(p => p.Variants.Any(v => v.Color == color || v.ColorAr == color));

        if (!string.IsNullOrEmpty(size))
            query = query.Where(p => p.Variants.Any(v => v.Size == size));

        return query;
    }
}
