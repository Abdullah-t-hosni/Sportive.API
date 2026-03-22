using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

// ─────────────────────────────────────────────
// CATEGORY SERVICE
// ─────────────────────────────────────────────
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
                c.Products.Count(p => !p.IsDeleted)))
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

// ─────────────────────────────────────────────
// CUSTOMER SERVICE
// ─────────────────────────────────────────────
public class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;
    public CustomerService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<CustomerDetailDto>> GetCustomersAsync(
        int page, int pageSize, string? search = null)
    {
        var query = _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(s) ||
                c.LastName.ToLower().Contains(s)  ||
                c.Email.ToLower().Contains(s)     ||
                (c.Phone != null && c.Phone.Contains(s)));
        }

        query = query.OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault
                )).ToList()
            ))
            .ToListAsync();

        return new PaginatedResult<CustomerDetailDto>(
            items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<CustomerDetailDto?> GetCustomerByIdAsync(int id) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault
                )).ToList()
            ))
            .FirstOrDefaultAsync();

    public async Task<CustomerDetailDto?> GetCustomerByEmailAsync(string email) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Where(c => c.Email == email)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault
                )).ToList()
            ))
            .FirstOrDefaultAsync();

    public async Task<int> EnsureCustomerProfileAsync(AppUser user)
    {
        var existing = await _db.Customers.FirstOrDefaultAsync(c => c.AppUserId == user.Id);
        if (existing != null)
            return existing.Id;

        var byEmail = await _db.Customers.FirstOrDefaultAsync(c => c.Email == user.Email);
        if (byEmail != null)
        {
            if (!string.IsNullOrEmpty(byEmail.AppUserId) && byEmail.AppUserId != user.Id)
                throw new InvalidOperationException("This email is already linked to another account.");

            byEmail.AppUserId = user.Id;
            byEmail.FirstName = user.FirstName;
            byEmail.LastName  = user.LastName;
            if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
                byEmail.Phone = user.PhoneNumber;
            byEmail.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return byEmail.Id;
        }

        var customer = new Customer
        {
            FirstName = user.FirstName,
            LastName  = user.LastName,
            Email     = user.Email!,
            Phone     = user.PhoneNumber,
            AppUserId = user.Id
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer.Id;
    }

    public async Task<CustomerDetailDto?> GetCustomerByAppUserIdAsync(string appUserId) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Where(c => c.AppUserId == appUserId)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault
                )).ToList()
            ))
            .FirstOrDefaultAsync();

    public async Task<AddressDto> AddAddressAsync(int customerId, CreateAddressDto dto)
    {
        // If first address → make it default
        var isFirst = !await _db.Addresses.AnyAsync(a => a.CustomerId == customerId);

        var address = new Address
        {
            CustomerId     = customerId,
            TitleAr        = dto.TitleAr,
            TitleEn        = dto.TitleEn,
            Street         = dto.Street,
            City           = dto.City,
            District       = dto.District,
            BuildingNo     = dto.BuildingNo,
            Floor          = dto.Floor,
            ApartmentNo    = dto.ApartmentNo,
            AdditionalInfo = dto.AdditionalInfo,
            Latitude       = dto.Latitude,
            Longitude      = dto.Longitude,
            IsDefault      = isFirst
        };

        _db.Addresses.Add(address);
        await _db.SaveChangesAsync();

        return new AddressDto(address.Id, address.TitleAr, address.TitleEn,
            address.Street, address.City, address.District,
            address.BuildingNo, address.Floor, address.ApartmentNo, address.IsDefault);
    }

    public async Task DeleteAddressAsync(int customerId, int addressId)
    {
        var address = await _db.Addresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId)
            ?? throw new KeyNotFoundException("Address not found");

        address.IsDeleted = true;
        address.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task SetDefaultAddressAsync(int customerId, int addressId)
    {
        var addresses = await _db.Addresses
            .Where(a => a.CustomerId == customerId)
            .ToListAsync();

        foreach (var a in addresses)
            a.IsDefault = (a.Id == addressId);

        await _db.SaveChangesAsync();
    }
}
