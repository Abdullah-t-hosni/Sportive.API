using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _db;
    public CategoryService(AppDbContext db) => _db = db;

    // ──────────────────────────────────────────────────────────
    // Flat list — كل الأقسام مع معلومات القسم الأب (بدون أبناء)
    // ──────────────────────────────────────────────────────────
    public async Task<List<CategoryDto>> GetAllAsync()
    {
        var cats = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.SizeGroup)
            .Include(c => c.Products)
            .OrderBy(c => c.ParentId).ThenBy(c => c.NameAr)
            .ToListAsync();

        return cats.Select(c => MapFlat(c)).ToList();
    }

    // ──────────────────────────────────────────────────────────
    // Tree — جذور فقط، مع بناء الشجرة كاملة في الذاكرة (أي عمق)
    // ──────────────────────────────────────────────────────────
    public async Task<List<CategoryDto>> GetTreeAsync()
    {
        var allCats = await _db.Categories
            .Include(c => c.SizeGroup)
            .Include(c => c.Products)
            .ToListAsync();

        var roots = allCats
            .Where(c => c.ParentId == null)
            .OrderBy(x => x.NameAr)
            .ToList();

        return roots.Select(r => BuildTreeRecursive(r, allCats)).ToList();
    }

    // ──────────────────────────────────────────────────────────
    // GetById — يعيد القسم مع شجرته الكاملة (أي عمق)
    // ──────────────────────────────────────────────────────────
    public async Task<CategoryDto?> GetByIdAsync(int id)
    {
        // نجيب الكل ونبني الشجرة في الذاكرة (أسرع من Include المتداخل)
        var allCats = await _db.Categories
            .Include(c => c.SizeGroup)
            .Include(c => c.Products)
            .ToListAsync();

        var target = allCats.FirstOrDefault(c => c.Id == id);
        if (target == null) return null;

        // نجيب الأب منفصلاً للاسم
        if (target.ParentId.HasValue)
            target.Parent = allCats.FirstOrDefault(c => c.Id == target.ParentId.Value);

        return BuildTreeRecursive(target, allCats);
    }

    // ──────────────────────────────────────────────────────────
    // Create
    // ──────────────────────────────────────────────────────────
    public async Task<CategoryDto> CreateAsync(CreateCategoryDto dto)
    {
        var type = dto.Type;
        if (dto.ParentId.HasValue)
        {
            var parent = await _db.Categories.FindAsync(dto.ParentId.Value)
                ?? throw new ArgumentException("القسم الرئيسي غير موجود");
            type = parent.Type; // Inherit from parent
        }

        var cat = new Category
        {
            NameAr        = dto.NameAr,
            NameEn        = dto.NameEn,
            DescriptionAr = dto.DescriptionAr,
            DescriptionEn = dto.DescriptionEn,
            ImageUrl      = dto.ImageUrl,
            Type          = type,
            ParentId      = dto.ParentId,
            SizeGroupId   = dto.SizeGroupId
        };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        return (await GetByIdAsync(cat.Id))!;
    }

    // ──────────────────────────────────────────────────────────
    // Update — يحمي من الحلقة الدائرية (circular reference)
    // ──────────────────────────────────────────────────────────
    public async Task<CategoryDto> UpdateAsync(int id, CreateCategoryDto dto)
    {
        var cat = await _db.Categories.FindAsync(id)
            ?? throw new KeyNotFoundException($"Category {id} not found");

        var type = dto.Type;
        if (dto.ParentId.HasValue)
        {
            if (dto.ParentId.Value == id)
                throw new ArgumentException("لا يمكن تعيين القسم كقسم فرعي من نفسه");

            // تحقق أن القسم الجديد ليس ابناً أو حفيداً من هذا القسم
            var allCats = await _db.Categories.ToListAsync();
            if (IsDescendant(id, dto.ParentId.Value, allCats))
                throw new ArgumentException("لا يمكن تعيين قسم فرعي كقسم رئيسي (سيسبب حلقة دائرية)");

            var parent = allCats.FirstOrDefault(x => x.Id == dto.ParentId.Value);
            if (parent != null) type = parent.Type;
        }

        cat.NameAr        = dto.NameAr;
        cat.NameEn        = dto.NameEn;
        cat.DescriptionAr = dto.DescriptionAr;
        cat.DescriptionEn = dto.DescriptionEn;
        cat.ImageUrl      = dto.ImageUrl;
        cat.Type          = type;
        cat.ParentId      = dto.ParentId;
        cat.SizeGroupId   = dto.SizeGroupId;
        cat.UpdatedAt     = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    // ──────────────────────────────────────────────────────────
    // Delete — يعيد تعيين جميع الأبناء المباشرين (أي عمق) للجد
    // ──────────────────────────────────────────────────────────
    public async Task DeleteAsync(int id)
    {
        var allCats = await _db.Categories.ToListAsync();
        var cat = allCats.FirstOrDefault(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Category {id} not found");

        // أعد تعيين الأبناء المباشرين فقط (يرثون الـ ParentId من القسم المحذوف)
        var directChildren = allCats.Where(c => c.ParentId == id).ToList();
        foreach (var child in directChildren)
        {
            child.ParentId  = cat.ParentId; // يصبح أخاً للمحذوف
            child.UpdatedAt = TimeHelper.GetEgyptTime();
        }
        await _db.SaveChangesAsync();

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
    }

    // ──────────────────────────────────────────────────────────
    // Helper — بناء الشجرة بشكل تكراري (أي عمق)
    // ──────────────────────────────────────────────────────────
    private static CategoryDto BuildTreeRecursive(Category current, List<Category> all)
    {
        var children = all
            .Where(c => c.ParentId == current.Id)
            .OrderBy(x => x.NameAr)
            .ToList();

        // نضع الأب لكل ابن لعرض اسمه
        foreach (var child in children)
            child.Parent = current;

        var subDtos = children.Select(c => BuildTreeRecursive(c, all)).ToList();
        
        // حساب عدد المنتجات بشكل تراكمي (القسم الحالي + الأقسام الفرعية)
        int directCount = current.Products?.Count ?? 0;
        int childrenCount = subDtos.Sum(s => s.ProductCount);
        int totalProductCount = directCount + childrenCount;

        return new CategoryDto(
            current.Id,
            current.NameAr,
            current.NameEn,
            current.DescriptionAr,
            current.DescriptionEn,
            current.ImageUrl,
            current.IsActive,
            current.Type,
            totalProductCount,
            current.CreatedAt,
            current.ParentId,
            current.SizeGroupId,
            current.SizeGroup?.Name,
            current.Parent?.NameAr,
            current.Parent?.NameEn,
            subDtos.Count > 0 ? subDtos : null
        );
    }

    // ──────────────────────────────────────────────────────────
    // Helper — القائمة المسطحة (بدون أبناء)
    // ──────────────────────────────────────────────────────────
    private static CategoryDto MapFlat(Category c)
    {
        return new CategoryDto(
            c.Id, c.NameAr, c.NameEn, c.DescriptionAr, c.DescriptionEn,
            c.ImageUrl, c.IsActive,
            c.Type,
            c.Products?.Count ?? 0, c.CreatedAt,
            c.ParentId,
            c.SizeGroupId,
            c.SizeGroup?.Name,
            c.Parent?.NameAr,
            c.Parent?.NameEn,
            null  // لا نُرجع الأبناء في القائمة المسطحة
        );
    }

    // ──────────────────────────────────────────────────────────
    // Helper — هل candidateId حفيد (بأي عمق) لـ ancestorId؟
    // ──────────────────────────────────────────────────────────
    private static bool IsDescendant(int ancestorId, int candidateId, List<Category> all)
    {
        var visited = new HashSet<int>();
        var current = all.FirstOrDefault(c => c.Id == candidateId);

        while (current?.ParentId != null)
        {
            if (current.ParentId.Value == ancestorId) return true;
            if (!visited.Add(current.ParentId.Value)) break; // منع الحلقات
            current = all.FirstOrDefault(c => c.Id == current.ParentId.Value);
        }

        return false;
    }
}
