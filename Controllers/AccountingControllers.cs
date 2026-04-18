// ============================================================
// Controllers/AccountingControllers.cs
// تم دمج ملفات المحاسبة واستعادة المحتوى مع تصحيح الـ Double Counting
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
using Sportive.API.Utils;
using ClosedXML.Excel;

namespace Sportive.API.Controllers;

// 1. ACCOUNTS (دليل الحسابات)
[ApiController, Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    public AccountsController(AppDbContext db, IAccountingService accounting) {
        _db = db;
        _accounting = accounting;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool onlyActive   = false,
        [FromQuery] bool? isLeaf      = null,
        [FromQuery] bool? allowPosting = null)
    {
        var q = _db.Accounts.AsQueryable();
        if (onlyActive) q = q.Where(a => a.IsActive);
        if (isLeaf.HasValue) q = q.Where(a => a.IsLeaf == isLeaf.Value);
        if (allowPosting.HasValue) q = q.Where(a => a.AllowPosting == allowPosting.Value);
        
        var accounts = await q.OrderBy(a => a.Code).ToListAsync();
        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account == null) return NotFound();
        return Ok(account);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountDto dto)
    {
        if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code))
            return BadRequest("كود الحساب موجود مسبقاً.");

        if (dto.OpeningBalance != 0 && !dto.IsLeaf)
            return BadRequest("لا يمكن إضافة رصيد افتتاحي لحساب أب. الأرصدة الافتتاحية تضاف للحسابات النهائية فقط.");

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

        if (dto.OpeningBalance != 0 && !account.IsLeaf)
            return BadRequest("لا يمكن إضافة رصيد افتتاحي لحساب أب.");

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

        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id))
            return BadRequest("لا يمكن حذف حساب يحتوي على حركات مالية.");

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("import-opening-balances")]
    public async Task<IActionResult> ImportOpeningBalances(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "لم يتم رفع ملف" });

        var successCount = 0;
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var allAccounts = await _db.Accounts.ToListAsync();

            for (int r = 2; r <= lastRow; r++)
            {
                var code = ws.Cell(r, 1).GetString().Trim();
                if (string.IsNullOrEmpty(code)) continue;

                var balStr = ws.Cell(r, 2).GetString().Trim();
                if (!decimal.TryParse(balStr, out var balance))
                {
                    errors.Add($"سطر {r}: الرصيد غير صحيح للحساب '{code}'");
                    continue;
                }

                var account = allAccounts.FirstOrDefault(a => a.Code == code);
                if (account == null)
                {
                    errors.Add($"سطر {r}: كود الحساب '{code}' غير موجود");
                    continue;
                }

                if (!account.IsLeaf)
                {
                    errors.Add($"سطر {r}: الحساب '{code}' ليس حساباً فرعياً (Leaf). لا يمكن إضافة رصيد افتتاحي إلا للحسابات الفرعية.");
                    continue;
                }

                account.OpeningBalance = balance;
                account.UpdatedAt = TimeHelper.GetEgyptTime();
                successCount++;
            }

            await _db.SaveChangesAsync();
            await _accounting.SyncEntityBalancesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"خطأ في المعالجة: {ex.Message}" });
        }

        return Ok(new { success = true, successCount, errors });
    }

    [HttpGet("mapping-registry")]
    public IActionResult GetMappingRegistry()
    {
        var registry = new List<object>
        {
            // --- Sales & POS ---
            new { key = MappingKeys.Sales, description = "حساب إيرادات المبيعات (الرئيسي)" },
            new { key = MappingKeys.SalesReturn, description = "حساب مردودات المبيعات" },
            new { key = MappingKeys.SalesDiscount, description = "حساب مسموحات وخصومات المبيعات" },
            new { key = MappingKeys.Customer, description = "حساب وسيط العملاء (المدينون) - افتراضي" },
            
            // --- Cash & Payments (POS) ---
            new { key = MappingKeys.PosCash, description = "خزينة الكاشير (نقدي)" },
            new { key = MappingKeys.PosBank, description = "حساب البنك / فيزا (POS)" },
            new { key = MappingKeys.PosVodafone, description = "فودافون كاش (POS)" },
            new { key = MappingKeys.PosInstaPay, description = "انستاباي (POS)" },
            
            // --- Cash & Payments (Website) ---
            new { key = MappingKeys.WebCash, description = "تحصيلات الموقع (نقدي عند الاستلام)" },
            new { key = MappingKeys.WebVodafone, description = "فودافون كاش (الموقع)" },
            new { key = MappingKeys.WebInstaPay, description = "انستاباي (الموقع)" },
            
            // --- Purchases & Inventory ---
            new { key = MappingKeys.Inventory, description = "حساب المخزون (الأصول المتداولة)" },
            new { key = MappingKeys.InventoryVariance, description = "حساب فروقات جرد المخزون (خسارة/ربح)" },
            new { key = MappingKeys.Supplier, description = "حساب الموردين (الدائنون) - افتراضي" },
            new { key = MappingKeys.PurchaseDiscount, description = "حساب الخصم المكتسب (مشتريات)" },
            new { key = MappingKeys.VatInput, description = "ضريبة المدخلات (مشتريات)" },
            new { key = MappingKeys.VatOutput, description = "ضريبة المخرجات (مبيعات)" },
            new { key = MappingKeys.DeliveryRevenue, description = "حساب إيرادات التوصيل" },
            new { key = MappingKeys.PaymentVoucherCash, description = "الخزينة الافتراضية لسندات الصرف للموردين" },

            // --- HR & Payroll ---
            new { key = MappingKeys.SalaryExpense, description = "حساب مصاريف الرواتب والأجور" },
            new { key = MappingKeys.SalariesPayable, description = "حساب الرواتب المستحقة" },
            new { key = MappingKeys.EmployeeAdvances, description = "حساب سلف الموظفين" },
            new { key = MappingKeys.EmployeeBonuses, description = "حساب مكافآت الموظفين" },
            new { key = MappingKeys.EmployeeDeductions, description = "حساب جزاءات وخصومات الموظفين (إيراد)" },

            // --- Fixed Assets ---
            new { key = MappingKeys.DepreciationExpense, description = "حساب مصروف الإهلاك" },
            new { key = MappingKeys.AccumulatedDepreciation, description = "حساب مجمع الإهلاك (أصول ثروات)" },
        };
        return Ok(registry);
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
                mapping.UpdatedAt = TimeHelper.GetEgyptTime();
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
            var allAccounts = await _db.Accounts.ToListAsync();
            foreach (var a in allAccounts)
            {
                a.Level = 1;
                a.IsLeaf = true;
            }

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

    [HttpGet("sync-balances"), HttpPost("sync-balances")]
    public async Task<IActionResult> SyncBalances()
    {
        try
        {
            await _accounting.SyncEntityBalancesAsync();
            return Ok(new { success = true, message = "تمت إعادة مزامنة أرصدة الموردين والعملاء بنجاح." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree()
    {
        var all = await _db.Accounts
            .Include(a => a.Lines)
            .Include(a => a.Parent)
            .OrderBy(a => a.Code)
            .ToListAsync();

        var balances = all.ToDictionary(a => a.Id, a => a.Lines.Sum(l => l.Debit - l.Credit));

        List<AccountDto> BuildTree(int? parentId)
        {
            return all
                .Where(a => a.ParentId == parentId)
                .Select(a => {
                    var children = BuildTree(a.Id);
                    var netLinesAmount = balances.GetValueOrDefault(a.Id, 0);
                    
                    // تحويل الصافي بناءً على طبيعة الحساب
                    var directCurrentBal = a.Nature == AccountNature.Debit ? netLinesAmount : -netLinesAmount;
                    
                    // 🚨 ADVANCED FIX:
                    // الحساب الأب توازنه = (مجموع أرصدة أبنائه الحالية) + (حركاته المباشرة).
                    // ممارسة فضلى: لا يوجد OpeningBalance للأب، هو فقط مرآة لأبنائه.
                    
                    decimal totalCurrentBalance = directCurrentBal;
                    
                    if (children.Any()) 
                    {
                        totalCurrentBalance += children.Sum(c => c.CurrentBalance);
                    }
                    else 
                    {
                        // فقط الحسابات النهائية (Leaf) تأخذ رصيداً افتتاحياً
                        totalCurrentBalance += a.OpeningBalance;
                    }

                    return new AccountDto(
                        a.Id, a.Code, a.NameAr, a.NameEn, a.Description,
                        a.Type.ToString(), a.Nature.ToString(), a.ParentId,
                        a.Parent?.NameAr, a.Level, a.IsLeaf, a.AllowPosting,
                        a.IsActive, a.IsSystem, a.OpeningBalance,
                        totalCurrentBalance, children
                    );
                })
                .ToList();
        }

        var tree = BuildTree(null);
        return Ok(tree);
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
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null, [FromQuery] bool includeLines = false)
    {
        var q = _db.JournalEntries.AsNoTracking();
        if (includeLines) q = q.Include(e => e.Lines).ThenInclude(l => l.Account);

        if (!string.IsNullOrEmpty(search))
            q = q.Where(e => e.EntryNumber.Contains(search) || (e.Description != null && e.Description.Contains(search)) || (e.Reference != null && e.Reference.Contains(search)));
        
        if (fromDate.HasValue) q = q.Where(e => e.EntryDate >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(e => e.EntryDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var total = await q.CountAsync();
        var entries = await q.OrderByDescending(e => e.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(e => new {
                e.Id, e.EntryNumber, e.EntryDate, e.Description, e.Reference,
                Status = e.Status.ToString(),
                Type = e.Type.ToString(),
                LineCount = includeLines ? e.Lines.Count : _db.JournalLines.Count(l => l.JournalEntryId == e.Id),
                TotalAmount = includeLines ? e.Lines.Where(l => l.Debit > 0).Sum(l => l.Debit) : (_db.JournalLines.AsNoTracking().Where(l => l.JournalEntryId == e.Id && l.Debit > 0).Sum(l => (decimal?)l.Debit) ?? 0),
                Lines = includeLines ? (object)e.Lines.Select(l => new { l.AccountId, l.Credit, l.Debit, AccountName = l.Account != null ? l.Account.NameAr : null }).ToList() : null
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

        return Ok(new JournalEntryDto(
            e.Id, e.EntryNumber, e.EntryDate, e.Type.ToString(), e.Status.ToString(),
            e.Reference, e.Description, e.TotalDebit, e.TotalCredit, e.IsBalanced, e.CreatedAt,
            e.Lines.Select(l => new JournalLineDto(
                l.Id, l.AccountId, l.Account?.Code ?? "", l.Account?.NameAr ?? "",
                l.Debit, l.Credit, l.Description, l.CustomerId, l.SupplierId,
                l.Supplier?.Name ?? l.Customer?.FullName ?? null
            )).ToList(),
            e.AttachmentUrl, e.AttachmentPublicId, null, null
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJournalEntryDto dto)
    {
        var entry = await _accounting.PostManualEntryAsync(dto, User);
        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateJournalEntryDto dto)
    {
        try {
            var entry = await _accounting.UpdateManualEntryAsync(id, dto, User);
            return Ok(entry);
        } catch (InvalidOperationException ex) {
            return BadRequest(new { message = ex.Message });
        } catch (KeyNotFoundException) {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string reason = "حذف يدوي")
    {
        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Id == id);
        if (entry == null) return NotFound();

        if (entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
        {
            await _accounting.ReverseEntryAsync(id, reason);
            return Ok(new { message = "تم عكس القيد بنجاح بدلاً من الحذف." });
        }

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
    private readonly SequenceService _seq;
    public ReceiptVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq) {
        _accounting = accounting;
        _db = db;
        _seq = seq;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        var q = _db.ReceiptVouchers.AsQueryable();
        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(v => v.VoucherDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));
        
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
                v.CashAccountId,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                FromAccountName = v.FromAccount != null ? v.FromAccount.NameAr : null,
                EntityName = v.Customer != null ? v.Customer.FullName : null
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrderId(int orderId)
    {
        var items = await _db.ReceiptVouchers
            .Where(v => v.OrderId == orderId)
            .Include(v => v.CashAccount)
            .Include(v => v.FromAccount)
            .Include(v => v.Customer)
            .OrderByDescending(v => v.VoucherDate)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
                v.CashAccountId,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                FromAccountName = v.FromAccount != null ? v.FromAccount.NameAr : null,
                EntityName = v.Customer != null ? v.Customer.FullName : null
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var v = await _db.ReceiptVouchers
            .Include(v => v.CashAccount).Include(v => v.FromAccount).Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptVoucherDto dto)
    {
        var vNo = await _seq.NextAsync("RV", async (db, pattern) => {
            var max = await db.ReceiptVouchers.Where(v => EF.Functions.Like(v.VoucherNumber, pattern)).Select(v => v.VoucherNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var voucher = new ReceiptVoucher {
            VoucherNumber = vNo, VoucherDate = dto.VoucherDate, Amount = dto.Amount, CashAccountId = dto.CashAccountId,
            FromAccountId = dto.FromAccountId, CustomerId = dto.CustomerId, PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference, Description = dto.Description, AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CostCenter = dto.CostCenter,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value, CreatedAt = TimeHelper.GetEgyptTime(), OrderId = dto.OrderId
        };

        _db.ReceiptVouchers.Add(voucher);

        if (dto.OrderId.HasValue) {
            var order = await _db.Orders.FindAsync(dto.OrderId.Value);
            if (order != null) {
                var remaining = order.TotalAmount - order.PaidAmount;
                if (dto.Amount > remaining + 0.01m) return BadRequest($"المبلغ أكبر من المديونية المتبقية ({remaining})");
                order.PaidAmount += dto.Amount;
                order.PaymentStatus = order.PaidAmount >= order.TotalAmount - 0.01m ? PaymentStatus.Paid : PaymentStatus.Pending;
                order.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        await _db.SaveChangesAsync();
        await _accounting.PostReceiptVoucherAsync(voucher, dto.OrderId);
        await _accounting.SyncEntityBalancesAsync();
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReceiptVoucherDto dto)
    {
        var voucher = await _db.ReceiptVouchers.Include(v => v.FromAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن تعديل سند مرحّل إلا من خلال الإدارة.");

        voucher.VoucherDate = dto.VoucherDate;
        voucher.Amount = dto.Amount;
        voucher.CashAccountId = dto.CashAccountId;
        voucher.FromAccountId = dto.FromAccountId;
        voucher.CustomerId = dto.CustomerId;
        voucher.PaymentMethod = dto.PaymentMethod;
        voucher.Reference = dto.Reference;
        voucher.Description = dto.Description;
        voucher.AttachmentUrl = dto.AttachmentUrl;
        voucher.AttachmentPublicId = dto.AttachmentPublicId;
        voucher.UpdatedAt = TimeHelper.GetEgyptTime();

        if (entry != null) {
            entry.EntryDate = voucher.VoucherDate; entry.Description = voucher.Description; entry.UpdatedAt = TimeHelper.GetEgyptTime();
            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Add(new JournalLine { AccountId = voucher.CashAccountId, Debit = voucher.Amount, Credit = 0, Description = $"[تحديث] {voucher.VoucherNumber}" });
            entry.Lines.Add(new JournalLine { AccountId = voucher.FromAccountId, Debit = 0, Credit = voucher.Amount, Description = $"من ح/ {voucher.FromAccount?.NameAr}" });
        }

        await _db.SaveChangesAsync();
        await _accounting.SyncEntityBalancesAsync();
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.ReceiptVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin")) {
            await _accounting.ReverseEntryAsync(entry.Id, "إلغاء سند قبض");
            return Ok(new { message = "تم إلغاء السند وعكس القيد." });
        }

        _db.ReceiptVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);
        await _db.SaveChangesAsync();
        await _accounting.SyncEntityBalancesAsync();
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
    private readonly SequenceService _seq;
    public PaymentVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq) {
        _accounting = accounting;
        _db = db;
        _seq = seq;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        var q = _db.PaymentVouchers.AsQueryable();
        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(v => v.VoucherDate <= toDate.Value.Date.AddDays(1).AddTicks(-1));

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(v => v.VoucherDate).Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
                v.CashAccountId, v.ToAccountId,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                ToAccountName = v.ToAccount != null ? v.ToAccount.NameAr : null,
                EntityName = v.Supplier != null ? v.Supplier.Name : null
            }).ToListAsync();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var v = await _db.PaymentVouchers
            .Include(v => v.CashAccount).Include(v => v.ToAccount).Include(v => v.Supplier)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentVoucherDto dto)
    {
        var vNo = await _seq.NextAsync("PV", async (db, pattern) => {
            var max = await db.PaymentVouchers.Where(v => EF.Functions.Like(v.VoucherNumber, pattern)).Select(v => v.VoucherNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0).DefaultIfEmpty(0).Max();
        });

        var voucher = new PaymentVoucher {
            VoucherNumber = vNo, VoucherDate = dto.VoucherDate, Amount = dto.Amount, CashAccountId = dto.CashAccountId,
            ToAccountId = dto.ToAccountId, SupplierId = dto.SupplierId, PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference, Description = dto.Description, AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CostCenter = dto.CostCenter,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value, CreatedAt = TimeHelper.GetEgyptTime(), PurchaseInvoiceId = dto.PurchaseInvoiceId
        };

        if (dto.PurchaseInvoiceId.HasValue) {
            var invoice = await _db.PurchaseInvoices.FindAsync(dto.PurchaseInvoiceId.Value);
            if (invoice != null) {
                var remaining = invoice.TotalAmount - invoice.PaidAmount;
                if (dto.Amount > remaining + 0.1m) return BadRequest($"المبلغ أكبر من مديونية الفاتورة ({remaining})");
                invoice.PaidAmount += dto.Amount;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
            }
        }

        _db.PaymentVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        await _accounting.PostPaymentVoucherAsync(voucher);
        await _accounting.SyncEntityBalancesAsync();
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePaymentVoucherDto dto)
    {
        var voucher = await _db.PaymentVouchers.Include(v => v.CashAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن تعديل سند مرحّل.");

        voucher.VoucherDate = dto.VoucherDate; voucher.Amount = dto.Amount; voucher.CashAccountId = dto.CashAccountId;
        voucher.ToAccountId = dto.ToAccountId; voucher.SupplierId = dto.SupplierId; voucher.Description = dto.Description;
        voucher.PurchaseInvoiceId = dto.PurchaseInvoiceId; voucher.UpdatedAt = TimeHelper.GetEgyptTime();

        if (entry != null) {
            entry.EntryDate = voucher.VoucherDate; entry.Description = voucher.Description;
            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Add(new JournalLine { AccountId = voucher.ToAccountId, Debit = voucher.Amount, Credit = 0, Description = $"[تحديث] {voucher.VoucherNumber}" });
            entry.Lines.Add(new JournalLine { AccountId = voucher.CashAccountId, Debit = 0, Credit = voucher.Amount, Description = $"من ح/ {voucher.CashAccount?.NameAr}" });
        }

        await _db.SaveChangesAsync();
        await _accounting.SyncEntityBalancesAsync();
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.PaymentVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin")) {
            await _accounting.ReverseEntryAsync(entry.Id, "إلغاء سند صرف");
            return Ok(new { message = "تم إلغاء السند وعكس القيد." });
        }

        _db.PaymentVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);
        await _db.SaveChangesAsync();
        await _accounting.SyncEntityBalancesAsync();
        return NoContent();
    }
}
