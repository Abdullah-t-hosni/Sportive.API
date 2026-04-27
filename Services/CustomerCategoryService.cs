using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class CustomerCategoryService : ICustomerCategoryService
{
    private readonly AppDbContext _db;

    public CustomerCategoryService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<CustomerCategoryDto>> GetAllAsync()
    {
        return await _db.CustomerCategories
            .AsNoTracking()
            .Select(c => new CustomerCategoryDto(
                c.Id,
                c.NameAr,
                c.NameEn,
                c.Description,
                c.DefaultDiscount,
                c.MinimumSpending,
                c.IsActive,
                c.Customers.Count
            ))
            .ToListAsync();
    }

    public async Task<CustomerCategoryDto?> GetByIdAsync(int id)
    {
        var c = await _db.CustomerCategories
            .AsNoTracking()
            .Include(x => x.Customers)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (c == null) return null;

        return new CustomerCategoryDto(
            c.Id,
            c.NameAr,
            c.NameEn,
            c.Description,
            c.DefaultDiscount,
            c.MinimumSpending,
            c.IsActive,
            c.Customers.Count
        );
    }

    public async Task<CustomerCategoryDto> CreateAsync(CreateCustomerCategoryDto dto)
    {
        var category = new CustomerCategory
        {
            NameAr = dto.NameAr,
            NameEn = dto.NameEn,
            Description = dto.Description,
            DefaultDiscount = dto.DefaultDiscount,
            MinimumSpending = dto.MinimumSpending,
            IsActive = true
        };

        _db.CustomerCategories.Add(category);
        await _db.SaveChangesAsync();

        return (await GetByIdAsync(category.Id))!;
    }

    public async Task<CustomerCategoryDto> UpdateAsync(int id, UpdateCustomerCategoryDto dto)
    {
        var category = await _db.CustomerCategories.FindAsync(id);
        if (category == null) throw new KeyNotFoundException("Category not found");

        category.NameAr = dto.NameAr;
        category.NameEn = dto.NameEn;
        category.Description = dto.Description;
        category.DefaultDiscount = dto.DefaultDiscount;
        category.MinimumSpending = dto.MinimumSpending;
        category.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();
        return (await GetByIdAsync(id))!;
    }

    public async Task DeleteAsync(int id)
    {
        var category = await _db.CustomerCategories.FindAsync(id);
        if (category == null) return;

        // Check if there are customers linked
        var hasCustomers = await _db.Customers.AnyAsync(c => c.CategoryId == id);
        if (hasCustomers) throw new InvalidOperationException("Cannot delete category with linked customers");

        _db.CustomerCategories.Remove(category);
        await _db.SaveChangesAsync();
    }
}
