using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

internal static class AccountMappingsExceptionHelper
{
    internal static bool LooksLikeMissingMappingsTable(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException!)
        {
            var m = e.Message;
            if (m.Contains("AccountSystemMappings", StringComparison.OrdinalIgnoreCase) &&
                (m.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
                 || m.Contains("Unknown table", StringComparison.OrdinalIgnoreCase)
                 || m.Contains("Base table or view not found", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }
}

// ══════════════════════════════════════════════════════
// CHART OF ACCOUNTS
// ══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AccountsController> _log;

    public AccountsController(AppDbContext db, ILogger<AccountsController> log)
    {
        _db  = db;
        _log = log;
    }

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

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (acct == null) return NotFound();

        // Get balance
        var balances = await GetAccountBalances(null, null);
        var balance = balances.FirstOrDefault(b => b.AccountId == id)?.Balance ?? 0;

        return Ok(new AccountFlatDto(
            acct.Id, acct.Code, acct.NameAr, acct.NameEn,
            acct.Type.ToString(), acct.Nature.ToString(),
            acct.ParentId, acct.Level, acct.AllowPosting, acct.IsActive,
            balance
        ));
    }

    /// <summary>
    /// ربط مفاتيح النظام (مبيعات، مخزون، نقدية...) بحسابات شجرة الحسابات — للواجهة التي تستدعي `/api/accounts/mappings`.
    /// </summary>
    [HttpGet("mappings")]
    public async Task<IActionResult> GetMappings() => await OkMappingsOrErrorAsync();

    [HttpPut("mappings")]
    [HttpPost("mappings")]
    public async Task<IActionResult> SaveMappings([FromBody] Dictionary<string, int?>? body)
    {
        if (body == null)
            return BadRequest(new { message = "Body is required (empty object {} is allowed)." });

        foreach (var kv in body)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Key.Length > AccountSystemMapping.MaxKeyLength)
                return BadRequest(new { message = $"Invalid key: {kv.Key}" });

            if (kv.Value.HasValue)
            {
                var ok = await _db.Accounts.AnyAsync(a => a.Id == kv.Value.Value && !a.IsDeleted);
                if (!ok)
                    return BadRequest(new { message = $"Account id {kv.Value} not found for key '{kv.Key}'" });
            }
        }

        foreach (var kv in body)
        {
            var row = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == kv.Key && !m.IsDeleted);
            if (row == null)
            {
                _db.AccountSystemMappings.Add(new AccountSystemMapping
                {
                    Key       = kv.Key,
                    AccountId = kv.Value,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.AccountId = kv.Value;
                row.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return await OkMappingsOrErrorAsync();
    }

    private async Task<IActionResult> OkMappingsOrErrorAsync()
    {
        try
        {
            return Ok(await GetMappingsDictionaryAsync());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AccountSystemMappings read failed");
            if (AccountMappingsExceptionHelper.LooksLikeMissingMappingsTable(ex))
            {
                return StatusCode(503, new
                {
                    message = "جدول ربط الحسابات غير موجود في قاعدة البيانات. طبّق migrations الأخيرة (مثلاً AddAccountSystemMappings) ثم أعد النشر.",
                    code = "MAPPINGS_TABLE_MISSING"
                });
            }

            throw;
        }
    }

    private async Task<Dictionary<string, int?>> GetMappingsDictionaryAsync()
    {
        var rows = await _db.AccountSystemMappings
            .Where(m => !m.IsDeleted)
            .AsNoTracking()
            .ToListAsync();

        return rows
            .Where(r => !string.IsNullOrEmpty(r.Key))
            .GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().AccountId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountDto dto)
    {
        string? generatedCode = dto.Code;

        int level = 1;
        Account? parent = null;
        if (dto.ParentId.HasValue)
        {
            parent = await _db.Accounts.FindAsync(dto.ParentId.Value);
            if (parent == null) return BadRequest(new { message = "الحساب الأب غير موجود" });
            level = parent.Level + 1;
        }

        // Automatic Code Generation
        if (string.IsNullOrWhiteSpace(generatedCode))
        {
            if (parent != null)
            {
                // Find last child under this parent
                var lastChildCode = await _db.Accounts
                    .Where(a => a.ParentId == dto.ParentId && !a.IsDeleted)
                    .OrderByDescending(a => a.Code)
                    .Select(a => a.Code)
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(lastChildCode))
                {
                    // First child: ParentCode + "01"
                    generatedCode = parent.Code + "01";
                }
                else
                {
                    // Increment suffix
                    string prefix = parent.Code;
                    string suffixStr = lastChildCode.Substring(prefix.Length);
                    if (int.TryParse(suffixStr, out int lastSuffix))
                    {
                        generatedCode = prefix + (lastSuffix + 1).ToString("D2");
                    }
                    else
                    {
                         generatedCode = parent.Code + "01"; // Fallback
                    }
                }
            }
            else
            {
                // Root account auto-increment
                var lastRootCode = await _db.Accounts
                    .Where(a => a.ParentId == null && !a.IsDeleted)
                    .OrderByDescending(a => a.Code)
                    .Select(a => a.Code)
                    .FirstOrDefaultAsync();

                if (int.TryParse(lastRootCode, out int lastNum))
                {
                    generatedCode = (lastNum + 1).ToString();
                }
                else
                {
                    generatedCode = "1";
                }
            }
        }

        if (await _db.Accounts.AnyAsync(a => a.Code == generatedCode && !a.IsDeleted))
            return BadRequest(new { message = $"الكود '{generatedCode}' مستخدم مسبقاً أو غير صالح للتوليد التلقائي" });

        if (parent != null) parent.IsLeaf = false;

        var acct = new Account
        {
            Code         = generatedCode!,
            NameAr       = dto.NameAr,
            NameEn       = dto.NameEn,
            Description  = dto.Description,
            Type         = dto.Type,
            Nature       = dto.Nature,
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
        if (acct.IsSystem) 
        {
            // For system accounts, we ONLY allow updating the opening balance
            acct.OpeningBalance = dto.OpeningBalance;
            acct.UpdatedAt      = DateTime.UtcNow;
        }
        else
        {
            acct.NameAr         = dto.NameAr;
            acct.NameEn         = dto.NameEn;
            acct.Description    = dto.Description;
            acct.AllowPosting   = dto.AllowPosting;
            acct.IsActive       = dto.IsActive;
            acct.OpeningBalance = dto.OpeningBalance;
            acct.UpdatedAt      = DateTime.UtcNow;
        }

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

        // Validate all accounts exist
        foreach (var line in dto.Lines)
        {
            var acct = await _db.Accounts.AnyAsync(a => a.Id == line.AccountId && !a.IsDeleted);
            if (!acct) return BadRequest(new { message = $"الحساب {line.AccountId} غير موجود" });
        }

        var count = await _db.JournalEntries.IgnoreQueryFilters().CountAsync() + 1;
        var year  = dto.EntryDate.Year % 100;
        var entryNo = $"JE-{year}{count:D5}";

        var entry = new JournalEntry
        {
            EntryNumber     = entryNo,
            EntryDate       = dto.EntryDate,
            Type            = JournalEntryType.Manual,
            Status          = dto.AsDraft ? JournalEntryStatus.Draft : JournalEntryStatus.Posted,
            Reference       = dto.Reference,
            Description     = dto.Description,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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

    /// <summary>تعديل قيد يدوي — مسودة فقط.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateJournalEntryDto dto)
    {
        if (!dto.Lines.Any() || dto.Lines.Count < 2)
            return BadRequest(new { message = "القيد يجب أن يحتوي على سطرين على الأقل" });

        var totalDebit  = dto.Lines.Sum(l => l.Debit);
        var totalCredit = dto.Lines.Sum(l => l.Credit);
        if (Math.Round(totalDebit, 2) != Math.Round(totalCredit, 2))
            return BadRequest(new { message = $"القيد غير متوازن — المدين: {totalDebit}, الدائن: {totalCredit}" });

        foreach (var line in dto.Lines)
        {
            var acct = await _db.Accounts.AnyAsync(a => a.Id == line.AccountId && !a.IsDeleted);
            if (!acct) return BadRequest(new { message = $"الحساب {line.AccountId} غير موجود" });
        }

        var entry = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (entry == null) return NotFound();
        if (entry.Type != JournalEntryType.Manual)
            return BadRequest(new { message = "يمكن تعديل القيود اليدوية فقط" });
        if (entry.Status != JournalEntryStatus.Draft)
            return BadRequest(new { message = "يمكن تعديل المسودات فقط" });

        entry.EntryDate   = dto.EntryDate;
        entry.Reference   = dto.Reference;
        entry.Description = dto.Description;
        entry.AttachmentUrl = dto.AttachmentUrl;
        entry.AttachmentPublicId = dto.AttachmentPublicId;
        entry.UpdatedAt   = DateTime.UtcNow;

        foreach (var line in entry.Lines.Where(l => !l.IsDeleted).ToList())
        {
            line.IsDeleted  = true;
            line.UpdatedAt  = DateTime.UtcNow;
        }

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

        if (dto.PostAfterUpdate)
            entry.Status = JournalEntryStatus.Posted;

        await _db.SaveChangesAsync();
        return Ok(new { id = entry.Id, entryNumber = entry.EntryNumber });
    }

    /// <summary>حذف قيد يدوي — مسودة فقط (soft delete).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);

        if (entry == null) return NotFound();
        if (entry.Type != JournalEntryType.Manual)
            return BadRequest(new { message = "يمكن حذف القيود اليدوية فقط" });
        if (entry.Status != JournalEntryStatus.Draft)
            return BadRequest(new { message = "يمكن حذف المسودات فقط" });

        foreach (var line in entry.Lines.Where(l => !l.IsDeleted))
        {
            line.IsDeleted  = true;
            line.UpdatedAt  = DateTime.UtcNow;
        }

        entry.IsDeleted  = true;
        entry.UpdatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
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
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
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
