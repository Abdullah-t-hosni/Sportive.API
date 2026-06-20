using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

/// <summary>
/// Single source of truth for all permission-checking logic.
/// All results are cached per-user (15 min TTL) and can be invalidated
/// immediately when roles or module permissions change.
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly AppDbContext  _db;
    private readonly ICacheService _cache;

    public PermissionService(AppDbContext db, ICacheService cache)
    {
        _db    = db;
        _cache = cache;
    }

    // ── Public API ────────────────────────────────────────────

    public async Task<bool> CanViewAsync(string userId, IEnumerable<string> moduleKeys)
    {
        var perms = await GetPermissionsAsync(userId);
        var keys  = moduleKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return perms.Any(p => keys.Contains(p.ModuleKey) && p.CanView);
    }

    public async Task<bool> CanEditAsync(string userId, IEnumerable<string> moduleKeys)
    {
        var perms = await GetPermissionsAsync(userId);
        var keys  = moduleKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return perms.Any(p => keys.Contains(p.ModuleKey) && p.CanEdit);
    }

    public async Task<bool> HasPosAccessAsync(string userId)
    {
        var perms = await GetPermissionsAsync(userId);
        return perms.Any(p => p.ModuleKey == ModuleKeys.Pos && p.CanView);
    }

    public async Task InvalidateCacheAsync(string userId)
    {
        await _cache.RemoveAsync(CacheKey(userId));
    }

    // ── Private helpers ───────────────────────────────────────

    private async Task<List<PermEntry>> GetPermissionsAsync(string userId)
    {
        return await _cache.GetOrCreateAsync(
            CacheKey(userId),
            async () =>
            {
                var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null) return new List<PermEntry>();

                if (!string.IsNullOrEmpty(user.PermissionsJson))
                {
                    var strings = System.Text.Json.JsonSerializer.Deserialize<List<string>>(user.PermissionsJson) ?? new List<string>();
                    var map = new Dictionary<string, PermEntry>(StringComparer.OrdinalIgnoreCase);

                    foreach (var s in strings)
                    {
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        
                        var parts = s.Split('.');
                        var moduleKey = parts[0];
                        
                        if (!map.TryGetValue(moduleKey, out var entry))
                        {
                            entry = new PermEntry(moduleKey, false, false);
                            map[moduleKey] = entry;
                        }

                        if (parts.Length == 1)
                        {
                            entry = entry with { CanView = true };
                        }
                        else if (parts.Length > 1 && parts[1].Equals("edit", StringComparison.OrdinalIgnoreCase))
                        {
                            entry = entry with { CanEdit = true };
                        }
                        
                        map[moduleKey] = entry;
                    }
                    return map.Values.ToList();
                }
                else
                {
                    // Fallback to old table for backward compatibility if PermissionsJson is not set
                    return await _db.UserModulePermissions
                        .AsNoTracking()
                        .Where(p => p.UserAccountID == userId)
                        .Select(p => new PermEntry(p.ModuleKey, p.CanView, p.CanEdit))
                        .ToListAsync();
                }
            },
            TimeSpan.FromMinutes(15));
    }

    private static string CacheKey(string userId) => $"UserPermissions_{userId}";

    // lightweight projection — avoids anonymous type limitations across methods
    private record PermEntry(string ModuleKey, bool CanView, bool CanEdit);
}
