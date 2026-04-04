// ============================================================
// Controllers/AccountingControllers.cs
// تم دمج ملفات المحاسبة (Accounts, Journal, Vouchers) في ملف واحد منظم لتجنب مشاكل Swagger
// ============================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.DTOs;
using System.Security.Claims;

namespace Sportive.API.Controllers;

// 1. ACCOUNTS (دليل الحسابات)
[ApiController, Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AccountsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool onlyActive = false)
    {
        var q = _db.Accounts.Where(a => !a.IsDeleted);
        if (onlyActive) q = q.Where(a => a.IsActive);
        
        var accounts = await q.OrderBy(a => a.Code).ToListAsync();
        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (account == null) return NotFound();
        return Ok(account);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountDto dto)
    {
        // التحقق من تكرار الكود
        if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code && !a.IsDeleted))
            return BadRequest("كود الحساب موجود مسبقاً.");

        var account = new Account
        {
            Code           = dto.Code ?? string.Empty,
            NameAr         = dto.NameAr,
            NameEn         = dto.NameEn,
            Type           = dto.Type,
            Nature         = dto.Nature,
            ParentId       = dto.ParentId,
            OpeningBalance = dto.OpeningBalance,
            IsActive       = true,
            Level          = dto.Level,
            IsLeaf         = dto.IsLeaf
        };

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAccountDto dto)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        account.NameAr         = dto.NameAr;
        account.NameEn         = dto.NameEn;
        account.IsActive       = dto.IsActive;
        account.OpeningBalance = dto.OpeningBalance;

        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        // منع الحذف إذا كان هناك حركات
        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id && !l.IsDeleted))
            return BadRequest("لا يمكن حذف حساب يحتوي على حركات مالية.");

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings()
    {
        var mappings = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        return Ok(mappings);
    }

    [HttpPut("mappings")]
    public async Task<IActionResult> SaveMappingsPut([FromBody] Dictionary<string, int?>? body)
        => await SaveMappingsInternal(body);

    [HttpPost("mappings")]
    public async Task<IActionResult> SaveMappingsPost([FromBody] Dictionary<string, int?>? body)
        => await SaveMappingsInternal(body);

    private async Task<IActionResult> SaveMappingsInternal(Dictionary<string, int?>? body)
    {
        if (body == null) return BadRequest("بيانات الربط مفقودة.");

        foreach (var kvp in body)
        {
            var mapping = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == kvp.Key);
            if (mapping != null)
            {
                mapping.AccountId = kvp.Value;
                mapping.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.AccountSystemMappings.Add(new AccountSystemMapping { Key = kvp.Key, AccountId = kvp.Value });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
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

            // 2. Recursive level calculation
            void UpdateLevels(int? parentId, int level)
            {
                var children = allAccounts.Where(a => a.ParentId == parentId).ToList();
                foreach (var child in children)
                {
                    child.Level = level;
                    var hasChildren = allAccounts.Any(a => a.ParentId == child.Id);
                    child.IsLeaf = !hasChildren;
                    UpdateLevels(child.Id, level + 1);
                }
            }

            UpdateLevels(null, 1);

            await _db.SaveChangesAsync();
            return Ok(new { success = true, message = "تم إصلاح شجرة الحسابات بنجاح.", count = allAccounts.Count });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("rebuild"), HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild() => await FixTree();
}

// 2. JOURNAL ENTRIES (قيود اليومية)
[ApiController, Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class JournalEntriesController : ControllerBase
{
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    public JournalEntriesController(IAccountingService accounting, AppDbContext db)
    {
        _accounting = accounting;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        var q = _db.JournalEntries.AsNoTracking();

        if (!string.IsNullOrEmpty(search))
            q = q.Where(e => e.EntryNumber.Contains(search) || (e.Description != null && e.Description.Contains(search)) || (e.Reference != null && e.Reference.Contains(search)));
        
        if (fromDate.HasValue) q = q.Where(e => e.EntryDate >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var total = await q.CountAsync();
        var entries = await q.OrderByDescending(e => e.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(e => new {
                Id = e.Id,
                EntryNumber = e.EntryNumber,
                EntryDate = e.EntryDate,
                Description = e.Description,
                Reference = e.Reference,
                Status = e.Status.ToString(),
                Type = e.Type.ToString(),
                LineCount = _db.JournalLines.Count(l => l.JournalEntryId == e.Id),
                TotalAmount = _db.JournalLines.Where(l => l.JournalEntryId == e.Id && l.Debit > 0).Sum(l => (decimal?)l.Debit) ?? 0
            })
            .ToListAsync();

        return Ok(new { items = entries, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var e = await _db.JournalEntries
            .Include(x => x.Lines).ThenInclude(l => l.Account)
            .Include(x => x.Lines).ThenInclude(l => l.Supplier)
            .Include(x => x.Lines).ThenInclude(l => l.Customer)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (e == null) return NotFound();

        // 🏛️ Projection to DTO with Entity Resolution
        return Ok(new JournalEntryDto(
            e.Id, e.EntryNumber, e.EntryDate, e.Type.ToString(), e.Status.ToString(),
            e.Reference, e.Description, e.TotalDebit, e.TotalCredit, e.IsBalanced, e.CreatedAt,
            e.Lines.Select(l => new JournalLineDto(
                l.Id, l.AccountId, l.Account?.Code ?? "", l.Account?.NameAr ?? "",
                l.Debit, l.Credit, l.Description, l.CustomerId, l.SupplierId,
                l.Supplier?.Name ?? l.Customer?.FullName ?? null
            )).ToList(),
            e.AttachmentUrl, e.AttachmentPublicId,
            null, // Global CustomerId
            null  // Global SupplierId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var entry = await _accounting.PostManualEntryAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();
        if (entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن حذف قيد مرحّل إلا من خلال الإدارة.");

        _db.JournalEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// 3. RECEIPT VOUCHERS (سندات القبض)
[ApiController, Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class ReceiptVouchersController : ControllerBase
{
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    public ReceiptVouchersController(IAccountingService accounting, AppDbContext db)
    {
        _accounting = accounting;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.ReceiptVouchers;
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptVoucherDto dto)
    {
        var year = DateTime.UtcNow.Year % 100;
        var count = await _db.ReceiptVouchers.IgnoreQueryFilters().CountAsync() + 1;
        var vNo = $"RV-{year}{count:D4}";

        var voucher = new ReceiptVoucher
        {
            VoucherNumber = vNo,
            VoucherDate = dto.VoucherDate,
            Amount = dto.Amount,
            CashAccountId = dto.CashAccountId,
            FromAccountId = dto.FromAccountId,
            CustomerId = dto.CustomerId,
            PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference,
            Description = dto.Description,
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            CreatedAt = DateTime.UtcNow
        };

        _db.ReceiptVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostReceiptVoucherAsync(voucher);
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.ReceiptVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن حذف سند مرحّل إلا من خلال الإدارة.");

        _db.ReceiptVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);

        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// 4. PAYMENT VOUCHERS (سندات الصرف)
[ApiController, Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class PaymentVouchersController : ControllerBase
{
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    public PaymentVouchersController(IAccountingService accounting, AppDbContext db)
    {
        _accounting = accounting;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PaymentVouchers;
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentVoucherDto dto)
    {
        var year = DateTime.UtcNow.Year % 100;
        var count = await _db.PaymentVouchers.IgnoreQueryFilters().CountAsync() + 1;
        var vNo = $"PV-{year}{count:D4}";

        var voucher = new PaymentVoucher
        {
            VoucherNumber = vNo,
            VoucherDate = dto.VoucherDate,
            Amount = dto.Amount,
            CashAccountId = dto.CashAccountId,
            ToAccountId = dto.ToAccountId,
            SupplierId = dto.SupplierId,
            PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference,
            Description = dto.Description,
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            CreatedAt = DateTime.UtcNow
        };

        _db.PaymentVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostPaymentVoucherAsync(voucher);
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.PaymentVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن حذف سند مرحّل إلا من خلال الإدارة.");

        _db.PaymentVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
