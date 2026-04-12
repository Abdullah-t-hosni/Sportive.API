using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;
    public CustomerService(AppDbContext db) => _db = db;

    public async Task<PaginatedResult<CustomerDetailDto>> GetCustomersAsync(
        int page, int pageSize, string? search = null)
    {
        pageSize = AppConstants.ClampPrecacheSize(pageSize);
        var query = _db.Customers
            .AsNoTracking()
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(c =>
                c.FullName.ToLower().Contains(s) ||
                c.Email.ToLower().Contains(s)     ||
                (c.Phone != null && c.Phone.Contains(s)));
        }

        query = query.OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync();

        // 1. Fetch the page of customers (no balance sub-query here — avoids N+1)
        var rawCustomers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FullName, c.Email, c.Phone, c.AppUserId,
                c.MainAccountId, c.FixedDiscount, c.CreatedAt,
                OpeningBalance = c.MainAccount != null ? c.MainAccount.OpeningBalance : 0,
                OrderCount = c.Orders.Count,
                OrderTotal = c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                Addresses = c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList()
            })
            .ToListAsync();

        // 2. Fetch all balances for this page in a single query (one round-trip instead of N)
        var customerIds = rawCustomers.Select(c => c.Id).ToList();
        var accountIds  = rawCustomers.Where(c => c.MainAccountId.HasValue)
                                      .Select(c => c.MainAccountId!.Value).ToList();

        var balanceMap = await _db.JournalLines
            .Where(l =>
                (l.CustomerId != null && customerIds.Contains(l.CustomerId.Value) ||
                 accountIds.Contains(l.AccountId)) &&
                l.JournalEntry.Status == JournalEntryStatus.Posted)
            .GroupBy(l => l.CustomerId ?? 0)
            .Select(g => new { CustomerId = g.Key, Net = g.Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0 })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Net);

        // 3. Map to DTOs — balance = opening + journal net
        var items = rawCustomers.Select(c => new CustomerDetailDto(
            c.Id, c.FullName, c.Email, c.Phone,
            c.OrderCount, c.OrderTotal, c.CreatedAt,
            c.Addresses, c.AppUserId,
            c.OpeningBalance + (balanceMap.TryGetValue(c.Id, out var net) ? net : 0),
            c.MainAccountId, c.FixedDiscount
        )).ToList();

        return new PaginatedResult<CustomerDetailDto>(
            items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize));
    }

    public async Task<List<CustomerRfmDto>> GetRfmDataAsync() =>
        await _db.Customers
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CustomerRfmDto(
                c.Id,
                c.FullName,
                c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => (decimal?)o.TotalAmount) ?? 0,
                c.CreatedAt
            ))
            .ToListAsync();

    public async Task<CustomerDetailDto?> GetCustomerByIdAsync(int id) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .Where(c => c.Id == id)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FullName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                (c.MainAccount != null ? c.MainAccount.OpeningBalance : 0) + _db.JournalLines.Where(l => (l.CustomerId == c.Id || (c.MainAccountId != null && l.AccountId == c.MainAccountId)) && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0,
                c.MainAccountId,
                c.FixedDiscount
            ))
            .FirstOrDefaultAsync();

    public async Task<CustomerDetailDto?> GetCustomerByEmailAsync(string email) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .Where(c => c.Email == email)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FullName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                (c.MainAccount != null ? c.MainAccount.OpeningBalance : 0) + _db.JournalLines.Where(l => (l.CustomerId == c.Id || (c.MainAccountId != null && l.AccountId == c.MainAccountId)) && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0,
                c.MainAccountId,
                c.FixedDiscount
            ))
            .FirstOrDefaultAsync();

    public async Task<CustomerDetailDto?> GetCustomerByUserIdAsync(string userId) =>
        await _db.Customers
            .Include(c => c.Addresses)
            .Include(c => c.Orders)
            .Include(c => c.MainAccount)
            .Where(c => c.AppUserId == userId)
            .Select(c => new CustomerDetailDto(
                c.Id, c.FullName, c.Email, c.Phone,
                c.Orders.Count,
                c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalAmount),
                c.CreatedAt,
                c.Addresses.Select(a => new AddressDto(
                    a.Id, a.TitleAr, a.TitleEn, a.Street, a.City,
                    a.District, a.BuildingNo, a.Floor, a.ApartmentNo, a.IsDefault, a.Latitude, a.Longitude
                )).ToList(),
                c.AppUserId,
                (c.MainAccount != null ? c.MainAccount.OpeningBalance : 0) + _db.JournalLines.Where(l => (l.CustomerId == c.Id || (c.MainAccountId != null && l.AccountId == c.MainAccountId)) && l.JournalEntry.Status == JournalEntryStatus.Posted).Sum(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0,
                c.MainAccountId,
                c.FixedDiscount
            ))
            .FirstOrDefaultAsync();

    public async Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerDto dto)
    {
        // 1. Check for existing customer
        Customer? existing = await _db.Customers
            .FirstOrDefaultAsync(c => 
                (!string.IsNullOrEmpty(dto.Phone) && c.Phone == dto.Phone) || 
                (!string.IsNullOrEmpty(dto.Email) && c.Email.ToLower() == dto.Email.ToLower()));
        
        if (existing != null)
            throw new InvalidOperationException("هذا العميل (أو الهاتف) مسجل بالفعل في ملفات العملاء.");

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
                throw new InvalidOperationException($"عذراً، هذا الهاتف ({dto.Phone ?? "غير محدد"}) مرتبط بحساب مستخدم آخر في النظام (Staff). يرجى التغيير.");
        }

        // 4. Create New
        var customer = new Customer
        {
            FullName = dto.FullName,
            Email = generatedEmail ?? $"{Guid.NewGuid().ToString().Substring(0, 8)}@pos.com",
            Phone = dto.Phone,
            CreatedAt = TimeHelper.GetEgyptTime(),
            IsActive = true,
            FixedDiscount = dto.FixedDiscount
        };

        // ── Point to main 1103 (Receivables) Control Account ──
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1103");
        if (parent != null)
        {
            customer.MainAccountId = parent.Id;
        }

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        return (await GetCustomerByIdAsync(customer.Id))!;
    }

    public async Task<CustomerDetailDto> UpdateCustomerAsync(int id, UpdateCustomerDto dto)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException($"Customer {id} not found");

        customer.FullName = dto.FullName;
        customer.Email = dto.Email ?? customer.Email;
        customer.Phone = dto.Phone;
        customer.IsActive = dto.IsActive;
        customer.MainAccountId = dto.MainAccountId;
        customer.FixedDiscount = dto.FixedDiscount;
        customer.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return (await GetCustomerByIdAsync(customer.Id))!;
    }



    public async Task EnsureCustomerAccountAsync(int customerId)
    {
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null || customer.MainAccountId != null) return;

        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1103");
        if (parent != null)
        {
            customer.MainAccountId = parent.Id;
            await _db.SaveChangesAsync();
        }
    }

    public async Task SyncAllMissingAccountsAsync()
    {
        var customers = await _db.Customers.Where(c => c.MainAccountId == null).ToListAsync();
        if (!customers.Any()) return;

        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1103");
        if (parent == null) return;

        foreach (var c in customers)
        {
            c.MainAccountId = parent.Id;
        }

        await _db.SaveChangesAsync();
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

        _db.Addresses.Remove(address);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ToggleCustomerAsync(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null) return false;
        
        customer.IsActive = !customer.IsActive;
        customer.UpdatedAt = TimeHelper.GetEgyptTime();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCustomerAsync(int id)
    {
        var customer = await _db.Customers.Include(c => c.Orders).FirstOrDefaultAsync(c => c.Id == id);
        if (customer == null) return false;

        if (customer.Orders.Any(o => o.Status != OrderStatus.Cancelled))
            throw new InvalidOperationException("لا يمكن حذف عميل لديه طلبات نشطة بقاعدة البيانات.");
        
        if (!string.IsNullOrEmpty(customer.AppUserId))
        {
            // Only delete the AppUser if they hold no staff-level roles
            var staffRoleNames = new[] { "Admin", "Manager", "Cashier", "Accountant", "Staff" };
            var staffRoleIds = await _db.Roles
                .Where(r => r.Name != null && staffRoleNames.Contains(r.Name))
                .Select(r => r.Id)
                .ToListAsync();
            var isStaff = await _db.UserRoles
                .AnyAsync(ur => ur.UserId == customer.AppUserId && staffRoleIds.Contains(ur.RoleId));
            if (!isStaff)
            {
                var appUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == customer.AppUserId);
                if (appUser != null) _db.Users.Remove(appUser);
            }
        }
        
        _db.Customers.Remove(customer);
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
