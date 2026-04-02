// ============================================================
// Controllers/AccountingControllers.cs
// تم دمج ملفات المحاسبة (Accounts, Journal, Vouchers) في ملف واحد منظم لتجنب مشاكل Swagger
// ============================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
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
            Code           = dto.Code,
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

        account.IsDeleted = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings()
    {
        var mappings = await _db.AccountMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
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
            var mapping = await _db.AccountMappings.FirstOrDefaultAsync(m => m.Key == kvp.Key);
            if (mapping != null)
            {
                mapping.AccountId = kvp.Value;
                mapping.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _db.AccountMappings.Add(new AccountMapping { Key = kvp.Key, AccountId = kvp.Value });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
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
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.JournalEntries.Where(e => !e.IsDeleted);
        var total = await q.CountAsync();
        var entries = await q.OrderByDescending(e => e.EntryDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new PaginatedResult<JournalEntry>(entries, total, page, pageSize, (int)Math.Ceiling(total/(double)pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entry = await _db.JournalEntries.Include(e => e.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (entry == null) return NotFound();
        return Ok(entry);
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
        if (entry.Status == JournalEntryStatus.Posted)
            return BadRequest("لا يمكن حذف قيد مرحّل.");

        entry.IsDeleted = true;
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
        var q = _db.ReceiptVouchers.Where(v => !v.IsDeleted);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Notes })
            .ToListAsync();

        return Ok(new PaginatedResult<object>(items, total, page, pageSize, (int)Math.Ceiling(total/(double)pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReceiptVoucher voucher)
    {
        voucher.CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _db.ReceiptVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostReceiptVoucherAsync(voucher);
        return Ok(voucher);
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
        var q = _db.PaymentVouchers.Where(v => !v.IsDeleted);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Notes })
            .ToListAsync();

        return Ok(new PaginatedResult<object>(items, total, page, pageSize, (int)Math.Ceiling(total/(double)pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PaymentVoucher voucher)
    {
        voucher.CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _db.PaymentVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostPaymentVoucherAsync(voucher);
        return Ok(voucher);
    }
}
