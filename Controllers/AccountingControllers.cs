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
using Sportive.API.Utils;

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
        var q = _db.Accounts.AsQueryable();
        if (onlyActive) q = q.Where(a => a.IsActive);
        
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
        // التحقق من تكرار الكود
        if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code))
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
        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id))
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

    [HttpGet("sync-balances"), HttpPost("sync-balances")]
    public async Task<IActionResult> SyncBalances([FromServices] IAccountingService accounting)
    {
        try
        {
            await accounting.SyncEntityBalancesAsync();
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

        // Calculate balances for all accounts
        var balances = all.ToDictionary(a => a.Id, a => a.Lines.Sum(l => l.Debit - l.Credit));

        // Use a recursive function to build the tree
        List<AccountDto> BuildTree(int? parentId)
        {
            return all
                .Where(a => a.ParentId == parentId)
                .Select(a => {
                    var children = BuildTree(a.Id);
                    var net = balances.GetValueOrDefault(a.Id, 0);
                    var currentBal = a.Nature == AccountNature.Debit ? net : -net;
                    
                    // Add opening balance
                    currentBal += a.OpeningBalance;

                    // For non-leaf accounts, balance is sum of children balances + its own
                    if (children.Any()) 
                    {
                        currentBal += children.Sum(c => c.CurrentBalance);
                    }

                    return new AccountDto(
                        a.Id, a.Code, a.NameAr, a.NameEn, a.Description,
                        a.Type.ToString(), a.Nature.ToString(), a.ParentId,
                        a.Parent?.NameAr, a.Level, a.IsLeaf, a.AllowPosting,
                        a.IsActive, a.IsSystem, a.OpeningBalance,
                        currentBal, children
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
                Id = e.Id,
                EntryNumber = e.EntryNumber,
                EntryDate = e.EntryDate,
                Description = e.Description,
                Reference = e.Reference,
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

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateJournalEntryDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        try 
        {
            var entry = await _accounting.UpdateManualEntryAsync(id, dto, userId);
            return Ok(entry);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
    private readonly SequenceService _seq;
    public ReceiptVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq)
    {
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

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var v = await _db.ReceiptVouchers
            .Include(v => v.CashAccount)
            .Include(v => v.FromAccount)
            .Include(v => v.Customer)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReceiptVoucherDto dto)
    {
        var vNo = await _seq.NextAsync("RV", async (db, pattern) =>
        {
            var max = await db.ReceiptVouchers
                .Where(v => EF.Functions.Like(v.VoucherNumber, pattern))
                .Select(v => v.VoucherNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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
            CreatedAt = TimeHelper.GetEgyptTime(),
            OrderId = dto.OrderId
        };

        _db.ReceiptVouchers.Add(voucher);

        // 🏛️ SYNC: Update Customer Balance if applicable
        if (voucher.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FindAsync(voucher.CustomerId.Value);
            if (customer != null)
            {
                customer.TotalPaid += voucher.Amount;
            }
        }

        // 🏛️ PRO-ACCOUNTING: Link to Order & Update Status if provided
        if (dto.OrderId.HasValue)
        {
            var order = await _db.Orders.FindAsync(dto.OrderId.Value);
            if (order != null)
            {
                // Check for double collection
                var currentPaid = order.PaidAmount;
                var remaining = order.TotalAmount - currentPaid;
                
                if (dto.Amount > remaining + 0.01m) // Allow 0.01 tolerance for precision
                {
                   return BadRequest($"لا يمكن تحصيل مبلغ ({dto.Amount}) وهو أكبر من المديونية المتبقية ({remaining}) للطلب رقم {order.OrderNumber}.");
                }

                // Update Order Payment calculations
                order.PaidAmount += dto.Amount;
                order.PaymentStatus = order.PaidAmount >= order.TotalAmount - 0.01m ? PaymentStatus.Paid : PaymentStatus.Pending;
                order.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostReceiptVoucherAsync(voucher, dto.OrderId);
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReceiptVoucherDto dto)
    {
        var voucher = await _db.ReceiptVouchers.Include(v => v.FromAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        // Check if there's a posted journal entry
        var entry = await _db.JournalEntries.Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن تعديل سند مرحّل إلا من خلال الإدارة.");

        // 1. Update Voucher Data
        var oldAmount = voucher.Amount;
        var oldCustomerId = voucher.CustomerId;

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

        // 🏛️ SYNC: Adjust Customer Balances
        if (oldCustomerId.HasValue)
        {
            var oldCust = await _db.Customers.FindAsync(oldCustomerId.Value);
            if (oldCust != null) oldCust.TotalPaid -= oldAmount;
        }
        if (voucher.CustomerId.HasValue)
        {
            var newCust = await _db.Customers.FindAsync(voucher.CustomerId.Value);
            if (newCust != null) newCust.TotalPaid += voucher.Amount;
        }

        // 2. Synchronize Journal Entry
        if (entry != null)
        {
            entry.EntryDate = voucher.VoucherDate;
            entry.Description = voucher.Description;
            entry.UpdatedAt = TimeHelper.GetEgyptTime();

            // Rebuild lines to reflect new accounts/amounts
            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Add(new JournalLine { AccountId = voucher.CashAccountId, Debit = voucher.Amount, Credit = 0, Description = $"[تحديث] {voucher.VoucherNumber} - {voucher.Description}" });
            entry.Lines.Add(new JournalLine { AccountId = voucher.FromAccountId, Debit = 0, Credit = voucher.Amount, Description = $"[تحديث] من ح/ {voucher.FromAccount?.NameAr} - {voucher.VoucherNumber}" });
        }

        await _db.SaveChangesAsync();
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

        // 🏛️ SYNC: Revert Customer Balance
        if (voucher.CustomerId.HasValue)
        {
            var cust = await _db.Customers.FindAsync(voucher.CustomerId.Value);
            if (cust != null) cust.TotalPaid -= voucher.Amount;
        }

        _db.ReceiptVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);

        await _db.SaveChangesAsync();
        return NoContent();
    }
    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrder(int orderId)
    {
        var vouchers = await _db.ReceiptVouchers
            .Where(v => v.OrderId == orderId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new {
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, 
                v.Description, cashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : "حساب غير معرف"
            })
            .ToListAsync();
        return Ok(vouchers);
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
    public PaymentVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq)
    {
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
        var items = await q.OrderByDescending(v => v.VoucherDate)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
                v.CashAccountId,
                ToAccountId = v.ToAccountId,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                ToAccountName = v.ToAccount != null ? v.ToAccount.NameAr : null,
                EntityName = v.Supplier != null ? v.Supplier.Name : null
            })
            .ToListAsync();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var v = await _db.PaymentVouchers
            .Include(v => v.CashAccount)
            .Include(v => v.ToAccount)
            .Include(v => v.Supplier)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePaymentVoucherDto dto)
    {
        var vNo = await _seq.NextAsync("PV", async (db, pattern) =>
        {
            var max = await db.PaymentVouchers
                .Where(v => EF.Functions.Like(v.VoucherNumber, pattern))
                .Select(v => v.VoucherNumber)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

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
            CreatedAt = TimeHelper.GetEgyptTime(),
            PurchaseInvoiceId = dto.PurchaseInvoiceId // Ensure this is mapped if in DTO
        };

        // 🏛️ PRO-ACCOUNTING: Link to Purchase Invoice & Prevent Overpayment
        if (dto.PurchaseInvoiceId.HasValue)
        {
            var invoice = await _db.PurchaseInvoices.FindAsync(dto.PurchaseInvoiceId.Value);
            if (invoice != null)
            {
                var remaining = invoice.TotalAmount - invoice.PaidAmount;
                if (dto.Amount > remaining + 0.1m) // Small buffer for decimals
                {
                    return BadRequest($"لا يمكن صرف مبلغ ({dto.Amount}) وهو أكبر من المديونية المتبقية للفاتورة ({remaining}).");
                }
                invoice.PaidAmount += dto.Amount;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
            }
        }

        _db.PaymentVouchers.Add(voucher);

        // 🏛️ SYNC: Update Supplier Balance
        if (voucher.SupplierId.HasValue)
        {
            var supplier = await _db.Suppliers.FindAsync(voucher.SupplierId.Value);
            if (supplier != null)
            {
                supplier.TotalPaid += voucher.Amount;
            }
        }

        await _db.SaveChangesAsync();

        // ترحيل تلقائي للمحاسبة
        await _accounting.PostPaymentVoucherAsync(voucher);
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePaymentVoucherDto dto)
    {
        var voucher = await _db.PaymentVouchers.Include(v => v.CashAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && !User.IsInRole("Admin"))
            return BadRequest("لا يمكن تعديل سند مرحّل إلا من خلال الإدارة.");

        // 1. Update Voucher
        var oldAmount = voucher.Amount;
        var oldSupplierId = voucher.SupplierId;

        voucher.VoucherDate = dto.VoucherDate;
        voucher.Amount = dto.Amount;
        voucher.CashAccountId = dto.CashAccountId;
        voucher.ToAccountId = dto.ToAccountId;
        voucher.SupplierId = dto.SupplierId;
        voucher.PaymentMethod = dto.PaymentMethod;
        voucher.Reference = dto.Reference;
        voucher.Description = dto.Description;
        voucher.AttachmentUrl = dto.AttachmentUrl;
        voucher.AttachmentPublicId = dto.AttachmentPublicId;
        voucher.AttachmentPublicId = dto.AttachmentPublicId;
        voucher.UpdatedAt = TimeHelper.GetEgyptTime();

        // 🏛️ SYNC: Revert old Purchase Invoice if any
        if (voucher.PurchaseInvoiceId.HasValue && oldAmount > 0)
        {
            var oldInv = await _db.PurchaseInvoices.FindAsync(voucher.PurchaseInvoiceId.Value);
            if (oldInv != null)
            {
                oldInv.PaidAmount -= oldAmount;
                oldInv.Status = oldInv.PaidAmount <= 0 ? PurchaseInvoiceStatus.Received : PurchaseInvoiceStatus.PartPaid;
            }
        }

        // 🏛️ SYNC: Apply new Purchase Invoice if any
        voucher.PurchaseInvoiceId = dto.PurchaseInvoiceId;
        if (voucher.PurchaseInvoiceId.HasValue)
        {
            var newInv = await _db.PurchaseInvoices.FindAsync(voucher.PurchaseInvoiceId.Value);
            if (newInv != null)
            {
                newInv.PaidAmount += voucher.Amount;
                newInv.Status = newInv.PaidAmount >= newInv.TotalAmount - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
            }
        }

        // 🏛️ SYNC: Adjust Supplier Balances
        if (oldSupplierId.HasValue)
        {
            var oldSupp = await _db.Suppliers.FindAsync(oldSupplierId.Value);
            if (oldSupp != null) oldSupp.TotalPaid -= oldAmount;
        }
        if (voucher.SupplierId.HasValue)
        {
            var newSupp = await _db.Suppliers.FindAsync(voucher.SupplierId.Value);
            if (newSupp != null) newSupp.TotalPaid += voucher.Amount;
        }

        // 2. Sync Ledger
        if (entry != null)
        {
            entry.EntryDate = voucher.VoucherDate;
            entry.Description = voucher.Description;
            entry.UpdatedAt = TimeHelper.GetEgyptTime();

            _db.JournalLines.RemoveRange(entry.Lines);
            entry.Lines.Add(new JournalLine { AccountId = voucher.ToAccountId, Debit = voucher.Amount, Credit = 0, Description = $"[تحديث] {voucher.VoucherNumber} - {voucher.Description}" });
            entry.Lines.Add(new JournalLine { AccountId = voucher.CashAccountId, Debit = 0, Credit = voucher.Amount, Description = $"[تحديث] من ح/ {voucher.CashAccount?.NameAr} - {voucher.VoucherNumber}" });
        }

        await _db.SaveChangesAsync();
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

        // 🏛️ SYNC: Revert Supplier Balance
        if (voucher.SupplierId.HasValue)
        {
            var supp = await _db.Suppliers.FindAsync(voucher.SupplierId.Value);
            if (supp != null) supp.TotalPaid -= voucher.Amount;
        }

        // 🏛️ SYNC: Revert Purchase Invoice
        if (voucher.PurchaseInvoiceId.HasValue)
        {
            var inv = await _db.PurchaseInvoices.FindAsync(voucher.PurchaseInvoiceId.Value);
            if (inv != null)
            {
                inv.PaidAmount -= voucher.Amount;
                inv.Status = inv.PaidAmount <= 0 ? PurchaseInvoiceStatus.Received : PurchaseInvoiceStatus.PartPaid;
            }
        }

        _db.PaymentVouchers.Remove(voucher);
        if (entry != null) _db.JournalEntries.Remove(entry);

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
