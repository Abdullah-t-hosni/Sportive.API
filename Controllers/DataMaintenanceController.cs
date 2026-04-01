using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Sportive.API.Data;
using Sportive.API.Services;
using System.IO;

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
                    try { await _db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE `{table}`;"); } catch { }
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
}
