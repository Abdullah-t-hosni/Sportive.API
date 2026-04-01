using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;
    public CustomerService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<CustomerDetailDto>> GetCustomersAsync(
        int page, int pageSize, string? search = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
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
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                _db.JournalLines.Where(l => l.CustomerId == c.Id && !l.IsDeleted && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0
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
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                _db.JournalLines.Where(l => l.CustomerId == c.Id && !l.IsDeleted && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0
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
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                _db.JournalLines.Where(l => l.CustomerId == c.Id && !l.IsDeleted && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0
            ))
            .FirstOrDefaultAsync();

    public async Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerDto dto)
    {
        // 1. Check for existing customer (including deleted)
        Customer? existing = await _db.Customers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => 
                (!string.IsNullOrEmpty(dto.Phone) && c.Phone == dto.Phone) || 
                (!string.IsNullOrEmpty(dto.Email) && c.Email.ToLower() == dto.Email.ToLower()));
        
        // 2. If found (active or deleted), restore or fail
        if (existing != null)
        {
            if (!existing.IsDeleted)
                throw new Exception("هذا العميل (أو الهاتف) مسجل بالفعل في ملفات العملاء النشطة.");
            
            // Restore deleted
            existing.IsDeleted = false;
            if (!string.IsNullOrEmpty(dto.FirstName)) existing.FirstName = dto.FirstName;
            if (!string.IsNullOrEmpty(dto.LastName))  existing.LastName  = dto.LastName;
            existing.Phone = dto.Phone;
            if (!string.IsNullOrEmpty(dto.Email)) existing.Email = dto.Email;
            
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return (await GetCustomerByIdAsync(existing.Id))!;
        }

        // 3. Check for conflict in Identity Users (Even if not a customer)
        var generatedEmail = dto.Email;
        if (string.IsNullOrWhiteSpace(generatedEmail) && !string.IsNullOrWhiteSpace(dto.Phone))
            generatedEmail = $"{dto.Phone}@sportive.com";

        if (!string.IsNullOrEmpty(generatedEmail))
        {
            var userConflict = await _db.Users.FirstOrDefaultAsync(u => 
                (u.Email != null && !string.IsNullOrEmpty(generatedEmail) && u.Email.ToLower() == generatedEmail.ToLower()) || 
                (u.PhoneNumber != null && u.PhoneNumber == dto.Phone));
            
            if (userConflict != null)
                throw new Exception($"عذراً، هذا الهاتف ({dto.Phone ?? "غير محدد"}) مرتبط بحساب مستخدم آخر في النظام (Staff). يرجى التغيير.");
        }

        // 4. Create New
        var customer = new Customer
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName ?? "",
            Email = generatedEmail ?? $"{Guid.NewGuid().ToString().Substring(0, 8)}@pos.com",
            Phone = dto.Phone,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // ── Auto-create Customer Account in Chart of Accounts ──
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1103");
        if (parent != null)
        {
            var maxCode = await _db.Accounts.Where(a => a.ParentId == parent.Id).MaxAsync(a => (string?)a.Code);
            long nextCodeNum = 1;
            if (maxCode != null && maxCode.StartsWith("1103") && long.TryParse(maxCode.Substring(4), out var existingNum)) {
                nextCodeNum = existingNum + 1;
            } else {
                nextCodeNum = 1;
            }
            var newCode = $"1103{nextCodeNum:D4}";

            var account = new Account
            {
                Code = newCode,
                NameAr = $"عميل - {customer.FullName}",
                Type = AccountType.Asset,
                Nature = AccountNature.Debit,
                ParentId = parent.Id,
                Level = parent.Level + 1,
                IsLeaf = true,
                AllowPosting = true,
                IsSystem = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            customer.MainAccountId = account.Id;
            await _db.SaveChangesAsync();
        }

        return (await GetCustomerByIdAsync(customer.Id))!;
    }

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
            address.BuildingNo, address.Floor, address.ApartmentNo, address.IsDefault, 
            address.Latitude, address.Longitude);
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

    public async Task<bool> ToggleCustomerAsync(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null) return false;
        
        customer.IsActive = !customer.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null) return false;
        
        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(customer.AppUserId))
        {
            var appUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == customer.AppUserId);
            if (appUser != null)
            {
                appUser.IsActive = false; // "Soft delete" IdentityUser by deactivating it
            }
        }
        
        await _db.SaveChangesAsync();
        return true;
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
