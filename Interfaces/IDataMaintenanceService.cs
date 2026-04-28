using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IDataMaintenanceService
{
    Task<(bool Success, string Message)> WipeCustomersAsync(string? currentUserName);
    
    Task<(bool Success, string Message)> RequestFactoryResetOtpAsync(AppUser user);
    
    Task<(bool Success, string Message)> FactoryResetAsync(AppUser user, string password, string otp, string confirmation, string? currentUserName);
    
    Task<(bool Success, string Message, int Count)> FixTreeAsync(string? currentUserName);
    
    Task<(bool Success, string Message)> SyncAccountsAsync(string? currentUserName);
    
    Task<(bool Success, Dictionary<string, int>? Purged, string Message)> PurgeDeletedAsync(string? currentUserName);
    
    Task<(bool Success, string Message)> FixPosOrdersAsync();
    
    Task<(bool Success, string Message)> CleanupDuplicatesAsync();
    
    Task<(bool Success, string Message)> SyncOrderAccountingAsync();
    
    Task<(bool Success, string Message)> SyncPaymentAccountingAsync();
    
    Task<(bool Success, string Message, object? Details)> SyncPurchaseJournalEntriesAsync();
    
    Task<(bool Success, string Message)> SyncEntityIdsAsync();
    
    Task<(bool Success, string Message)> SyncSubAccountsAsync();
    
    Task<(bool Success, string Message)> SyncLedgerSourceAsync();
    
    Task<(bool Success, string Message)> FixUtcTimesAsync();
}
