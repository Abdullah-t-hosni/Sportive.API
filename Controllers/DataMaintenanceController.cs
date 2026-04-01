using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class DataMaintenanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<DataMaintenanceController> _logger;
    private readonly IBackupService _backupService;

    public DataMaintenanceController(AppDbContext db, ILogger<DataMaintenanceController> logger, IBackupService backupService)
    {
        _db = db;
        _logger = logger;
        _backupService = backupService;
    }

    /// <summary>
    /// مسح كافة بيانات العملاء (تصفير العملاء)
    /// </summary>
    [HttpPost("wipe-customers")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> WipeCustomers()
    {
        _logger.LogWarning("[Maintenance] Wipe Customers requested by: {User}", User.Identity?.Name);
        
        using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");

            // 1. التوابع (الطلبات، العنوان، السلة، الوش لست)
            await _db.CartItems.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.WishlistItems.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.Reviews.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.OrderStatusHistories.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.OrderItems.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.Orders.IgnoreQueryFilters().ExecuteDeleteAsync();
            await _db.Addresses.IgnoreQueryFilters().ExecuteDeleteAsync();

            // 2. المحاسبة التابعة للعملاء
            await _db.ReceiptVouchers.IgnoreQueryFilters().ExecuteDeleteAsync();

            // 3. مسح العملاء
            await _db.Customers.IgnoreQueryFilters().ExecuteDeleteAsync();

            // 4. Identity - مسح المستخدمين ذوي رتبة Customer
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
                    await _db.Users.Where(u => idsToDelete.Contains(u.Id)).ExecuteDeleteAsync();
                }
            }

            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            await transaction.CommitAsync();

            _logger.LogInformation("Customers wiped successfully.");
            return Ok(new { success = true, message = "تم مسح كافة بيانات العملاء بنجاح." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
            _logger.LogError(ex, "Wipe Customers failed.");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// تصفير شامل للنظام (Factory Reset)
    /// </summary>
    [HttpPost("factory-reset")]
    public async Task<IActionResult> FactoryReset()
    {
        _logger.LogCritical("FACTORY RESET requested by: {User}", User.Identity?.Name);

        try 
        {
            // 0. Backup قبل العملية
            try { await _backupService.RunBackupAsync(); } catch { }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");

                // 1. المسح الشامل للجداول التابعة
                await _db.CartItems.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.WishlistItems.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Reviews.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.OrderStatusHistories.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.OrderItems.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.InventoryAuditItems.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.InventoryMovements.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.PurchaseInvoiceItems.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.SupplierPayments.IgnoreQueryFilters().ExecuteDeleteAsync();

                // 2. المحاسبة
                await _db.JournalLines.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.ReceiptVouchers.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.PaymentVouchers.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.JournalEntries.IgnoreQueryFilters().ExecuteDeleteAsync();

                // 3. الكيانات الرئيسية
                await _db.Orders.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.InventoryAudits.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.PurchaseInvoices.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Suppliers.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.ProductImages.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.ProductVariants.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Products.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Coupons.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Addresses.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Customers.IgnoreQueryFilters().ExecuteDeleteAsync();
                await _db.Notifications.IgnoreQueryFilters().ExecuteDeleteAsync();

                // 4. Identity - حذف العملاء مع حماية الأدمن
                var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer");
                if (customerRole != null)
                {
                    var customerUserIds = await _db.UserRoles.Where(ur => ur.RoleId == customerRole.Id).Select(ur => ur.UserId).ToListAsync();
                    var idsToDelete = await _db.Users
                        .Where(u => customerUserIds.Contains(u.Id))
                        .Where(u => u.Email != "admin@sportive.com" && u.Email != "abdullah@sportive.com")
                        .Select(u => u.Id).ToListAsync();

                    if (idsToDelete.Any()) await _db.Users.Where(u => idsToDelete.Contains(u.Id)).ExecuteDeleteAsync();
                }

                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
                await transaction.CommitAsync();

                _logger.LogCritical("FACTORY RESET COMPLETED.");
                return Ok(new { success = true, message = "تم تصفير النظام بالكامل بنجاح." });
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                await _db.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 1;");
                throw;
            }
        }
        catch (Exception)
        {
            _logger.LogError("FACTORY RESET FAILED.");
            return BadRequest(new { success = false, message = "Reset failed." });
        }
    }
}
