using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

// ══════════════════════════════════════════════════════
// CHART OF ACCOUNTS
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AccountsController(AppDbContext db) => _db = db;

    // شجرة الحسابات الكاملة (هرمية)
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree()
    {
        var all = await _db.Accounts
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.Code)
            .ToListAsync();

        var balances = await GetAccountBalances(null, null);
        var balanceMap = balances.ToDictionary(b => b.AccountId, b => b.Balance);

        var roots = all.Where(a => a.ParentId == null).ToList();
        return Ok(roots.Select(r => MapTree(r, all, balanceMap)).ToList());
    }

    // قائمة مستوية للـ dropdowns
    [HttpGet]
    public async Task<IActionResult> GetFlat([FromQuery] bool? allowPosting = null)
    {
        var q = _db.Accounts.Where(a => !a.IsDeleted && a.IsActive);
        if (allowPosting.HasValue) q = q.Where(a => a.AllowPosting == allowPosting.Value);

        var accounts = await q.OrderBy(a => a.Code).ToListAsync();
        var balances = await GetAccountBalances(null, null);
        var bmap = balances.ToDictionary(b => b.AccountId, b => b.Balance);

        return Ok(accounts.Select(a => new AccountFlatDto(
            a.Id, a.Code, a.NameAr, a.NameEn,
            a.Type.ToString(), a.Nature.ToString(),
            a.ParentId, a.Level, a.AllowPosting, a.IsActive,
            bmap.GetValueOrDefault(a.Id, 0)
        )));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountDto dto)
    {
        if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code && !a.IsDeleted))
            return BadRequest(new { message = $"الكود '{dto.Code}' مستخدم مسبقاً" });

        int level = 1;
        if (dto.ParentId.HasValue)
        {
            var parent = await _db.Accounts.FindAsync(dto.ParentId.Value);
            if (parent == null) return BadRequest(new { message = "الحساب الأب غير موجود" });
            parent.IsLeaf = false;
            level = parent.Level + 1;
        }

        var nature = dto.Nature;

        var acct = new Account
        {
            Code         = dto.Code,
            NameAr       = dto.NameAr,
            NameEn       = dto.NameEn,
            Description  = dto.Description,
            Type         = dto.Type,
            Nature       = nature,
            ParentId     = dto.ParentId,
            Level        = level,
            AllowPosting = dto.AllowPosting,
            IsLeaf       = true,
            CreatedAt    = DateTime.UtcNow,
        };

        _db.Accounts.Add(acct);
        await _db.SaveChangesAsync();
        return Ok(new { id = acct.Id, code = acct.Code });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAccountDto dto)
    {
        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (acct == null) return NotFound();
        if (acct.IsSystem) return BadRequest(new { message = "لا يمكن تعديل هذا الحساب" });

        acct.NameAr         = dto.NameAr;
        acct.NameEn         = dto.NameEn;
        acct.Description    = dto.Description;
        acct.AllowPosting   = dto.AllowPosting;
        acct.IsActive       = dto.IsActive;
        acct.OpeningBalance = dto.OpeningBalance;
        acct.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var acct = await _db.Accounts
            .Include(a => a.Children)
            .Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);

        if (acct == null) return NotFound();
        if (acct.IsSystem) return BadRequest(new { message = "لا يمكن حذف حساب النظام" });
        if (acct.Children.Any(c => !c.IsDeleted)) return BadRequest(new { message = "يوجد حسابات فرعية — احذفها أولاً" });
        if (acct.Lines.Any()) return BadRequest(new { message = "يوجد قيود على هذا الحساب" });

        acct.IsDeleted = true;
        acct.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // حساب الأرصدة من القيود
    private async Task<List<AccountBalanceDto>> GetAccountBalances(DateTime? from, DateTime? to)
    {
        var q = _db.JournalLines
            .Include(l => l.Account)
            .Where(l => !l.IsDeleted && l.JournalEntry.Status == JournalEntryStatus.Posted);

        if (from.HasValue) q = q.Where(l => l.JournalEntry.EntryDate >= from.Value);
        if (to.HasValue)   q = q.Where(l => l.JournalEntry.EntryDate <= to.Value);

        var lines = await q.ToListAsync();
        var accounts = await _db.Accounts.Where(a => !a.IsDeleted).ToListAsync();

        return accounts.Select(a =>
        {
            var acctLines = lines.Where(l => l.AccountId == a.Id);
            var debit  = acctLines.Sum(l => l.Debit)  + (a.Nature == AccountNature.Debit  ? a.OpeningBalance : 0);
            var credit = acctLines.Sum(l => l.Credit) + (a.Nature == AccountNature.Credit ? a.OpeningBalance : 0);
            var balance = a.Nature == AccountNature.Debit ? debit - credit : credit - debit;
            return new AccountBalanceDto(a.Id, a.Code, a.NameAr, a.Level, debit, credit, balance, a.Nature.ToString());
        }).ToList();
    }

    private AccountDto MapTree(Account node, List<Account> all, Dictionary<int, decimal> bmap)
    {
        var children = all.Where(a => a.ParentId == node.Id).OrderBy(a => a.Code).ToList();
        return new AccountDto(
            node.Id, node.Code, node.NameAr, node.NameEn, node.Description,
            node.Type.ToString(), node.Nature.ToString(), node.ParentId, null,
            node.Level, node.IsLeaf, node.AllowPosting, node.IsActive, node.IsSystem,
            node.OpeningBalance, bmap.GetValueOrDefault(node.Id, 0),
            children.Select(c => MapTree(c, all, bmap)).ToList()
        );
    }
}

// ══════════════════════════════════════════════════════
// JOURNAL ENTRIES
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class JournalEntriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public JournalEntriesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] string?   type       = null,
        [FromQuery] string?   status     = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => !e.IsDeleted)
            .AsQueryable();

        if (fromDate.HasValue) q = q.Where(e => e.EntryDate >= fromDate.Value);
        if (toDate.HasValue)   q = q.Where(e => e.EntryDate <= toDate.Value.AddDays(1));
        if (!string.IsNullOrEmpty(search))
            q = q.Where(e => e.EntryNumber.Contains(search) || (e.Reference != null && e.Reference.Contains(search)) || (e.Description != null && e.Description.Contains(search)));
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<JournalEntryType>(type, out var t))
            q = q.Where(e => e.Type == t);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<JournalEntryStatus>(status, out var s))
            q = q.Where(e => e.Status == s);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(e => new JournalEntrySummaryDto(
                e.Id, e.EntryNumber, e.EntryDate, e.Type.ToString(), e.Status.ToString(),
                e.Reference, e.Description,
                e.Lines.Sum(l => l.Debit), e.Lines.Sum(l => l.Credit),
                e.AttachmentUrl, e.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<JournalEntrySummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entry = await _db.JournalEntries
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (entry == null) return NotFound();

        return Ok(new JournalEntryDto(
            entry.Id, entry.EntryNumber, entry.EntryDate,
            entry.Type.ToString(), entry.Status.ToString(),
            entry.Reference, entry.Description,
            entry.Lines.Sum(l => l.Debit), entry.Lines.Sum(l => l.Credit),
            entry.Lines.Sum(l => l.Debit) == entry.Lines.Sum(l => l.Credit),
            entry.CreatedAt,
            entry.Lines.Select(l => new JournalLineDto(
                l.Id, l.AccountId, l.Account.Code, l.Account.NameAr, l.Debit, l.Credit, l.Description
            )).ToList(),
            entry.AttachmentUrl, entry.AttachmentPublicId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto)
    {
        if (!dto.Lines.Any() || dto.Lines.Count < 2)
            return BadRequest(new { message = "القيد يجب أن يحتوي على سطرين على الأقل" });

        var totalDebit  = dto.Lines.Sum(l => l.Debit);
        var totalCredit = dto.Lines.Sum(l => l.Credit);

        if (Math.Round(totalDebit, 2) != Math.Round(totalCredit, 2))
            return BadRequest(new { message = $"القيد غير متوازن — المدين: {totalDebit}, الدائن: {totalCredit}" });

        // Validate all accounts exist and allow posting
        foreach (var line in dto.Lines)
        {
            var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == line.AccountId && !a.IsDeleted);
            if (acct == null) return BadRequest(new { message = $"الحساب {line.AccountId} غير موجود" });
            if (!acct.AllowPosting) return BadRequest(new { message = $"الحساب '{acct.NameAr}' لا يقبل الترحيل المباشر" });
        }

        var count = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year  = dto.EntryDate.Year % 100;
        var entryNo = $"JE-{year}{count:D5}";

        var entry = new JournalEntry
        {
            EntryNumber     = entryNo,
            EntryDate       = dto.EntryDate,
            Type            = JournalEntryType.Manual,
            Status          = JournalEntryStatus.Posted, // ترحيل مباشر للقيود اليدوية
            Reference       = dto.Reference,
            Description     = dto.Description,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            AttachmentUrl   = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt       = DateTime.UtcNow,
        };

        foreach (var line in dto.Lines)
        {
            entry.Lines.Add(new JournalLine
            {
                AccountId   = line.AccountId,
                Debit       = line.Debit,
                Credit      = line.Credit,
                Description = line.Description,
                CustomerId  = line.CustomerId,
                SupplierId  = line.SupplierId,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entry.Id },
            new { id = entry.Id, entryNumber = entry.EntryNumber });
    }

    // عكس القيد
    [HttpPost("{id}/reverse")]
    public async Task<IActionResult> Reverse(int id)
    {
        var entry = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (entry == null) return NotFound();
        if (entry.Status != JournalEntryStatus.Posted)
            return BadRequest(new { message = "يمكن عكس القيود المرحّلة فقط" });

        var count   = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year    = DateTime.UtcNow.Year % 100;
        var revNo   = $"JE-{year}{count:D5}";

        var reversal = new JournalEntry
        {
            EntryNumber     = revNo,
            EntryDate       = DateTime.UtcNow,
            Type            = entry.Type,
            Status          = JournalEntryStatus.Posted,
            Reference       = entry.EntryNumber,
            Description     = $"عكس قيد {entry.EntryNumber}",
            ReversalOfId    = entry.Id,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt       = DateTime.UtcNow,
        };

        // Swap debit ↔ credit
        foreach (var line in entry.Lines)
        {
            reversal.Lines.Add(new JournalLine
            {
                AccountId   = line.AccountId,
                Debit       = line.Credit,
                Credit      = line.Debit,
                Description = line.Description,
                CreatedAt   = DateTime.UtcNow,
            });
        }

        entry.Status    = JournalEntryStatus.Reversed;
        entry.UpdatedAt = DateTime.UtcNow;

        _db.JournalEntries.Add(reversal);
        await _db.SaveChangesAsync();

        return Ok(new { id = reversal.Id, entryNumber = reversal.EntryNumber });
    }
}

// ══════════════════════════════════════════════════════
// RECEIPT VOUCHERS
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class ReceiptVouchersController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReceiptVouchersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.ReceiptVouchers
            .Include(v => v.CashAccount)
            .Include(v => v.FromAccount)
            .Include(v => v.Customer)
            .Where(v => !v.IsDeleted).AsQueryable();

        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value);
        if (toDate.HasValue)   q = q.Where(v => v.VoucherDate <= toDate.Value.AddDays(1));
        if (!string.IsNullOrEmpty(search))
            q = q.Where(v => v.VoucherNumber.Contains(search)
                || (v.Customer != null && v.Customer.FirstName.Contains(search))
                || (v.Description != null && v.Description.Contains(search)));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new VoucherSummaryDto(
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount,
                v.CashAccount.NameAr, v.FromAccount.NameAr,
                v.Customer != null ? v.Customer.FullName : null,
                v.PaymentMethod.ToString(), v.Description,
                v.AttachmentUrl, v.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<VoucherSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptVoucherDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { message = "المبلغ يجب أن يكون أكبر من صفر" });

        var cashAcct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == dto.CashAccountId && !a.IsDeleted);
        var fromAcct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == dto.FromAccountId && !a.IsDeleted);

        if (cashAcct == null || fromAcct == null)
            return BadRequest(new { message = "الحساب غير موجود" });

        var count = await _db.ReceiptVouchers.IgnoreQueryFilters().CountAsync() + 1;
        var year  = dto.VoucherDate.Year % 100;
        var vNo   = $"RV-{year}{count:D4}";

        // القيد: مدين حساب النقدية — دائن حساب المورد/العميل
        var entryCount = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var entryNo    = $"JE-{year}{entryCount:D5}";

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = dto.VoucherDate,
            Type        = JournalEntryType.ReceiptVoucher,
            Status      = JournalEntryStatus.Posted,
            Reference   = vNo,
            Description = dto.Description ?? $"سند قبض {vNo}",
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt   = DateTime.UtcNow,
        };
        entry.Lines.Add(new JournalLine { AccountId = dto.CashAccountId, Debit = dto.Amount,   CreatedAt = DateTime.UtcNow });
        entry.Lines.Add(new JournalLine { AccountId = dto.FromAccountId, Credit = dto.Amount,  CustomerId = dto.CustomerId, CreatedAt = DateTime.UtcNow });

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();

        var voucher = new ReceiptVoucher
        {
            VoucherNumber  = vNo,
            VoucherDate    = dto.VoucherDate,
            Amount         = dto.Amount,
            CashAccountId  = dto.CashAccountId,
            FromAccountId  = dto.FromAccountId,
            CustomerId     = dto.CustomerId,
            PaymentMethod  = dto.PaymentMethod,
            Reference      = dto.Reference,
            Description    = dto.Description,
            JournalEntryId = entry.Id,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            AttachmentUrl  = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt      = DateTime.UtcNow,
        };

        _db.ReceiptVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        return Ok(new { id = voucher.Id, voucherNumber = voucher.VoucherNumber, journalEntryId = entry.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var v = await _db.ReceiptVouchers
            .Include(x => x.JournalEntry).ThenInclude(e => e!.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return NotFound();

        // عكس القيد المحاسبي
        if (v.JournalEntry != null)
        {
            v.JournalEntry.Status    = JournalEntryStatus.Reversed;
            v.JournalEntry.UpdatedAt = DateTime.UtcNow;
        }
        v.IsDeleted = true;
        v.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// PAYMENT VOUCHERS
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class PaymentVouchersController : ControllerBase
{
    private readonly AppDbContext _db;
    public PaymentVouchersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var q = _db.PaymentVouchers
            .Include(v => v.CashAccount).Include(v => v.ToAccount).Include(v => v.Supplier)
            .Where(v => !v.IsDeleted).AsQueryable();

        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value);
        if (toDate.HasValue)   q = q.Where(v => v.VoucherDate <= toDate.Value.AddDays(1));
        if (!string.IsNullOrEmpty(search))
            q = q.Where(v => v.VoucherNumber.Contains(search)
                || (v.Supplier != null && v.Supplier.Name.Contains(search))
                || (v.Description != null && v.Description.Contains(search)));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new VoucherSummaryDto(
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount,
                v.CashAccount.NameAr, v.ToAccount.NameAr,
                v.Supplier != null ? v.Supplier.Name : null,
                v.PaymentMethod.ToString(), v.Description,
                v.AttachmentUrl, v.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<VoucherSummaryDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentVoucherDto dto)
    {
        if (dto.Amount <= 0) return BadRequest(new { message = "المبلغ يجب أن يكون أكبر من صفر" });

        var count = await _db.PaymentVouchers.IgnoreQueryFilters().CountAsync() + 1;
        var year  = dto.VoucherDate.Year % 100;
        var vNo   = $"PV-{year}{count:D4}";

        // القيد: مدين حساب المصروف/المورد — دائن حساب النقدية
        var entryCount = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var entryNo    = $"JE-{year}{entryCount:D5}";

        var entry = new JournalEntry
        {
            EntryNumber = entryNo,
            EntryDate   = dto.VoucherDate,
            Type        = JournalEntryType.PaymentVoucher,
            Status      = JournalEntryStatus.Posted,
            Reference   = vNo,
            Description = dto.Description ?? $"سند دفع {vNo}",
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt   = DateTime.UtcNow,
        };
        entry.Lines.Add(new JournalLine { AccountId = dto.ToAccountId,   Debit  = dto.Amount, SupplierId = dto.SupplierId, CreatedAt = DateTime.UtcNow });
        entry.Lines.Add(new JournalLine { AccountId = dto.CashAccountId, Credit = dto.Amount, CreatedAt = DateTime.UtcNow });

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync();

        var voucher = new PaymentVoucher
        {
            VoucherNumber   = vNo,
            VoucherDate     = dto.VoucherDate,
            Amount          = dto.Amount,
            CashAccountId   = dto.CashAccountId,
            ToAccountId     = dto.ToAccountId,
            SupplierId      = dto.SupplierId,
            PaymentMethod   = dto.PaymentMethod,
            Reference       = dto.Reference,
            Description     = dto.Description,
            JournalEntryId  = entry.Id,
            CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            AttachmentUrl   = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CreatedAt       = DateTime.UtcNow,
        };

        _db.PaymentVouchers.Add(voucher);
        await _db.SaveChangesAsync();

        return Ok(new { id = voucher.Id, voucherNumber = voucher.VoucherNumber, journalEntryId = entry.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var v = await _db.PaymentVouchers
            .Include(x => x.JournalEntry)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (v == null) return NotFound();

        if (v.JournalEntry != null)
        {
            v.JournalEntry.Status    = JournalEntryStatus.Reversed;
            v.JournalEntry.UpdatedAt = DateTime.UtcNow;
        }
        v.IsDeleted = true;
        v.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
