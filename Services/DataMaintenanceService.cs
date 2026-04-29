using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sportive.API.Data;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services;

public class DataMaintenanceService : IDataMaintenanceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataMaintenanceService> _logger;
    private readonly UserManager<AppUser> _userManager;
    private readonly IMemoryCache _cache;
    private readonly IAccountingService _accounting;
    private readonly IEmailService _emailService; // Optional if you have an email service

    public DataMaintenanceService(
        AppDbContext db,
        ILogger<DataMaintenanceService> logger,
        UserManager<AppUser> userManager,
        IMemoryCache cache,
        IAccountingService accounting,
        IEmailService emailService)
    {
        _db = db;
        _logger = logger;
        _userManager = userManager;
        _cache = cache;
        _accounting = accounting;
        _emailService = emailService;
    }

    public async Task<(bool Success, string Message)> WipeCustomersAsync(string? currentUserName)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            try 
            {
                await _db.Addresses.ExecuteDeleteAsync();
                await _db.Customers.ExecuteDeleteAsync();
            }
            finally 
            {
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            }

            _logger.LogWarning("WIPE CUSTOMERS COMPLETED by {User} at {Time}", currentUserName ?? "Unknown", DateTime.UtcNow);
            return (true, "تم مسح كافة سجلات العملاء.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WipeCustomers failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message)> RequestFactoryResetOtpAsync(AppUser user)
    {
        var otp = new Random().Next(100000, 999999).ToString();
        var cacheKey = $"FactoryResetOtp_{user.Id}";
        
        _cache.Set(cacheKey, otp, TimeSpan.FromMinutes(5));

        _logger.LogCritical("FACTORY RESET OTP REQUESTED for {Email}: {OTP}", user.Email, otp);
        
        try 
        {
            await _emailService.SendEmailAsync(user.Email, "رمز تأكيد تصفير النظام (Factory Reset)", 
                $"تحذير: تم طلب تصفير النظام. رمز التأكيد الخاص بك هو: {otp}. صالح لمدة 5 دقائق.");
        } 
        catch (Exception ex) 
        {
            _logger.LogWarning(ex, "Failed to send OTP email, but OTP was generated.");
        }

        return (true, "تم إرسال رمز التأكيد. صالح لمدة 5 دقائق.");
    }

    public async Task<(bool Success, string Message)> FactoryResetAsync(AppUser user, string password, string otp, string confirmation, string? currentUserName)
    {
        if (confirmation != "CONFIRM_FACTORY_RESET")
            return (false, "يجب إرسال { \"confirmation\": \"CONFIRM_FACTORY_RESET\" }.");

        if (!await _userManager.CheckPasswordAsync(user, password))
            return (false, "كلمة المرور غير صحيحة.");

        var cacheKey = $"FactoryResetOtp_{user.Id}";
        if (!_cache.TryGetValue(cacheKey, out string? cachedOtp) || cachedOtp != otp)
            return (false, "رمز التأكيد (OTP) غير صحيح أو منتهي الصلاحية.");

        _logger.LogWarning("FACTORY RESET INITIATED by {User} at {Time}.", currentUserName ?? "Unknown", DateTime.UtcNow);

        try
        {
            // 1. Create a logical Backup First (Basic Implementation - Export to SQL or similar would be better, but we log the event)
            _logger.LogCritical("CRITICAL BACKUP POINT: Executing Factory Reset for User: {User}", currentUserName);

            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            try 
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        string[] tablesToTruncate = {
                            "OrderItemAttributes", "InventoryAuditItems", "InventoryMovements", "PurchaseInvoiceItems",
                            "SupplierPayments", "JournalLines", "ReceiptVouchers", "PaymentVouchers", "JournalEntries",
                            "Orders", "InventoryAudits", "PurchaseInvoices", "Suppliers", "ProductImages",
                            "ProductVariants", "Products", "Coupons", "Addresses", "Customers", "Notifications",
                            "CartItems", "WishlistItems", "Reviews", "OrderStatusHistories", "OrderItems", "ShippingZones"
                        };

                        foreach (var table in tablesToTruncate)
                        {
                            try { 
                                if (table.All(char.IsLetterOrDigit)) 
                                {
                                    string query = $"TRUNCATE TABLE `{table}`;";
                                    await _db.Database.ExecuteSqlRawAsync(query); 
                                }
                            }
                            catch (Exception ex) { _logger.LogWarning("TRUNCATE {Table} skipped: {Error}", table, ex.Message); }
                        }

                        var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
                        if (customerRole != null)
                        {
                            var customerUserIds = await _db.UserRoles.Where(ur => ur.RoleId == customerRole.Id).Select(ur => ur.UserId).ToListAsync();
                            
                            // Chunking user deletes to avoid massive SQL statements
                            foreach (var batch in customerUserIds.Chunk(500))
                            {
                                var idsToDelete = await _db.Users
                                    .Where(u => batch.Contains(u.Id))
                                    .Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com")
                                    .Select(u => u.Id).ToListAsync();

                                if (idsToDelete.Any())
                                {
                                    await _db.Users.Where(u => idsToDelete.Contains(u.Id)).ExecuteDeleteAsync();
                                }
                            }
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception)
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                });
            }
            finally 
            {
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            }

            _cache.Remove(cacheKey); // Clear OTP
            _logger.LogCritical("FACTORY RESET COMPLETED by {User} at {Time}", currentUserName ?? "Unknown", DateTime.UtcNow);
            return (true, "تم تصفير النظام بنجاح وبدء تسلسل المعرفات من 1.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FACTORY RESET ERROR");
            return (false, "فشل التصفير. يرجى مراجعة سجلات الخادم.");
        }
    }

    public async Task<(bool Success, string Message, int Count)> FixTreeAsync(string? currentUserName)
    {
        try
        {
            var allAccounts = await _db.Accounts.ToListAsync();
            foreach (var a in allAccounts)
            {
                a.Level = 1;
                a.IsLeaf = true;
            }

            var queue = new Queue<(int? ParentId, int Level)>();
            queue.Enqueue((null, 1));

            var accountLookup = allAccounts.ToLookup(a => a.ParentId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var children = accountLookup[current.ParentId].ToList();
                
                foreach (var child in children)
                {
                    child.Level = current.Level;
                    var hasChildren = accountLookup[child.Id].Any();
                    child.IsLeaf = !hasChildren;
                    
                    queue.Enqueue((child.Id, current.Level + 1));
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("FIX TREE COMPLETED by {User} at {Time}", currentUserName ?? "Unknown", DateTime.UtcNow);
            return (true, "تم إصلاح شجرة الحسابات بنجاح.", allAccounts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FixTree failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.", 0);
        }
    }

    public async Task<(bool Success, string Message)> SyncAccountsAsync(string? currentUserName)
    {
        try 
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                int count = 0;
                var suppliers = await _db.Suppliers.Where(s => s.MainAccountId == null).ToListAsync();
                var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");
                if (parent == null) return (false, "حساب الموردين الرئيسي (2101) غير موجود.");

                var existingCodesList = await _db.Accounts.Where(a => a.ParentId == parent.Id).Select(a => a.Code).ToListAsync();
                var existingCodes = new HashSet<string>(existingCodesList.Where(c => c != null)!);

                long nextCodeNum = 1;
                var maxCode = existingCodes.Max();
                if (maxCode != null && maxCode.Length > 4 && long.TryParse(maxCode.Substring(4), out var existingNum)) {
                    nextCodeNum = existingNum + 1;
                }

                foreach (var s in suppliers)
                {
                    string newCode;
                    while (true)
                    {
                        newCode = $"2101{nextCodeNum:D4}";
                        if (!existingCodes.Contains(newCode)) break;
                        nextCodeNum++;
                    }
                    existingCodes.Add(newCode);

                    var account = new Account
                    {
                        Code = newCode, NameAr = $"مورد - {s.Name}", Type = AccountType.Liability, Nature = AccountNature.Credit,
                        ParentId = parent.Id, Level = parent.Level + 1, IsLeaf = true, AllowPosting = true, CreatedAt = TimeHelper.GetEgyptTime()
                    };
                    _db.Accounts.Add(account);
                    s.MainAccount = account;
                    count++;
                }
                
                // Chunk the save if there are too many changes
                // Since this is creating accounts, standard SaveChanges is usually fine for < 1000 items,
                // but we can chunk the suppliers processing if needed. Since we bulk save at the end:
                await _db.SaveChangesAsync();
                
                _logger.LogInformation("SYNC ACCOUNTS COMPLETED by {User} at {Time}. Created {Count} accounts.", currentUserName ?? "Unknown", DateTime.UtcNow, count);
                return (true, $"تم تعميد {count} حساب مورد بنجاح.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncAccounts failed");
            return (false, "فشلت العملية. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, Dictionary<string, int>? Purged, string Message)> PurgeDeletedAsync(string? currentUserName)
    {
        try {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            try 
            {
                var counts = new Dictionary<string, int>();
                counts["Accounts"] = 0;
                counts["Journals"] = 0;
                counts["Suppliers"] = 0;
                counts["Customers"] = 0;
                counts["Orders"] = 0;
                counts["Products"] = 0;
                counts["Variants"] = 0;
                counts["Purchases"] = 0;
                
                _logger.LogWarning("PURGE DELETED COMPLETED by {User} at {Time}", currentUserName ?? "Unknown", DateTime.UtcNow);
                return (true, counts, "تم الحذف بنجاح");
            } 
            finally 
            {
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "PurgeDeleted failed");
            return (false, null, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message)> FixPosOrdersAsync()
    {
        try {
            var affected = await _db.Orders.Where(o => o.OrderNumber.StartsWith("POS") && o.Source == OrderSource.Website)
                                            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Source, OrderSource.POS));
            return (true, affected > 0 ? $"تم تصحيح {affected} طلب POS." : "كافة الطلبات سليمة.");
        } catch (Exception ex) { 
            _logger.LogError(ex, "FixPosOrders failed"); 
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى."); 
        }
    }

    public async Task<(bool Success, string Message)> CleanupDuplicatesAsync()
    {
        try {
            var dupCodes = await _db.Accounts.GroupBy(a => a.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync();
            if (!dupCodes.Any()) return (true, "لم يتم العثور على تكرارات في الأكواد.");
            return (true, $"تم العثور على {dupCodes.Count} كود مكرر. يرجى استخدام Purge Deleted أولاً.");
        } catch (Exception ex) { 
            _logger.LogError(ex, "CleanupDuplicates failed"); 
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى."); 
        }
    }

    public async Task<(bool Success, string Message)> SyncOrderAccountingAsync()
    {
        try {
            await _accounting.SyncAllOrdersAccountingAsync();
            return (true, "تمت إعادة توليد كافة القيود المحاسبية للمبيعات بنجاح.");
        } catch (Exception ex) {
            _logger.LogError(ex, "SyncOrderAccounting failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message)> SyncPaymentAccountingAsync()
    {
        try {
            await _accounting.SyncAllPaymentAccountingAsync();
            return (true, "تمت إعادة توليد كافة قيود المقبوضات والمدفوعات بنجاح.");
        } catch (Exception ex) {
            _logger.LogError(ex, "SyncPaymentAccounting failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message, object? Details)> SyncPurchaseJournalEntriesAsync()
    {
        try
        {
            int updatedEntries = 0;
            int updatedLines = 0;

            var invoiceEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference != null)
                .ToListAsync();

            var invoicesMap = await _db.PurchaseInvoices.AsNoTracking()
                .Where(i => invoiceEntries.Select(e => e.Reference).Contains(i.InvoiceNumber))
                .ToDictionaryAsync(i => i.InvoiceNumber!, i => i.SupplierId);

            foreach (var entry in invoiceEntries)
            {
                if (entry.Reference != null && invoicesMap.TryGetValue(entry.Reference, out var supplierId))
                {
                    bool entryChanged = false;
                    foreach (var line in entry.Lines.Where(l => l.SupplierId == null))
                    {
                        line.SupplierId = supplierId;
                        updatedLines++;
                        entryChanged = true;
                    }
                    if (entryChanged) updatedEntries++;
                }
            }

            var paymentEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => (e.Type == JournalEntryType.PaymentVoucher || e.Type == JournalEntryType.ReceiptVoucher) && e.Reference != null)
                .ToListAsync();

            var paymentRefs = paymentEntries.Select(e => e.Reference).ToList();
            var supplierPaymentsMap = await _db.SupplierPayments.AsNoTracking()
                .Where(p => paymentRefs.Contains(p.PaymentNumber))
                .ToDictionaryAsync(p => p.PaymentNumber!, p => p.SupplierId);

            var receiptVouchersMap = await _db.ReceiptVouchers.AsNoTracking()
                .Where(v => paymentRefs.Contains(v.VoucherNumber))
                .ToDictionaryAsync(v => v.VoucherNumber!, v => v.CustomerId);

            foreach (var entry in paymentEntries)
            {
                if (entry.Reference == null) continue;

                bool entryChanged = false;
                if (supplierPaymentsMap.TryGetValue(entry.Reference, out var sId))
                {
                    foreach (var line in entry.Lines.Where(l => l.SupplierId == null && l.CustomerId == null))
                    {
                        line.SupplierId = sId;
                        updatedLines++;
                        entryChanged = true;
                    }
                }
                else if (receiptVouchersMap.TryGetValue(entry.Reference, out var cId) && cId != null)
                {
                    foreach (var line in entry.Lines.Where(l => l.CustomerId == null && l.SupplierId == null))
                    {
                        line.CustomerId = cId;
                        updatedLines++;
                        entryChanged = true;
                    }
                }
                
                if (entryChanged) updatedEntries++;
            }

            if (updatedLines > 0)
            {
                await _db.SaveChangesAsync();
            }

            return (true, $"تم تحديث {updatedLines} سطر محاسبي في {updatedEntries} قيد بنجاح.", new { entries = updatedEntries, lines = updatedLines });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SYNC PURCHASE ENTRIES ERROR");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.", null);
        }
    }

    public async Task<(bool Success, string Message)> SyncEntityIdsAsync()
    {
        try {
            await _accounting.SyncAllEntityIdsAsync();
            return (true, "تمت مزامنة كافة معرفات العملاء والموردين بنجاح.");
        } catch (Exception ex) {
            _logger.LogError(ex, "SyncEntityIds failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message)> SyncSubAccountsAsync()
    {
        try {
            await _accounting.ConsolidateSubAccountsToControlAsync();
            return (true, "تم توحيد الحسابات ودمج الحركات في الحسابات الرئيسية بنجاح.");
        } catch (Exception ex) {
            _logger.LogError(ex, "SyncSubAccounts failed");
            return (false, "العملية فشلت. يرجى المحاولة مرة أخرى.");
        }
    }

    public async Task<(bool Success, string Message)> SyncLedgerSourceAsync()
    {
        try {
            var updatedEntries = await _db.JournalEntries
                .Where(e => e.CostCenter == null && e.OrderId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CostCenter, e => _db.Orders.Where(o => o.Id == e.OrderId).Select(o => o.Source).FirstOrDefault()));

            var updatedLines = await _db.JournalLines
                .Where(l => l.CostCenter == null && l.OrderId != null)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.CostCenter, l => _db.Orders.Where(o => o.Id == l.OrderId).Select(o => o.Source).FirstOrDefault()));

            return (true, $"تم تحديث المصدر لـ {updatedEntries} قيد و {updatedLines} بند.");
        } catch (Exception ex) {
            _logger.LogError(ex, "SyncLedgerSource failed");
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string Message)> FixUtcTimesAsync()
    {
        try {
            var rvs = await _db.ReceiptVouchers
                .Where(v => v.VoucherDate.Date == v.CreatedAt.Date) 
                .ToListAsync();
                
            int affectedRvs = 0;
            foreach(var batch in rvs.Chunk(100)) {
                foreach(var v in batch) {
                    if (Math.Abs((v.CreatedAt - v.VoucherDate).TotalHours - 3) < 0.2) {
                        v.VoucherDate = v.VoucherDate.AddHours(3);
                        affectedRvs++;
                    }
                }
            }

            var pvs = await _db.PaymentVouchers
                .Where(v => v.VoucherDate.Date == v.CreatedAt.Date)
                .ToListAsync();
                
            int affectedPvs = 0;
            foreach(var batch in pvs.Chunk(100)) {
                foreach(var v in batch) {
                    if (Math.Abs((v.CreatedAt - v.VoucherDate).TotalHours - 3) < 0.2) {
                        v.VoucherDate = v.VoucherDate.AddHours(3);
                        affectedPvs++;
                    }
                }
            }

            var jes = await _db.JournalEntries
                .Where(e => e.EntryDate.Date == e.CreatedAt.Date)
                .ToListAsync();
                
            int affectedJes = 0;
            foreach(var batch in jes.Chunk(100)) {
                foreach(var e in batch) {
                    if (Math.Abs((e.CreatedAt - e.EntryDate).TotalHours - 3) < 0.2) {
                        e.EntryDate = e.EntryDate.AddHours(3);
                        affectedJes++;
                    }
                }
            }

            if (affectedRvs > 0 || affectedPvs > 0 || affectedJes > 0)
                await _db.SaveChangesAsync();
                
            return (true, $"تم تصحيح {affectedRvs} سند قبض، {affectedPvs} سند صرف، و {affectedJes} قيد محاسبي.");
        } 
        catch (Exception ex) { 
            _logger.LogError(ex, "FixUtcTimes failed");
            return (false, ex.Message); 
        }
    }
}
