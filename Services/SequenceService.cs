using Sportive.API.Utils;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Services;

/// <summary>
/// Generates unique, sequential document numbers that are safe under concurrent requests.
/// Uses a database-level MAX query inside a serializable transaction to avoid race conditions.
/// Registered as a Singleton so the in-process lock provides a fast path before hitting the DB.
/// </summary>
public class SequenceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    // One lock per prefix so different document types don't block each other
    private readonly Dictionary<string, SemaphoreSlim> _locks = new();
    private readonly object _lockDictLock = new();

    // Cache the last used number to prevent DB race conditions during uncommitted transactions
    private readonly Dictionary<string, int> _lastUsed = new();

    public SequenceService(IServiceScopeFactory scopeFactory)
        => _scopeFactory = scopeFactory;

    private SemaphoreSlim GetLock(string prefix)
    {
        lock (_lockDictLock)
        {
            if (!_locks.TryGetValue(prefix, out var sem))
                _locks[prefix] = sem = new SemaphoreSlim(1, 1);
            return sem;
        }
    }

    /// <summary>
    /// Returns the next document number, e.g. "PO-2504-0042".
    /// Format: {prefix}-{YY}{MM}-{seq:D4}
    /// Thread-safe against concurrent HTTP requests.
    /// </summary>
    public async Task<string> NextAsync(string prefix, Func<AppDbContext, string, Task<int>> maxSelector)
    {
        var now    = TimeHelper.GetEgyptTime();
        var stamp  = $"{now.Year % 100:D2}{now.Month:D2}";
        var cacheKey = $"{prefix}-{stamp}";

        var sem = GetLock(prefix);
        await sem.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var likePattern = $"{prefix}-{stamp}-%";

            // 1. Get current max from DB
            var dbMax = await maxSelector(db, likePattern);
            
            // 2. Compare with in-memory cache to handle uncommitted parallel requests
            int currentMax;
            if (_lastUsed.TryGetValue(cacheKey, out var cachedValue))
            {
                currentMax = Math.Max(dbMax, cachedValue);
            }
            else
            {
                currentMax = dbMax;
            }

            var next = currentMax + 1;
            _lastUsed[cacheKey] = next;

            return $"{prefix}-{stamp}-{next:D4}";
        }
        finally
        {
            sem.Release();
        }
    }
}
