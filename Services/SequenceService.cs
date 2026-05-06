using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

/// <summary>
/// Generates unique, sequential document numbers that are safe under concurrent requests.
/// Database-backed and safe across multiple server instances.
/// </summary>
public class SequenceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SequenceService> _logger;

    public SequenceService(IServiceScopeFactory scopeFactory, ILogger<SequenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [Obsolete("Use NextAsync(prefix) instead. This overload seeds the DbSequences table if it doesn't exist.")]
    public async Task<string> NextAsync(string prefix, Func<AppDbContext, string, Task<int>> maxSelector)
    {
        var now   = TimeHelper.GetEgyptTime();
        var stamp = $"{now.Year % 100:D2}{now.Month:D2}";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await db.DbSequences.AnyAsync(s => s.Prefix == prefix && s.Stamp == stamp);
        if (!exists)
        {
            try
            {
                var currentMax = await maxSelector(db, $"{prefix}-{stamp}-%");
                var seq = new DbSequence
                {
                    Prefix = prefix,
                    Stamp = stamp,
                    LastValue = currentMax,
                    LastUpdatedAt = now
                };
                db.DbSequences.Add(seq);
                await db.SaveChangesAsync();
                _logger.LogInformation("Seeded DbSequence for {Prefix}-{Stamp} with initial value {Max}", prefix, stamp, currentMax);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed DbSequence for {Prefix}. It will start from 1.", prefix);
            }
        }

        return await NextAsync(prefix);
    }

    /// <summary>
    /// Returns the next document number, e.g. "PO-2504-0042".
    /// Format: {prefix}-{YY}{MM}-{seq:D4}
    /// Database-backed and safe across multiple server instances.
    /// </summary>
    public async Task<string> NextAsync(string prefix)
    {
        var now   = TimeHelper.GetEgyptTime();
        var stamp = $"{now.Year % 100:D2}{now.Month:D2}";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // Use Serializable to ensure no two instances read the same value
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            try
            {
                var seq = await db.DbSequences
                    .FirstOrDefaultAsync(s => s.Prefix == prefix && s.Stamp == stamp);

                if (seq == null)
                {
                    seq = new DbSequence
                    {
                        Prefix = prefix,
                        Stamp = stamp,
                        LastValue = 1,
                        LastUpdatedAt = now
                    };
                    db.DbSequences.Add(seq);
                }
                else
                {
                    seq.LastValue++;
                    seq.LastUpdatedAt = now;
                }

                await db.SaveChangesAsync();
                await tx.CommitAsync();

                return $"{prefix}-{stamp}-{seq.LastValue:D4}";
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate") == true || ex.InnerException?.Message.Contains("unique") == true)
            {
                // Conflict during initial creation - retry will handle it automatically via strategy
                await tx.RollbackAsync();
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sequence generation failed for {Prefix}", prefix);
                await tx.RollbackAsync();
                throw;
            }
        });
    }
}
