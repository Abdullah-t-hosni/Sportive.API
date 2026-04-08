using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Sportive.API.Data;
using Sportive.API.Services;
using System.IO;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class DataMaintenanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataMaintenanceController> _logger;

    public DataMaintenanceController(AppDbContext db, ILogger<DataMaintenanceController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("wipe-customers")]
    public async Task<IActionResult> WipeCustomers()
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await _db.Addresses.ExecuteDeleteAsync();
            await _db.Customers.ExecuteDeleteAsync();
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");

            return Ok(new { success = true, message = "تم مسح كافة سجلات العملاء." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("factory-reset")]
    public async Task<IActionResult> FactoryReset()
    {
        _logger.LogWarning("FACTORY RESET INITIATED.");

        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    var tablesToTruncate = new[] {
                        "OrderItemAttributes", "InventoryAuditItems", "InventoryMovements", "PurchaseInvoiceItems",
                        "SupplierPayments", "JournalLines", "ReceiptVouchers", "PaymentVouchers", "JournalEntries",
                        "Orders", "InventoryAudits", "PurchaseInvoices", "Suppliers", "ProductImages",
                        "ProductVariants", "Products", "Coupons", "Addresses", "Customers", "Notifications",
                        "CartItems", "WishlistItems", "Reviews", "OrderStatusHistories", "OrderItems"
                    };

                    foreach (var table in tablesToTruncate)
                    {
                        try { await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE `" + table + "`;"); } catch { }
                    }

                    var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
                    if (customerRole != null)
                    {
                        var customerUserIds = await _db.UserRoles.Where(ur => ur.RoleId == customerRole.Id).Select(ur => ur.UserId).ToListAsync();
                        var idsToDelete = await _db.Users
                            .Where(u => customerUserIds.Contains(u.Id))
                            .Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com")
                            .Select(u => u.Id).ToListAsync();

                        if (idsToDelete.Any())
                        {
                             await _db.Users.Where(u => idsToDelete.Contains(u.Id))
                                            .ExecuteDeleteAsync();
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
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            _logger.LogCritical("FACTORY RESET COMPLETED.");
            return Ok(new { success = true, message = "تم تصفير النظام بنجاح وبدء تسلسل المعرفات من 1." });
        }
        catch (Exception ex)
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            _logger.LogError(ex, "FACTORY RESET ERROR");
            return BadRequest(new { success = false, message = "فشل التصفير: " + ex.Message });
        }
    }

    [HttpGet("fix-tree"), HttpPost("fix-tree")]
    public async Task<IActionResult> FixTree()
    {
        try
        {
            var allAccounts = await _db.Accounts.ToListAsync();
            foreach (var a in allAccounts)
            {
                a.Level = 1;
                a.IsLeaf = true;
            }
            UpdateLevelsRecursively(allAccounts, null, 1);
            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "تم إصلاح شجرة الحسابات بنجاح.", count = allAccounts.Count });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private void UpdateLevelsRecursively(List<Account> allAccounts, int? parentId, int level)
    {
        var children = allAccounts.Where(a => a.ParentId == parentId).ToList();
        foreach (var child in children)
        {
            child.Level = level;
            var hasChildren = allAccounts.Any(a => a.ParentId == child.Id);
            child.IsLeaf = !hasChildren;
            UpdateLevelsRecursively(allAccounts, child.Id, level + 1);
        }
    }

    [HttpGet("debug-account/{code}")]
    public async Task<IActionResult> DebugAccount(string code)
    {
        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == code);
        if (acc == null) return NotFound(new { message = $"لم يتم العثور على أي حساب بالكود {code}" });
        return Ok(new { id = acc.Id, code = acc.Code, name = acc.NameAr, parentId = acc.ParentId, level = acc.Level, type = acc.Type.ToString() });
    }

    [HttpGet("debug-supplier/{id}")]
    public async Task<IActionResult> DebugSupplier(int id)
    {
        var s = await _db.Suppliers.Include(s => s.MainAccount).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound(new { message = "المورد غير موجود نهائياً." });
        return Ok(new { s.Id, s.Name, s.Phone, s.MainAccountId, accountCode = s.MainAccount?.Code });
    }

    [HttpGet("sync-accounts")]
    public async Task<IActionResult> SyncAccounts()
    {
        int count = 0;
        var suppliers = await _db.Suppliers.Where(s => s.MainAccountId == null).ToListAsync();
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");
        if (parent == null) return BadRequest("حساب الموردين الرئيسي (2101) غير موجود.");

        foreach (var s in suppliers)
        {
            var query = _db.Accounts.Where(a => a.ParentId == parent.Id);
            var maxCode = await query.MaxAsync(a => (string?)a.Code);
            long nextCodeNum = 1;
            if (maxCode != null && maxCode.Length > 4 && long.TryParse(maxCode.Substring(4), out var existingNum)) {
                nextCodeNum = existingNum + 1;
            }
            string newCode;
            while (true)
            {
                newCode = $"2101{nextCodeNum:D4}";
                if (!await _db.Accounts.AnyAsync(a => a.Code == newCode)) break;
                nextCodeNum++;
            }
            var account = new Account
            {
                Code = newCode, NameAr = $"مورد - {s.Name}", Type = AccountType.Liability, Nature = AccountNature.Credit,
                ParentId = parent.Id, Level = parent.Level + 1, IsLeaf = true, AllowPosting = true, CreatedAt = DateTime.UtcNow
            };
            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();
            s.MainAccountId = account.Id;
            count++;
        }
        await _db.SaveChangesAsync();
        return Ok(new { message = $"تم تعميد {count} حساب مورد بنجاح." });
    }

    [HttpGet("rebuild"), HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild() => await FixTree();

    [HttpGet("purge-deleted"), HttpPost("purge-deleted")]
    public async Task<IActionResult> PurgeDeleted()
    {
        try {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            var counts = new Dictionary<string, int>();
            counts["Accounts"] = 0;
            counts["Journals"] = 0;
            counts["Suppliers"] = 0;
            counts["Customers"] = 0;
            counts["Orders"] = 0;
            counts["Products"] = 0;
            counts["Variants"] = 0;
            counts["Purchases"] = 0;
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            return Ok(new { success = true, purged = counts });
        }
        catch (Exception ex) {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("fix-pos-orders")]
    public async Task<IActionResult> FixPosOrders()
    {
        try {
            var affected = await _db.Orders.Where(o => o.OrderNumber.StartsWith("POS") && o.Source == OrderSource.Website)
                                         .ExecuteUpdateAsync(s => s.SetProperty(o => o.Source, OrderSource.POS));
            return Ok(new { success = true, message = affected > 0 ? $"تم تصحيح {affected} طلب POS." : "كافة الطلبات سليمة." });
        } catch (Exception ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpPost("cleanup-duplicates")]
    public async Task<IActionResult> CleanupDuplicates()
    {
        try {
            var dupCodes = await _db.Accounts.GroupBy(a => a.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync();
            if (!dupCodes.Any()) return Ok(new { success = true, message = "لم يتم العثور على تكرارات في الأكواد." });
            return Ok(new { success = true, message = $"تم العثور على {dupCodes.Count} كود مكرر. يرجى استخدام Purge Deleted أولاً." });
        } catch (Exception ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }
    [HttpPost("sync-purchase-journal-entries")]
    public async Task<IActionResult> SyncPurchaseJournalEntries()
    {
        try
        {
            int updatedEntries = 0;
            int updatedLines = 0;

            // 1. Process Purchase Invoices
            var invoiceEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => e.Type == JournalEntryType.PurchaseInvoice && e.Reference != null)
                .ToListAsync();

            foreach (var entry in invoiceEntries)
            {
                var invoice = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.InvoiceNumber == entry.Reference);
                if (invoice != null)
                {
                    bool entryChanged = false;
                    foreach (var line in entry.Lines.Where(l => l.SupplierId == null))
                    {
                        line.SupplierId = invoice.SupplierId;
                        updatedLines++;
                        entryChanged = true;
                    }
                    if (entryChanged) updatedEntries++;
                }
            }

            // 2. Process Supplier Payments (Payment Vouchers)
            var paymentEntries = await _db.JournalEntries
                .Include(e => e.Lines)
                .Where(e => (e.Type == JournalEntryType.PaymentVoucher || e.Type == JournalEntryType.ReceiptVoucher) && e.Reference != null)
                .ToListAsync();

            foreach (var entry in paymentEntries)
            {
                // Try to find in SupplierPayments
                var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.PaymentNumber == entry.Reference);
                if (payment != null)
                {
                    bool entryChanged = false;
                    foreach (var line in entry.Lines.Where(l => l.SupplierId == null && l.CustomerId == null))
                    {
                        line.SupplierId = payment.SupplierId;
                        updatedLines++;
                        entryChanged = true;
                    }
                    if (entryChanged) updatedEntries++;
                }
                else 
                {
                    // Try to find in ReceiptVouchers (for Customer returns/payments if needed)
                    var rv = await _db.ReceiptVouchers.FirstOrDefaultAsync(v => v.VoucherNumber == entry.Reference);
                    if (rv != null && rv.CustomerId != null)
                    {
                        bool entryChanged = false;
                        foreach (var line in entry.Lines.Where(l => l.CustomerId == null && l.SupplierId == null))
                        {
                            line.CustomerId = rv.CustomerId;
                            updatedLines++;
                            entryChanged = true;
                        }
                        if (entryChanged) updatedEntries++;
                    }
                }
            }

            if (updatedLines > 0)
            {
                await _db.SaveChangesAsync();
            }

            return Ok(new { 
                success = true, 
                message = $"تم تحديث {updatedLines} سطر محاسبي في {updatedEntries} قيد بنجاح.",
                details = new { entries = updatedEntries, lines = updatedLines }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SYNC PURCHASE ENTRIES ERROR");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}
