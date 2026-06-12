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
using Sportive.API.Extensions;
using Hangfire;

namespace Sportive.API.Controllers;

[ApiController, Route("api/[controller]")]
[RequirePermission(ModuleKeys.AccountingMain + "," + ModuleKeys.Pos)]
public class ReceiptVouchersController : ControllerBase
{
    private readonly ITranslator _t;
    private readonly IAccountingService _accounting;
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly IPdfService _pdf;
    public ReceiptVouchersController(IAccountingService accounting, AppDbContext db, SequenceService seq, IPdfService pdf, ITranslator t) {
        _accounting = accounting;
        _db = db;
        _seq = seq;
        _pdf = pdf;
        _t = t;
    }

    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GetPdf(int id)
    {
        var voucher = await _db.ReceiptVouchers
            .Include(v => v.CashAccount)
            .Include(v => v.FromAccount)
            .Include(v => v.Customer)
            .Include(v => v.Employee)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (voucher == null) return NotFound();

        var pdfBytes = await _pdf.GenerateVoucherPdfAsync(voucher, null);
        return File(pdfBytes, "application/pdf", $"Receipt-{voucher.VoucherNumber}.pdf");
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
        [FromQuery] int? employeeId = null,
        [FromQuery] int? branchId = null)
    {
        var q = _db.ReceiptVouchers.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var isNumeric = decimal.TryParse(search, out var searchAmt);
            q = q.Where(v => v.VoucherNumber.Contains(search)
                          || (v.Description != null && v.Description.Contains(search))
                          || (v.Reference != null && v.Reference.Contains(search))
                          || (isNumeric && v.Amount == searchAmt)
                          || (v.Customer != null && v.Customer.FullName.Contains(search))
                          || (v.Employee != null && v.Employee.Name.Contains(search))
                          || (v.CashAccount != null && (v.CashAccount.NameAr.Contains(search) || v.CashAccount.Code.Contains(search)))
                          || (v.FromAccount != null && (v.FromAccount.NameAr.Contains(search) || v.FromAccount.Code.Contains(search))));
        }
        
        if (fromDate.HasValue) q = q.Where(v => v.VoucherDate >= fromDate.Value.Date.AddHours(TimeHelper.GetBusinessDayEndHour()));
        if (toDate.HasValue) q = q.Where(v => v.VoucherDate <= toDate.Value.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1));
        if (source.HasValue) q = q.Where(v => v.CostCenter == source.Value);

        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            int? isolatedBranchId = User.GetBranchId();
            if (isolatedBranchId.HasValue)
            {
                q = q.Where(v => v.BranchId == isolatedBranchId.Value);
            }
        }
        else if (branchId.HasValue) 
        {
            q = q.Where(v => v.BranchId == branchId.Value);
        }

        if (employeeId.HasValue)
            q = q.Where(v => v.EmployeeId == employeeId.Value || _db.JournalLines.Any(l => l.JournalEntryId == v.JournalEntryId && l.EmployeeId == employeeId.Value));
        else if (onlyEmployees == true)
            q = q.Where(v => v.EmployeeId != null || _db.JournalLines.Any(l => l.JournalEntryId == v.JournalEntryId && l.EmployeeId != null));
        
        var total = await q.CountAsync();
        var rawItems = await q.OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description, v.CreatedAt,
                v.CashAccountId,
                v.CostCenter,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                FromAccountName = v.FromAccount != null ? v.FromAccount.NameAr : null,
                CustomerName = v.Customer != null ? v.Customer.FullName : null,
                EmployeeName = v.Employee != null ? v.Employee.Name : null
            })
            .ToListAsync();

        var items = rawItems.Select(v => new {
            v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description, v.CreatedAt,
            v.CashAccountId,
            CostCenter = (int?)v.CostCenter,
            CostCenterLabel = v.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (v.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
            v.CashAccountName,
            v.FromAccountName,
            EntityName = v.CustomerName ?? v.EmployeeName
        }).ToList();

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling(total/(double)pageSize) });
    }

    [HttpGet("order/{orderId}")]
    public async Task<IActionResult> GetByOrderId(int orderId)
    {
        var rawItems = await _db.ReceiptVouchers
            .Where(v => v.OrderId == orderId)
            .Include(v => v.CashAccount)
            .Include(v => v.FromAccount)
            .Include(v => v.Customer)
            .OrderByDescending(v => v.VoucherDate).ThenByDescending(v => v.Id)
            .Select(v => new { 
                v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
                v.CashAccountId,
                v.CostCenter,
                CashAccountName = v.CashAccount != null ? v.CashAccount.NameAr : null,
                FromAccountName = v.FromAccount != null ? v.FromAccount.NameAr : null,
                CustomerName = v.Customer != null ? v.Customer.FullName : null,
                EmployeeName = v.Employee != null ? v.Employee.Name : null
            })
            .ToListAsync();

        var items = rawItems.Select(v => new {
            v.Id, v.VoucherNumber, v.VoucherDate, v.Amount, v.PaymentMethod, v.Reference, v.Description,
            v.CashAccountId,
            CostCenter = (int?)v.CostCenter,
            CostCenterLabel = v.CostCenter == OrderSource.Website ? _t.Get("Accounting.CostCenter.Website") : (v.CostCenter == OrderSource.POS ? _t.Get("Accounting.CostCenter.POS") : _t.Get("Accounting.CostCenter.General")),
            v.CashAccountName,
            v.FromAccountName,
            EntityName = v.CustomerName ?? v.EmployeeName
        }).ToList();
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
        var vNo = await _seq.NextAsync("RV");
        
        var cashAccount = await _db.Accounts.FindAsync(dto.CashAccountId);
        if (cashAccount == null) return BadRequest(_t.Get("Accounting.ReceiptVoucher.AccountNotFound"));
        if (!cashAccount.CanReceivePayment && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin")))
            return BadRequest(_t.Get("Accounting.ReceiptVoucher.AccountCannotReceivePayment", cashAccount.NameAr));

        var vDate = dto.VoucherDate.ToStoreTime();
        if (vDate.TimeOfDay == TimeSpan.Zero) vDate = vDate.Add(TimeHelper.GetEgyptTime().TimeOfDay);

        var voucher = new ReceiptVoucher {
            VoucherNumber = vNo, VoucherDate = vDate, Amount = dto.Amount, CashAccountId = dto.CashAccountId,
            FromAccountId = dto.FromAccountId, CustomerId = dto.CustomerId, PaymentMethod = dto.PaymentMethod,
            Reference = dto.Reference, Description = dto.Description, AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId,
            CostCenter = (OrderSource?)dto.CostCenter,
            EmployeeId = dto.EmployeeId,
            BranchId = dto.BranchId,
            CreatedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value, CreatedAt = TimeHelper.GetEgyptTime(), OrderId = dto.OrderId
        };

        if (voucher.CostCenter == null && dto.OrderId.HasValue)
        {
            voucher.CostCenter = await _db.Orders.Where(o => o.Id == dto.OrderId.Value).Select(o => (OrderSource?)o.Source).FirstOrDefaultAsync();
        }

        _db.ReceiptVouchers.Add(voucher);

        if (dto.OrderId.HasValue) {
            var order = await _db.Orders.FindAsync(dto.OrderId.Value);
            if (order != null) {
                var remaining = order.TotalAmount - order.PaidAmount;
                if (dto.Amount > remaining + 0.01m) return BadRequest(_t.Get("Accounting.ReceiptVoucher.AmountExceedsRemaining", dto.Amount, remaining));
                
                order.PaidAmount += dto.Amount;
                order.PaymentStatus = order.PaidAmount >= order.TotalAmount - 0.01m ? PaymentStatus.Paid : PaymentStatus.Pending;
                order.UpdatedAt = TimeHelper.GetEgyptTime();

                _db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    Status = order.Status,
                    Note = _t.Get("Accounting.ReceiptVoucher.DebtCollectionLog", dto.Amount, dto.PaymentMethod),
                    ChangedByUserId = dto.EmployeeId?.ToString() ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    CreatedAt = TimeHelper.GetEgyptTime()
                });
            }
        }
        else if (dto.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FindAsync(dto.CustomerId.Value);
            if (customer != null)
            {
                var customerAccountId = await _db.AccountSystemMappings
                    .Where(m => m.Key == MappingKeys.Customer.ToLower())
                    .Select(m => m.AccountId)
                    .FirstOrDefaultAsync();

                if (customerAccountId.HasValue)
                {
                    var currentBalance = await _db.JournalLines
                        .Where(l => l.AccountId == customerAccountId.Value && l.CustomerId == dto.CustomerId.Value)
                        .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0;

                    if (dto.Amount > currentBalance + 0.1m)
                    {
                        return BadRequest(_t.Get("Accounting.ReceiptVoucher.AmountExceedsCustomerBalance", currentBalance));
                    }
                }

                if (dto.Reference == null || !dto.Reference.StartsWith("Installment-"))
                {
                    var pendingInstallments = await _db.CustomerInstallments
                        .Where(i => i.CustomerId == dto.CustomerId.Value && i.Status != InstallmentStatus.Paid && i.Status != InstallmentStatus.Cancelled)
                        .OrderBy(i => i.DueDate)
                        .ToListAsync();

                    decimal remainingToDistribute = dto.Amount;
                    foreach (var inst in pendingInstallments)
                    {
                        if (remainingToDistribute <= 0) break;

                        decimal amountToPay = Math.Min(remainingToDistribute, inst.RemainingAmount);
                        remainingToDistribute -= amountToPay;

                        var payment = new InstallmentPayment
                        {
                            CustomerInstallmentId = inst.Id,
                            Amount = amountToPay,
                            PaymentDate = TimeHelper.GetEgyptTime(),
                            Note = $"سداد آلي من سند قبض رقم {vNo}",
                            CollectedBy = voucher.CreatedByUserId,
                            ReceiptVoucher = voucher
                        };
                        _db.InstallmentPayments.Add(payment);

                        inst.PaidAmount += amountToPay;
                        inst.UpdatedAt = TimeHelper.GetEgyptTime();

                        if (inst.PaidAmount >= inst.TotalAmount)
                            inst.Status = InstallmentStatus.Paid;
                        else if (inst.DueDate < DateTime.Today)
                            inst.Status = InstallmentStatus.Overdue;
                        else
                            inst.Status = InstallmentStatus.Partial;
                    }
                }
            }
        }

        await _db.SaveChangesAsync();
        await _accounting.PostReceiptVoucherAsync(voucher, dto.OrderId);
        BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        return Ok(voucher);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateReceiptVoucherDto dto)
    {
        var voucher = await _db.ReceiptVouchers.Include(v => v.FromAccount).FirstOrDefaultAsync(v => v.Id == id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin")))
            return BadRequest(_t.Get("Accounting.ReceiptVoucher.CannotEditPosted"));

        var oldAmount = voucher.Amount;
        var vDate = dto.VoucherDate.ToStoreTime();
        if (vDate.TimeOfDay == TimeSpan.Zero) vDate = vDate.Add(TimeHelper.GetEgyptTime().TimeOfDay);
        
        voucher.VoucherDate = vDate;
        voucher.Amount = dto.Amount;
        voucher.CashAccountId = dto.CashAccountId;
        voucher.FromAccountId = dto.FromAccountId;
        voucher.CustomerId = dto.CustomerId;
        voucher.EmployeeId = dto.EmployeeId;
        voucher.PaymentMethod = dto.PaymentMethod;
        voucher.Reference = dto.Reference;
        voucher.Description = dto.Description;
        voucher.AttachmentUrl = dto.AttachmentUrl;
        voucher.AttachmentPublicId = dto.AttachmentPublicId;
        voucher.CostCenter = (OrderSource?)dto.CostCenter;
        voucher.BranchId = dto.BranchId;
        voucher.UpdatedAt = TimeHelper.GetEgyptTime();

        if (voucher.OrderId.HasValue)
        {
            var order = await _db.Orders.FindAsync(voucher.OrderId.Value);
            if (order != null)
            {
                order.PaidAmount = (order.PaidAmount - oldAmount) + voucher.Amount;
                order.PaymentStatus = order.PaidAmount >= order.TotalAmount - 0.01m ? PaymentStatus.Paid : PaymentStatus.Pending;
                order.UpdatedAt = TimeHelper.GetEgyptTime();
            }
        }

        if (entry != null) {
            entry.EntryDate = voucher.VoucherDate; 
            entry.Description = voucher.Description; 
            entry.UpdatedAt = TimeHelper.GetEgyptTime();
            _db.JournalLines.RemoveRange(entry.Lines);
            
            entry.Lines.Add(new JournalLine { 
                AccountId = voucher.CashAccountId, 
                Debit = voucher.Amount, 
                Credit = 0, 
                Description = _t.Get("Accounting.ReceiptVoucher.UpdateLog", voucher.VoucherNumber),
                CustomerId = voucher.CustomerId,
                EmployeeId = voucher.EmployeeId,
                CostCenter = voucher.CostCenter
            });
            entry.Lines.Add(new JournalLine { 
                AccountId = voucher.FromAccountId, 
                Debit = 0, 
                Credit = voucher.Amount, 
                Description = _t.Get("Accounting.FromAccountDesc", voucher.FromAccount?.NameAr ?? ""),
                CustomerId = voucher.CustomerId,
                EmployeeId = voucher.EmployeeId,
                CostCenter = voucher.CostCenter
            });
        }

        await _db.SaveChangesAsync();
        BackgroundJob.Enqueue<IAccountingService>(a => a.SyncEntityBalancesAsync());
        return Ok(voucher);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _db.ReceiptVouchers.FindAsync(id);
        if (voucher == null) return NotFound();

        var entry = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Type == JournalEntryType.ReceiptVoucher && e.Reference == voucher.VoucherNumber);
        
        if (entry != null && entry.Status == JournalEntryStatus.Posted && (!User.IsInRole("SuperAdmin") && !User.IsInRole("Admin"))) {
            await _accounting.ReverseEntryAsync(entry.Id, _t.Get("Accounting.ReceiptVoucher.ReverseLog"));
            return Ok(new { message = _t.Get("Accounting.ReceiptVoucher.ReverseSuccess") });
        }

        _db.ReceiptVouchers.Remove(voucher);
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
        return NoContent();
    }
}
