using Sportive.API.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.DTOs;
using System.Security.Claims;
using Sportive.API.Utils;
using Hangfire;

namespace Sportive.API.Controllers;

[ApiController, Route("api/[controller]")]
[RequirePermission(ModuleKeys.AccountingMain + "," + ModuleKeys.Pos + "," + ModuleKeys.HrAdvances + "," + ModuleKeys.HrPayroll)]
public class PaymentVouchersController : ControllerBase
{
    private readonly ITranslator _t;
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly IPdfService _pdf;
    private readonly AccountingCoreService _core;
    public PaymentVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq, IPdfService pdf, ITranslator t, AccountingCoreService core) {
        _accounting = accounting;
        _db = db;
        _seq = seq;
        _pdf = pdf;
        _t = t;
        _core = core;
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var voucher = await _db.PaymentVouchers
            .Include(v => v.CashAccount)
            .Include(v => v.ToAccount)
            .Include(v => v.Supplier)
            .Include(v => v.Employee)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (voucher == null) return NotFound();

        var pdfBytes = await _pdf.GenerateVoucherPdfAsync(null, voucher);
        return File(pdfBytes, "application/pdf", $"Payment-{voucher.VoucherNumber}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20, 
        [FromQuery] DateTime? fromDate = null, 
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] bool? onlyEmployees = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] int? employeeId = null)
    {
        var q = _db.PaymentVouchers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var isNumeric = decimal.TryParse(search, out var searchAmt);
            q = q.Where(v => v.VoucherNumber.Contains(search)
                          || (v.Description != null && v.Description.Contains(search))
                          || (v.Reference != null && v.Reference.Contains(search))
                          || (isNumeric && v.Amount == searchAmt)
                          || (v.Supplier != null && v.Supplier.Name.Contains(search))
                          || (v.Employee != null && v.Employee.Name.Contains(search))
                          || (v.CashAccount != null && (v.CashAccount.NameAr.Contains(search) || v.CashAccount.Code.Contains(search)))
                          || (v.ToAccount != null && (v.ToAccount.NameAr.Contains(search) || v.ToAccount.Code.Contains(search))));
        }
        
        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value.Date.AddHours(2));
        if (toDate.HasValue) q = q.Where(v => v.VoucherDate <= toDate.Value.Date.AddDays(1).AddHours(2).AddTicks(-1));
        if (source.HasValue) q = q.Where(v => v.CostCenter == source.Value);

        if (employeeId.HasValue)
            q = q.Where(v => v.EmployeeId == employeeId.Value || _db.JournalLines.Any(l => l.JournalEntryId == v.JournalEntryId && l.EmployeeId == employeeId.Value));
        else if (onlyEmployees == true)
            q = q.Where(v => v.EmployeeId != null || _db.JournalLines.Any(l => l.JournalEntryId == v.JournalEntryId && l.EmployeeId != null));

        var total = await q.CountAsync();
        var rawItems = await q.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id).Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description, v.CreatedAt,
                v.CashAccountId, v.ToAccountId,
                v.CostCenter,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                ToAccountName = v.ToAccount != null ? v.ToAccount.NameAr : null,
                SupplierName = v.Supplier != null ? v.Supplier.Name : null,
                EmployeeName = v.Employee != null ? v.Employee.Name : null
            }).ToListAsync();

        var items = rawItems.Select(v => new {
            v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description, v.CreatedAt,
            v.CashAccountId, v.ToAccountId,
            CostCenter = (int?)v.CostCenter,
            CostCenterLabel = v.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (v.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
            v.CashAccountName,
            v.ToAccountName,
            EntityName = v.SupplierName ?? v.EmployeeName
        }).ToList();

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
        var vNo = await _seq.NextAsync("PV");

        var cashAccount = await _db.Accounts.FindAsync(dto.CashAccountId);
        if (cashAccount == null) return BadRequest(_t.Get("Accounting.PaymentVoucher.AccountNotFound"));
        if (!cashAccount.CanReceivePayment && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin")))
            return BadRequest(_t.Get("Accounting.ReceiptVoucher.AccountCannotReceivePayment", cashAccount.NameAr));

        var voucher = new PaymentVoucher {
            VoucherNumber = vNo, VoucherDate = dto.VoucherDate.ToStoreTime(), Amount = dto.Amount, CashAccountId = dto.CashAccountId,
            ToAccountId = dto.ToAccountId, SupplierId = dto.SupplierId, PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference, Description = dto.Description, AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CostCenter = (OrderSource?)dto.CostCenter,
            EmployeeId = dto.EmployeeId,
            BranchId = dto.BranchId,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value, CreatedAt = TimeHelper.GetEgyptTime(), PurchaseInvoiceId = dto.PurchaseInvoiceId
        };

        if (dto.PurchaseInvoiceId.HasValue) {
            var invoice = await _db.PurchaseInvoices.FindAsync(dto.PurchaseInvoiceId.Value);
            if (invoice != null) {
                var remaining = invoice.TotalAmount - invoice.PaidAmount;
                if (dto.Amount > remaining + 0.1m) return BadRequest(_t.Get("Accounting.ReceiptVoucher.AmountExceedsRemaining", dto.Amount, remaining));
                invoice.PaidAmount += dto.Amount;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
            }
        }

        _db.PaymentVouchers.Add(voucher);
        await _db.SaveChangesAsync();
        await _accounting.PostPaymentVoucherAsync(voucher);

        if (voucher.JournalEntryId.HasValue)
        {
            // ⚡ PERF FIX: run payroll sync in background to avoid blocking the HTTP response
            var jeId = voucher.JournalEntryId.Value;
            BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollForVoucherAsync(jeId));
        }

        var employeeAdvancesAccountId = await _db.AccountSystemMappings
            .Where(m => m.Key == MappingKeys.EmployeeAdvances.ToLower())
            .Select(m => m.AccountId)
            .FirstOrDefaultAsync();

        if (dto.ToAccountId == employeeAdvancesAccountId && dto.EmployeeId.HasValue)
        {
            var advNo = await _seq.NextAsync("ADV");
            var advance = new EmployeeAdvance
            {
                AdvanceNumber = advNo,
                EmployeeId = dto.EmployeeId.Value,
                AdvanceDate = dto.VoucherDate.ToStoreTime(),
                Amount = dto.Amount,
                DeductedAmount = 0,
                Status = AdvanceStatus.Pending,
                Reason = dto.Description,
                Notes = dto.Description,
                CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                CostCenter = (OrderSource?)dto.CostCenter,
                CashAccountId = dto.CashAccountId,
                JournalEntryId = voucher.JournalEntryId
            };
            _db.EmployeeAdvances.Add(advance);
            await _db.SaveChangesAsync();
        }

        BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdatePaymentVoucherDto dto)
    {
        var voucher = await _db.PaymentVouchers.Include(v => v.CashAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin")))
            return BadRequest(_t.Get("Accounting.PaymentVoucher.CannotEditPosted"));

        var oldAmount = voucher.Amount;
        voucher.VoucherDate = dto.VoucherDate.ToStoreTime(); voucher.Amount = dto.Amount; voucher.CashAccountId = dto.CashAccountId;
        voucher.ToAccountId = dto.ToAccountId; voucher.SupplierId = dto.SupplierId; voucher.EmployeeId = dto.EmployeeId; voucher.Description = dto.Description;
        voucher.PurchaseInvoiceId = dto.PurchaseInvoiceId;
        voucher.CostCenter = (OrderSource?)dto.CostCenter;
        voucher.BranchId = dto.BranchId;
        voucher.UpdatedAt = TimeHelper.GetEgyptTime();

        if (voucher.PurchaseInvoiceId.HasValue)
        {
            var invoice = await _db.PurchaseInvoices.FindAsync(voucher.PurchaseInvoiceId.Value);
            if (invoice != null)
            {
                invoice.PaidAmount = (invoice.PaidAmount - oldAmount) + voucher.Amount;
                invoice.Status = invoice.PaidAmount >= invoice.TotalAmount - 0.1m ? PurchaseInvoiceStatus.Paid : PurchaseInvoiceStatus.PartPaid;
                invoice.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        if (entry != null) {
            entry.EntryDate = voucher.VoucherDate; 
            entry.Description = voucher.Description;
            entry.UpdatedAt = TimeHelper.GetEgyptTime();
            _db.JournalLines.RemoveRange(entry.Lines);
            
            entry.Lines.Add(new JournalLine { 
                AccountId = voucher.ToAccountId, 
                Debit = voucher.Amount, 
                Credit = 0, 
                Description = _t.Get("Accounting.ReceiptVoucher.UpdateLog", voucher.VoucherNumber),
                SupplierId = voucher.SupplierId,
                EmployeeId = voucher.EmployeeId,
                CostCenter = voucher.CostCenter
            });
            entry.Lines.Add(new JournalLine { 
                AccountId = voucher.CashAccountId, 
                Debit = 0, 
                Credit = voucher.Amount, 
                Description = _t.Get("Accounting.FromAccountDesc", voucher.CashAccount?.NameAr ?? ""),
                SupplierId = voucher.SupplierId,
                EmployeeId = voucher.EmployeeId,
                CostCenter = voucher.CostCenter
            });
        }

        await _db.SaveChangesAsync();
        BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());

        if (entry != null)
        {
            BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollForVoucherAsync(entry.Id));
        }

        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.PaymentVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.PaymentVoucher && e.Reference == voucher.VoucherNumber);
        if (entry != null && entry.Status == JournalEntryStatus.Posted && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin"))) {
            await _accounting.ReverseEntryAsync(entry.Id, _t.Get("Accounting.PaymentVoucher.ReverseLog"));
            return Ok(new { message = _t.Get("Accounting.ReceiptVoucher.ReverseSuccess") });
        }

        List<PayrollRun> runsToSync = new();
        if (entry != null)
        {
            var textToSearch = $"{entry.Reference} {entry.Description} {string.Join(" ", entry.Lines.Select(l => l.Description))}".ToLower();
            var payrollRuns = await _db.PayrollRuns.Where(r => r.Status != PayrollStatus.Draft).ToListAsync();
            runsToSync = payrollRuns.Where(r => textToSearch.Contains(r.PayrollNumber.ToLower())).ToList();
        }

        _db.PaymentVouchers.Remove(voucher);
        if (entry != null)
        {
            var childReversals = await _db.JournalEntries
                .Include(j => j.Lines)
                .Where(j => j.ReversalOfId == entry.Id)
                .ToListAsync();
            if (childReversals.Any())
            {
                foreach (var child in childReversals)
                {
                    _db.JournalLines.RemoveRange(child.Lines);
                }
                _db.JournalEntries.RemoveRange(childReversals);
            }
            _db.JournalLines.RemoveRange(entry.Lines);
            _db.JournalEntries.Remove(entry);
        }
        await _db.SaveChangesAsync();
        BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());

        foreach (var run in runsToSync)
        {
            BackgroundJob.Enqueue<IAccountingService>(a => a.SyncPayrollRunPaymentsAsync(run.Id));
        }

        return NoContent();
    }
}
