using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.DTOs;
using Sportive.API.Utils;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

[ApiController, Route("api/[controller]")]
[RequirePermission(ModuleKeys.AccountingMain + "," + ModuleKeys.Pos + "," + ModuleKeys.PurchasesMain + "," + ModuleKeys.ReturnsFull)]
public class JournalEntriesController : ControllerBase
{
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    private readonly IPdfService _pdf;
    private readonly ITranslator _t;
    private readonly SequenceService _seq;
    private readonly AccountingCoreService _core;
    private readonly IAuditService _audit;
    public JournalEntriesController(IAccountingService accounting, AppDbContext db, IPdfService pdf, ITranslator t, SequenceService seq, AccountingCoreService core, IAuditService audit)
    {
        _accounting = accounting;
        _db = db;
        _pdf = pdf;
        _t = t;
        _seq = seq;
        _core = core;
        _audit = audit;
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var entry = await _db.JournalEntries
            .Include(x => x.Lines).ThenInclude(l => l.Account)
            .Include(x => x.Lines).ThenInclude(l => l.Supplier)
            .Include(x => x.Lines).ThenInclude(l => l.Customer)
            .Include(x => x.Lines).ThenInclude(l => l.Employee)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (entry == null) return NotFound();

        var pdfBytes = await _pdf.GenerateJournalEntryPdfAsync(entry);
        return File(pdfBytes, "application/pdf", $"JV-{entry.EntryNumber}.pdf");
    }


    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20, 
        [FromQuery] string? search = null, 
        [FromQuery] DateTime? fromDate = null, 
        [FromQuery] DateTime? toDate = null, 
        [FromQuery] bool includeLines = false,
        [FromQuery] OrderSource? source = null,
        [FromQuery] JournalEntryStatus? status = null,
        [FromQuery] string? entryNumber = null,
        [FromQuery] string? description = null,
        [FromQuery] string? types = null,
        [FromQuery] string? excludeTypes = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortDir = "desc",
        [FromQuery] int? branchId = null)
    {
        var q = _db.JournalEntries.AsNoTracking();
        if (includeLines) q = q.Include(e => e.Lines).ThenInclude(l => l.Account);

        if (!string.IsNullOrEmpty(search))
        {
            var isNumeric = int.TryParse(search, out var searchId);
            var isDecimal = decimal.TryParse(search, out var searchAmt);

            q = q.Where(r => r.EntryNumber.Contains(search) 
                           || (r.Description != null && r.Description.Contains(search)) 
                           || (r.Reference != null && r.Reference.Contains(search))
                           || (isNumeric && r.Id == searchId)
                           || (isDecimal && r.Lines.Any(l => l.Debit == searchAmt || l.Credit == searchAmt)));
        }
        
        if (fromDate.HasValue) q = q.Where(e => e.EntryDate >= fromDate.Value.Date.AddHours(TimeHelper.GetBusinessDayEndHour()));
        if (toDate.HasValue) q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1));
        if (source.HasValue) q = q.Where(e => e.CostCenter == source.Value);

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(e => e.Lines.Any(l => l.BranchId == isolatedBranchId.Value));
            }
        }
        else if (branchId.HasValue) 
        {
            q = q.Where(e => e.Lines.Any(l => l.BranchId == branchId.Value));
        }

        if (status.HasValue) q = q.Where(e => e.Status == status.Value);
        if (!string.IsNullOrEmpty(types))
        {
            var typeList = types.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).Cast<JournalEntryType>().ToList();
            if (typeList.Any()) q = q.Where(e => typeList.Contains(e.Type));
        }
        if (!string.IsNullOrEmpty(excludeTypes))
        {
            var excludeList = excludeTypes.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).Cast<JournalEntryType>().ToList();
            if (excludeList.Any()) q = q.Where(e => !excludeList.Contains(e.Type));
        }
        if (!string.IsNullOrEmpty(entryNumber)) q = q.Where(e => e.EntryNumber.Contains(entryNumber));
        if (!string.IsNullOrEmpty(description)) q = q.Where(e => (e.Description != null && e.Description.Contains(description)) || (e.Reference != null && e.Reference.Contains(description)));

        var total = await q.CountAsync();
        List<object> entries;

        var desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<JournalEntry> ordered;

        if (sortBy?.ToLower() == "createdat")
        {
            ordered = desc ? q.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                           : q.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id);
        }
        else if (sortBy?.ToLower() == "date")
        {
            ordered = desc ? q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
                           : q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id);
        }
        else
        {
            ordered = desc ? q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
                           : q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id);
        }

        if (includeLines)
        {
            var rawEntries = await ordered
                .Skip((page-1)*pageSize).Take(pageSize)
                .Select(e => new {
                    e.Id,
                    e.EntryNumber,
                    e.EntryDate,
                    e.Description,
                    e.Reference,
                    e.CreatedAt,
                    e.Status,
                    e.Type,
                    e.CostCenter,
                    BranchId = e.Lines.Select(l => l.BranchId).FirstOrDefault(),
                    LineCount = e.Lines.Count,
                    TotalAmount = e.Lines.Where(l => l.Debit > 0).Sum(l => (decimal?)l.Debit) ?? 0,
                    Lines = e.Lines.Select(l => new {
                        l.AccountId,
                        AccountCode = l.Account != null ? l.Account.Code : null,
                        l.Credit,
                        l.Debit,
                        AccountName = l.Account != null ? l.Account.NameAr : null,
                        l.CostCenter,
                        l.BranchId
                    }).ToList()
                })
                .ToListAsync();

            entries = rawEntries.Select(e => (object)new {
                e.Id,
                e.EntryNumber,
                e.EntryDate,
                e.Description,
                e.Reference,
                e.CreatedAt,
                Status = e.Status.ToString(),
                Type = e.Type.ToString(),
                CostCenter = (int?)e.CostCenter,
                BranchId = e.BranchId,
                CostCenterLabel = e.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (e.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
                e.LineCount,
                e.TotalAmount,
                Lines = (object)e.Lines.Select(l => new {
                    l.AccountId,
                    l.AccountCode,
                    l.Credit,
                    l.Debit,
                    l.AccountName,
                    CostCenter = (int?)l.CostCenter,
                    l.BranchId
                }).ToList()
            }).ToList();
        }
        else
        {
            var rawEntries = await ordered
                .Skip((page-1)*pageSize).Take(pageSize)
                .Select(e => new {
                    e.Id,
                    e.EntryNumber,
                    e.EntryDate,
                    e.Description,
                    e.Reference,
                    e.CreatedAt,
                    e.Status,
                    e.Type,
                    e.CostCenter,
                    BranchId = e.Lines.Select(l => l.BranchId).FirstOrDefault(),
                    LineCount = e.Lines.Count,
                    TotalAmount = e.Lines.Where(l => l.Debit > 0).Sum(l => (decimal?)l.Debit) ?? 0
                })
                .ToListAsync();

            entries = rawEntries.Select(e => (object)new {
                e.Id,
                e.EntryNumber,
                e.EntryDate,
                e.Description,
                e.Reference,
                e.CreatedAt,
                Status = e.Status.ToString(),
                Type = e.Type.ToString(),
                CostCenter = (int?)e.CostCenter,
                BranchId = e.BranchId,
                CostCenterLabel = e.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (e.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
                e.LineCount,
                e.TotalAmount,
                Lines = (object?)null
            }).ToList();
        }

        return Ok(new { items = entries, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var e = await _db.JournalEntries
            .Include(x => x.Lines).ThenInclude(l => l.Account)
            .Include(x => x.Lines).ThenInclude(l => l.Supplier)
            .Include(x => x.Lines).ThenInclude(l => l.Customer)
            .Include(x => x.Lines).ThenInclude(l => l.Employee)
            .FirstOrDefaultAsync(x => x.Id == id);
            
        if (e == null) return NotFound();

        return Ok(new JournalEntryDto(
            e.Id, e.EntryNumber, e.EntryDate, e.Type.ToString(), e.Status.ToString(),
            e.Reference, e.Description, e.TotalDebit, e.TotalCredit, e.IsBalanced, e.CreatedAt,
            e.Lines.Select(l => new JournalLineDto(
                l.Id, l.AccountId, l.Account?.Code ?? "", l.Account?.NameAr ?? "",
                l.Debit, l.Credit, l.Description, l.CustomerId, l.SupplierId, l.EmployeeId,
                l.Supplier?.Name ?? l.Customer?.FullName ?? l.Employee?.Name ?? null,
                (int?)l.CostCenter,
                l.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (l.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
                l.BranchId
            )).ToList(),
            e.AttachmentUrl, e.AttachmentPublicId, null, null, (int?)e.CostCenter,
            e.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (e.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
            e.Lines.FirstOrDefault()?.BranchId
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto)
    {
        try {
            var advancesAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1105");
            if (advancesAccount != null)
            {
                foreach (var line in dto.Lines)
                {
                    if (line.AccountId == advancesAccount.Id && line.Debit > 0 && !line.EmployeeId.HasValue)
                    {
                        return BadRequest("يجب اختيار الموظف عند استخدام حساب سلف الموظفين");
                    }
                }
            }

            var entry = await _accounting.PostManualEntryAsync(dto, User);
            try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollForVoucherAsync(entry.Id)); } catch { /* non-critical: don't fail the request if Hangfire is unavailable */ }

            // Log Audit
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            try { await _audit.LogChangeAsync<JournalEntry>("CreateJournalEntry", "JournalEntry", entry.Id.ToString(), null, entry, userId, userName); } catch { /* non-critical */ }

            return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
        } catch (InvalidOperationException ex) {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateJournalEntryDto dto)
    {
        try {
            var oldEntry = await _db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);
            var entry = await _accounting.UpdateManualEntryAsync(id, dto, User);
            try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollForVoucherAsync(entry.Id)); } catch { /* non-critical */ }

            // Log Audit
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            try { await _audit.LogChangeAsync<JournalEntry>("UpdateJournalEntry", "JournalEntry", entry.Id.ToString(), oldEntry, entry, userId, userName); } catch { /* non-critical */ }

            return Ok(entry);
        } catch (InvalidOperationException ex) {
            return BadRequest(new { message = ex.Message });
        } catch (KeyNotFoundException) {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string reason = "Manual Deletion")
    {
        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();

        bool isAdmin = User.IsInRole("SuperAdmin") || User.IsInRole("Admin");

        if (entry.Status == JournalEntryStatus.Reversed && !isAdmin)
        {
            return BadRequest(new { message = "لا يمكن حذف قيد تم عكسه بالفعل حفاظاً على سلامة الدورة المحاسبية." });
        }

        var isReversalParent = await _db.JournalEntries.AnyAsync(e => e.ReversalOfId == id);
        if (isReversalParent && !isAdmin)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه تم عكسه بالفعل ويوجد قيد عكسي مرتبط به." });
        }

        var isLinkedToPaymentVoucher = await _db.PaymentVouchers.AnyAsync(v => v.JournalEntryId == id);
        if (isLinkedToPaymentVoucher)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بسند صرف. يرجى حذف سند الصرف نفسه أولاً." });
        }

        var isLinkedToReceiptVoucher = await _db.ReceiptVouchers.AnyAsync(v => v.JournalEntryId == id);
        if (isLinkedToReceiptVoucher)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بسند قبض. يرجى حذف سند القبض نفسه أولاً." });
        }

        var isLinkedToEmployeeAdvance = await _db.EmployeeAdvances.AnyAsync(a => a.JournalEntryId == id);
        if (isLinkedToEmployeeAdvance)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بسلفة موظف. يرجى حذف السلفة أولاً." });
        }

        var isLinkedToDepreciation = await _db.AssetDepreciations.AnyAsync(d => d.JournalEntryId == id);
        if (isLinkedToDepreciation)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بإهلاك أصل ثابت. يرجى حذف الإهلاك أولاً." });
        }

        var isLinkedToDisposal = await _db.AssetDisposals.AnyAsync(d => d.JournalEntryId == id);
        if (isLinkedToDisposal)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط باستبعاد أصل ثابت. يرجى حذف عملية الاستبعاد أولاً." });
        }

        var isLinkedToInventoryAudit = await _db.InventoryAudits.AnyAsync(a => a.JournalEntryId == id);
        if (isLinkedToInventoryAudit)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بجلسة جرد مخزني." });
        }

        var isLinkedToPayrollRun = await _db.PayrollRuns.AnyAsync(r => r.JournalEntryId == id);
        if (isLinkedToPayrollRun)
        {
            return BadRequest(new { message = "لا يمكن حذف هذا القيد لأنه مرتبط بمسير رواتب." });
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        if (entry.Status == JournalEntryStatus.Posted && !isAdmin)
        {
            var oldEntry = await _db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);
        
            try {
                await _accounting.ReverseEntryAsync(id, reason);
                var reversedEntry = await _db.JournalEntries.AsNoTracking().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id);

                // Log Audit Reversal
                await _audit.LogChangeAsync<JournalEntry>("ReverseJournalEntry", "JournalEntry", id.ToString(), oldEntry, reversedEntry, userId, userName);

                return Ok(new { message = _t.Get("Accounting.ReverseSuccessMessage") });
            } catch (Exception ex) {
                return BadRequest(new { message = ex.Message });
            }
        }

        if (isAdmin)
        {
            var childReversals = await _db.JournalEntries
                .Include(j => j.Lines)
                .Where(j => j.ReversalOfId == id)
                .ToListAsync();
            if (childReversals.Any())
            {
                foreach (var child in childReversals)
                {
                    _db.JournalLines.RemoveRange(child.Lines);
                }
                _db.JournalEntries.RemoveRange(childReversals);
            }

            if (entry.ReversalOfId.HasValue)
            {
                var parent = await _db.JournalEntries.FindAsync(entry.ReversalOfId.Value);
                if (parent != null && parent.Status == JournalEntryStatus.Reversed)
                {
                    parent.Status = JournalEntryStatus.Posted;
                    parent.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
        }

        // Extract runs to sync before the entry is deleted
        var textToSearch = $"{entry.Reference} {entry.Description} {string.Join(" ", entry.Lines.Select(l => l.Description))}".ToLower();
        var payrollRuns = await _db.PayrollRuns.Where(r => r.Status != PayrollStatus.Draft).ToListAsync();
        var runsToSync = payrollRuns.Where(r => textToSearch.Contains(r.PayrollNumber.ToLower())).ToList();

        // Log Audit Deletion
        await _audit.LogChangeAsync<JournalEntry>("DeleteJournalEntry", "JournalEntry", id.ToString(), entry, null, userId, userName);

        _db.JournalLines.RemoveRange(entry.Lines);
        _db.JournalEntries.Remove(entry);
        await _db.SaveChangesAsync();
        try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync()); } catch { /* non-critical */ }

        foreach (var run in runsToSync)
        {
            try { Hangfire.BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollRunPaymentsAsync(run.Id)); } catch { /* non-critical */ }
        }

        return NoContent();
    }
}
