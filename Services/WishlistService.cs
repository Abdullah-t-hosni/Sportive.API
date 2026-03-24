using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IWishlistService
{
    Task<List<Product>> GetWishlistAsync(int customerId);
    Task<bool> AddToWishlistAsync(int customerId, int productId);
    Task<bool> RemoveFromWishlistAsync(int customerId, int productId);
    Task<bool> IsInWishlistAsync(int customerId, int productId);
}

public class WishlistService : IWishlistService
{
    private readonly AppDbContext _db;
    public WishlistService(AppDbContext db) => _db = db;

    public async Task<List<Product>> GetWishlistAsync(int customerId)
    {
        return await _db.WishlistItems
            .Include(w => w.Product!).ThenInclude(p => p!.Images)
            .Where(w => w.CustomerId == customerId)
            .Select(w => w.Product!)
            .ToListAsync();
    }

    public async Task<bool> AddToWishlistAsync(int customerId, int productId)
    {
        var item = await _db.WishlistItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId);

        if (item == null)
        {
            _db.WishlistItems.Add(new WishlistItem { CustomerId = customerId, ProductId = productId });
            return await _db.SaveChangesAsync() > 0;
        }

        if (item.IsDeleted)
        {
            item.IsDeleted = false;
            item.UpdatedAt = DateTime.UtcNow;
            return await _db.SaveChangesAsync() > 0;
        }

        return true;
    }

    public async Task<bool> RemoveFromWishlistAsync(int customerId, int productId)
    {
        var item = await _db.WishlistItems.FirstOrDefaultAsync(w => w.CustomerId == customerId && w.ProductId == productId);
        if (item == null) return false;

        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        return await _db.SaveChangesAsync() > 0;
    }

    public async Task<bool> IsInWishlistAsync(int customerId, int productId)
    {
        return await _db.WishlistItems.AnyAsync(w => w.CustomerId == customerId && w.ProductId == productId);
    }
}
