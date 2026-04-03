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
[Authorize(Roles = "Admin")]
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
            // 1. التحرر من قيود المفاتيح لإلقاء البيانات بفعالية
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");

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

                // 2. Identity - حذف العملاء مع حماية الأدمن
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
                                        .IgnoreQueryFilters()
                                        .ExecuteDeleteAsync();
                    }
                }

                await transaction.CommitAsync();
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");

                _logger.LogCritical("FACTORY RESET COMPLETED.");
                return Ok(new { success = true, message = "تم تصفير النظام بنجاح وبدء تسلسل المعرفات من 1." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
                _logger.LogError(ex, "INNER FACTORY RESET ERROR");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OUTER FACTORY RESET ERROR");
            return BadRequest(new { success = false, message = "فشل التصفير: " + ex.Message });
        }
    }

    [HttpGet("fix-tree"), HttpPost("fix-tree")]
    public async Task<IActionResult> FixTree()
    {
        try
        {
            var allAccounts = await _db.Accounts.IgnoreQueryFilters().Where(a => !a.IsDeleted).ToListAsync();
            
            // 1. Reset all first to be safe
            foreach (var a in allAccounts)
            {
                a.Level = 1;
                a.IsLeaf = true;
            }

            // 2. Recursive level calculation (Helper function locally)
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
        var acc = await _db.Accounts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Code == code);
        if (acc == null) return NotFound(new { message = $"لم يتم العثور على أي حساب بالكود {code}" });
        
        return Ok(new { 
            id = acc.Id, 
            code = acc.Code, 
            name = acc.NameAr, 
            parentId = acc.ParentId,
            level = acc.Level,
            type = acc.Type.ToString()
        });
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
            var query = _db.Accounts.IgnoreQueryFilters().Where(a => a.ParentId == parent.Id);
            var maxCode = await query.MaxAsync(a => (string?)a.Code);
            long nextCodeNum = 1;
            if (maxCode != null && maxCode.Length > 4 && long.TryParse(maxCode.Substring(4), out var existingNum)) {
                nextCodeNum = existingNum + 1;
            }
            
            string newCode;
            while (true)
            {
                newCode = $"2101{nextCodeNum:D4}";
                bool exists = await _db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Code == newCode);
                if (!exists) break;
                nextCodeNum++;
            }

            var account = new Account
            {
                Code = newCode,
                NameAr = $"مورد - {s.Name}",
                Type = AccountType.Liability, Nature = AccountNature.Credit,
                ParentId = parent.Id, Level = parent.Level + 1, IsLeaf = true, AllowPosting = true,
                CreatedAt = DateTime.UtcNow
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
}
