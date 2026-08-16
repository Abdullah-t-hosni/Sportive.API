using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Sportive.API.Attributes;

namespace Sportive.API.Controllers;

[Route("api/system/audit-logs")]
[ApiController]
[RequirePermission(ModuleKeys.AuditLogs)]
public class AuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly Sportive.API.Interfaces.ITenantContext _tenantContext;

    public AuditLogsController(AppDbContext db, IWebHostEnvironment env, Sportive.API.Interfaces.ITenantContext tenantContext)
    {
        _db = db;
        _env = env;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? userId,
        [FromQuery] string? fromDate,
        [FromQuery] string? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(action))
        {
            if (action == "CreateOrder")
            {
                query = query.Where(x => x.Action == "CreateOrder" || 
                    (x.Notes != null && (x.Notes.Contains("SPT-") || x.Notes.Contains("spt-"))));
            }
            else if (action == "CreatePosOrder")
            {
                query = query.Where(x => x.Action == "CreatePosOrder" || 
                    (x.Notes != null && (x.Notes.Contains("POS-") || x.Notes.Contains("pos-"))));
            }
            else
            {
                query = query.Where(x => x.Action.Contains(action));
            }
        }

        if (!string.IsNullOrEmpty(entityType))
            query = query.Where(x => x.EntityType.Contains(entityType));

        if (!string.IsNullOrEmpty(entityId))
            query = query.Where(x => x.EntityId == entityId);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(x => x.UserId == userId);

        if (!string.IsNullOrEmpty(fromDate) && DateTime.TryParse(fromDate, out var fromDt))
            query = query.Where(x => x.CreatedAt >= fromDt);

        if (!string.IsNullOrEmpty(toDate) && DateTime.TryParse(toDate, out var toDt))
        {
            toDt = toDt.Date.AddDays(1).AddTicks(-1);
            query = query.Where(x => x.CreatedAt <= toDt);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userIds = items.Select(x => x.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().ToList();
        var usersMap = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => !string.IsNullOrEmpty(u.FullName) ? u.FullName : u.UserName ?? "النظام");

        var formattedItems = items.Select(x => {
            string? resolvedUser = x.UserName;
            if (string.IsNullOrEmpty(resolvedUser) || resolvedUser.Contains("http://") || resolvedUser.Contains("schemas.xmlsoap") || resolvedUser == x.UserId)
            {
                if (!string.IsNullOrEmpty(x.UserId) && usersMap.TryGetValue(x.UserId, out var name))
                    resolvedUser = name;
                else
                {
                    if (x.Action == "BOSTAWEBHOOKRAW") 
                        resolvedUser = "نظام شحن بوسطة";
                    else if (x.EntityType == "Cart" || x.EntityType == "CartItem") 
                        resolvedUser = "العميل (عبر المتجر)";
                    else if (x.Action == "createorder" || x.Action == "updateorder") 
                        resolvedUser = "العميل / النظام";
                    else 
                        resolvedUser = "النظام (System)";
                }
            }

            return new {
                x.Id,
                Action = FormatActionNameAr(x.Action),
                ActionRaw = x.Action,
                EntityType = FormatEntityTypeAr(x.EntityType),
                EntityTypeRaw = x.EntityType,
                x.EntityId,
                x.Notes,
                UserId = x.UserId,
                UserName = resolvedUser,
                x.IpAddress,
                x.OldValues,
                x.NewValues,
                x.CreatedAt
            };
        });

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = formattedItems
        });
    }

    private static string FormatActionNameAr(string action) => action switch
    {
        "ReorderCategories" => "إعادة ترتيب الأقسام",
        "CreateCategory" => "إضافة قسم جديد",
        "UpdateCategory" => "تعديل بيانات قسم",
        "DeleteCategory" => "حذف قسم",
        "CreateBrand" => "إضافة ماركة جديدة",
        "UpdateBrand" => "تعديل ماركة",
        "DeleteBrand" => "حذف ماركة",
        "CreateOrder" => "إنشاء طلب أونلاين",
        "CreatePosOrder" => "إنشاء طلب كاشير",
        "UpdateOrder" => "تعديل بيانات طلب",
        "DeleteOrder" => "حذف طلب",
        "UpdatePaymentStatus" => "تحديث حالة السداد",
        "UpdateAdminNote" => "تعديل ملاحظة الإدارة",
        "PartialReturn" => "إرجاع جزئي للطلب",
        "CreateCustomer" => "إضافة عميل جديد",
        "UpdateCustomer" => "تعديل بيانات عميل",
        "DeleteCustomer" => "حذف عميل",
        "ToggleCustomer" => "تغيير حالة عميل",
        "CreateBranch" => "إضافة فرع جديد",
        "UpdateBranch" => "تعديل بيانات فرع",
        "DeleteBranch" => "حذف فرع",
        "CreateWarehouse" => "إضافة مخزن جديد",
        "UpdateWarehouse" => "تعديل بيانات مخزن",
        "DeleteWarehouse" => "حذف مخزن",
        "Login" => "تسجيل دخول",
        "Logout" => "تسجيل خروج",
        "UpdateSettings" => "تعديل إعدادات النظام",
        "ImportProducts" => "استيراد منتجات",
        "ImportInventory" => "استيراد كميات المخزون",
        "CreatePOSClosure" => "إغلاق وردية كاشير",
        "UpdatePOSClosure" => "تعديل وردية كاشير",
        "DeletePOSClosure" => "حذف وردية كاشير",
        "CreateInventoryAudit" => "بدء جرد مخزني",
        "PostInventoryAudit" => "اعتماد جرد مخزني",
        "BostaWebhook" => "تحديث من شركة بوسطة",
        _ => action
    };

    private static string FormatEntityTypeAr(string entityType) => entityType switch
    {
        "Category" => "قسم",
        "Brand" => "ماركة",
        "Customer" => "عميل",
        "Order" => "طلب",
        "User" => "مستخدم",
        "Branch" => "فرع",
        "Warehouse" => "مخزن",
        "Product" => "منتج",
        "ProductVariant" => "متغير منتج",
        "PurchaseInvoice" => "فاتورة شراء",
        "InventoryAudit" => "جرد مخزني",
        "StoreInfo" => "إعدادات",
        "System" => "النظام",
        "POSShiftClosure" => "وردية كاشير",
        _ => entityType
    };

    [HttpGet("{id}")]
    public async Task<IActionResult> GetLogById(int id)
    {
        var log = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (log == null) return NotFound("Audit log not found");
        return Ok(log);
    }

    [HttpGet("uncancelled-orders")]
    public async Task<IActionResult> GetUncancelledOrdersAudit()
    {
        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(x => (x.EntityType == "Order" || x.EntityType == "OrderStatus" || x.Action.Contains("Status")) &&
                        x.OldValues != null && x.OldValues.Contains("Cancelled") &&
                        x.NewValues != null && !x.NewValues.Contains("Cancelled"))
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync();

        return Ok(logs);
    }

    [HttpPost("archive")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> ArchiveOldLogs()
    {
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
        var oldLogs = await _db.AuditLogs
            .Where(x => x.CreatedAt < oneMonthAgo)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        if (!oldLogs.Any())
        {
            return Ok(new { message = "No logs to archive.", count = 0 });
        }

        var prefix = _tenantContext.CurrentTenant?.Slug?.ToLowerInvariant() ?? "global";
        var backupDir = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), prefix, "backups", "audit_logs");
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var fileName = $"audit_logs_archive_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        var filePath = Path.Combine(backupDir, fileName);

        var json = JsonSerializer.Serialize(oldLogs, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(filePath, json);

        _db.AuditLogs.RemoveRange(oldLogs);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Archive successful", count = oldLogs.Count, file = fileName });
    }
}
