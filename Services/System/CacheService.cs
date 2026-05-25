using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Sportive.API.Services;

// ─── Cache Key Constants ───────────────────────────────────
public static class CacheKeys
{
    public const string FeaturedProducts   = "featured_products";
    public const string DashboardStats     = "dashboard_stats";
    public const string AllCategories      = "all_categories";
    public const string TopProducts        = "top_products";
    public const string OrderStatusStats   = "order_status_stats";

    public static string Product(int id)   => $"product_{id}";
    public static string Category(int id)  => $"category_{id}";
    public static string CustomerCart(int id) => $"cart_{id}";
}

// ─── Cache Service Interface ───────────────────────────────
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task RemoveAsync(string key);
    Task RemoveByPrefixAsync(string prefix);
}

// ─── Memory Cache Implementation (default) ─────────────────
public class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryCacheService> _logger;
    private readonly HashSet<string> _keys = new();

    private static readonly MemoryCacheEntryOptions DefaultOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };

    public MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        _cache.TryGetValue(key, out T? value);
        if (value != null)
            _logger.LogDebug("Cache HIT: {Key}", key);
        else
            _logger.LogDebug("Cache MISS: {Key}", key);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var options = expiry.HasValue
            ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry }
            : DefaultOptions;

        _cache.Set(key, value, options);
        _keys.Add(key);
        _logger.LogDebug("Cache SET: {Key} (expires: {Expiry})", key, expiry ?? TimeSpan.FromMinutes(10));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _cache.Remove(key);
        _keys.Remove(key);
        _logger.LogDebug("Cache REMOVED: {Key}", key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix)
    {
        var toRemove = _keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var key in toRemove)
        {
            _cache.Remove(key);
            _keys.Remove(key);
        }
        _logger.LogDebug("Cache REMOVED by prefix '{Prefix}': {Count} keys", prefix, toRemove.Count);
        return Task.CompletedTask;
    }
}

// ─── Redis Cache Implementation (production) ───────────────
public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;

    private static readonly DistributedCacheEntryOptions DefaultOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
        SlidingExpiration = TimeSpan.FromMinutes(5)
    };

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var json = await _cache.GetStringAsync(key);
            if (json == null)
            {
                _logger.LogDebug("Redis MISS: {Key}", key);
                return default;
            }
            _logger.LogDebug("Redis HIT: {Key}", key);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key {Key}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        try
        {
            var options = expiry.HasValue
                ? new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry }
                : DefaultOptions;

            var json = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, json, options);
            _logger.LogDebug("Redis SET: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try { await _cache.RemoveAsync(key); }
        catch (Exception ex) { _logger.LogWarning(ex, "Redis REMOVE failed for key {Key}", key); }
    }

    public Task RemoveByPrefixAsync(string prefix)
    {
        // Redis doesn't support prefix deletion natively without SCAN
        // For now log a warning — use Redis key patterns in production
        _logger.LogWarning("RemoveByPrefix '{Prefix}' not fully supported without SCAN in Redis", prefix);
        return Task.CompletedTask;
    }
}

// ─── Cache Extension: GetOrCreateAsync helper ──────────────
public static class CacheExtensions
{
    public static async Task<T> GetOrCreateAsync<T>(
        this ICacheService cache,
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiry = null)
    {
        var cached = await cache.GetAsync<T>(key);
        if (cached != null) return cached;

        var value = await factory();
        if (value != null)
            await cache.SetAsync(key, value, expiry);

        return value!;
    }
}
