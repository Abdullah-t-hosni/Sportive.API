using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Services;

public interface IAuditIntegrityService
{
    /// <summary>
    /// Traverses all AuditLogs in order, checking that each record's PreviousHash
    /// matches the Hash of the predecessor, and that the Hash matches the computed HMAC-SHA256 hash.
    /// Supports legacy unhashed logs at the start of the table.
    /// </summary>
    Task<bool> VerifyEntireChainAsync();
}

public class AuditIntegrityService : IAuditIntegrityService
{
    private readonly AppDbContext _db;
    private readonly string _auditSecret;

    public AuditIntegrityService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        var secret = config["Security:AuditSecret"];
        if (string.IsNullOrEmpty(secret) || secret == "${AUDIT_SECRET}")
        {
            secret = Environment.GetEnvironmentVariable("AUDIT_SECRET");
        }
        _auditSecret = secret ?? string.Empty;
    }

    public async Task<bool> VerifyEntireChainAsync()
    {
        if (string.IsNullOrEmpty(_auditSecret))
        {
            throw new InvalidOperationException("Security:AuditSecret is not configured.");
        }

        var logs = await _db.AuditLogs.OrderBy(x => x.Id).ToListAsync();
        
        bool chainStarted = false;
        string expectedPreviousHash = "GENESIS";

        foreach (var log in logs)
        {
            if (!chainStarted)
            {
                if (string.IsNullOrEmpty(log.Hash) && string.IsNullOrEmpty(log.PreviousHash))
                {
                    // Skip legacy records before chaining was introduced
                    continue;
                }
                // The chain begins with the first record having a non-null Hash or PreviousHash
                chainStarted = true;
            }

            // Once the chain has started, every subsequent record must be chained
            if (string.IsNullOrEmpty(log.Hash) || string.IsNullOrEmpty(log.PreviousHash))
            {
                return false;
            }

            if (log.PreviousHash != expectedPreviousHash)
            {
                return false;
            }

            var computedHash = ComputeAuditHash(log.Action, log.UserId, log.CreatedAt, expectedPreviousHash);
            if (log.Hash != computedHash)
            {
                return false;
            }

            expectedPreviousHash = log.Hash;
        }

        return true;
    }

    private string ComputeAuditHash(string action, string? userId, DateTime createdAt, string previousHash)
    {
        var payload = $"{action}{userId ?? ""}{createdAt:yyyy-MM-dd HH:mm:ss}{previousHash}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_auditSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
