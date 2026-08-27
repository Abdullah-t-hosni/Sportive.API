using System.Security.Claims;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using System.Data;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using Sportive.API.Utils;
using ClosedXML.Excel;

namespace Sportive.API.Controllers;

// SUPPLIERS
[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.PurchasesMain + "," + ModuleKeys.SupplierVouchers + "," + ModuleKeys.Purchases)]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;
    private readonly IAuditService _audit;
    public SuppliersController(AppDbContext db, ITranslator t, IAuditService audit)
    {
        _db = db;
        _t = t;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search   = null,
        [FromQuery] bool?   isActive = null,
        [FromQuery] string? sortBy   = null,
        [FromQuery] string  sortDir  = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var q = _db.Suppliers
            .AsNoTracking()
            .Include(s => s.Invoices)
            .Include(s => s.MainAccount)
            .AsQueryable();

        if (isActive.HasValue) q = q.Where(s => s.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(s => s.Name.Contains(search) || s.Phone.Contains(search)
                           || (s.CompanyName != null && s.CompanyName.Contains(search)));

        var total = await q.CountAsync();
        var desc = sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        var projected = q.Select(s => new {
            s.Id,
            s.Name,
            s.Phone,
            s.CompanyName,
            s.TaxNumber,
            s.Email,
            s.Address,
            s.IsActive,
            s.TotalPurchases,
            s.TotalPaid,
            s.OpeningBalance,
            s.AttachmentUrl,
            s.AttachmentPublicId,
            InvoiceCount = s.Invoices.Count,
            // 💡 Calculate Real Ledger Balance (Credit - Debit on supplier account) matching Supplier Statement 100%
            JournalNet = _db.JournalLines
                .Where(l => (l.SupplierId == s.Id || (s.MainAccountId != null && l.AccountId == s.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft)
                .Sum(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0,
            HasJournalLines = _db.JournalLines.Any(l => (l.SupplierId == s.Id || (s.MainAccountId != null && l.AccountId == s.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft)
        }).Select(x => new {
            x.Id,
            x.Name,
            x.Phone,
            x.CompanyName,
            x.TaxNumber,
            x.Email,
            x.Address,
            x.IsActive,
            x.TotalPurchases,
            x.TotalPaid,
            x.InvoiceCount,
            x.AttachmentUrl,
            x.AttachmentPublicId,
            Balance = x.HasJournalLines ? x.JournalNet : (x.OpeningBalance + x.TotalPurchases - x.TotalPaid)
        });

        var ordered = sortBy?.ToLower() switch {
            "totalpurchases" => desc ? projected.OrderByDescending(s => s.TotalPurchases) : projected.OrderBy(s => s.TotalPurchases),
            "totalpaid"      => desc ? projected.OrderByDescending(s => s.TotalPaid)      : projected.OrderBy(s => s.TotalPaid),
            "balance"        => desc ? projected.OrderByDescending(s => s.Balance)        : projected.OrderBy(s => s.Balance),
            _                => desc ? projected.OrderByDescending(s => s.Name)           : projected.OrderBy(s => s.Name),
        };

        var items = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber, s.Email, s.Address,
                s.IsActive, s.TotalPurchases, s.TotalPaid, s.Balance,
                s.InvoiceCount,
                s.AttachmentUrl, s.AttachmentPublicId
            )).ToListAsync();

        return Ok(new PaginatedResult<SupplierDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var s = await _db.Suppliers.Include(s => s.Invoices).Include(s => s.MainAccount).FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();

        var hasJournal = await _db.JournalLines.AnyAsync(l => (l.SupplierId == s.Id || (s.MainAccountId != null && l.AccountId == s.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft);
        decimal balance;
        if (hasJournal)
        {
            balance = await _db.JournalLines
                .Where(l => (l.SupplierId == s.Id || (s.MainAccountId != null && l.AccountId == s.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft)
                .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0;
        }
        else
        {
            balance = s.OpeningBalance + s.TotalPurchases - s.TotalPaid;
        }

        return Ok(new SupplierDto(s.Id, s.Name, s.Phone, s.CompanyName, s.TaxNumber,
            s.Email, s.Address, s.IsActive, s.TotalPurchases, s.TotalPaid,
            balance, s.Invoices.Count,
            s.AttachmentUrl, s.AttachmentPublicId));
    }

    [HttpGet("{id}/advance-balance")]
    public async Task<IActionResult> GetAdvanceBalance(int id)
    {
        var payments = await _db.SupplierPayments
            .Where(p => p.SupplierId == id && p.PurchaseInvoiceId == null && p.Amount > 0)
            .OrderBy(p => p.PaymentDate)
            .ToListAsync();

        var advanceBalance = payments.Sum(p => p.Amount);
        return Ok(new { advanceBalance });
    }

    [HttpGet("{id}/unlinked-payments")]
    public async Task<IActionResult> GetUnlinkedPayments(int id)
    {
        var payments = await _db.SupplierPayments
            .Where(p => p.SupplierId == id && p.PurchaseInvoiceId == null && p.Amount > 0)
            .OrderBy(p => p.PaymentDate)
            .Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                p.Amount,
                PaymentMethod = p.PaymentMethod,
                p.AccountName,
                p.Notes
            })
            .ToListAsync();

        return Ok(new { payments, total = payments.Sum(p => p.Amount) });
    }

    [HttpPost]

    public async Task<IActionResult> Create([FromBody] CreateSupplierDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest(new { message = _t.Get("Suppliers.NameAndPhoneRequired") });

        var existing = await _db.Suppliers.FirstOrDefaultAsync(s => 
            s.Name.Trim() == dto.Name.Trim() || s.Phone.Trim() == dto.Phone.Trim());
        
        if (existing != null)
        {
            return BadRequest(new { message = _t.Get("Suppliers.AlreadyExists") });
        }

        var supplier = new Supplier
        {
            Name        = dto.Name.Trim(),
            Phone       = dto.Phone.Trim(),
            CompanyName = dto.CompanyName?.Trim(),
            TaxNumber   = dto.TaxNumber?.Trim(),
            Email       = dto.Email?.Trim().ToLower(),
            Address     = dto.Address?.Trim(),
            CreatedAt   = TimeHelper.GetEgyptTime(),
            AttachmentUrl = dto.AttachmentUrl,
            AttachmentPublicId = dto.AttachmentPublicId
        };

        // ——— Use Global Control Account for Supplier ———
        var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2101");
        if (parent != null)
        {
            supplier.MainAccountId = parent.Id;
        }

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("CreateSupplier", "Supplier", supplier.Id.ToString(), $"Created supplier: {supplier.Name}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, new SupplierDto(
            supplier.Id, supplier.Name, supplier.Phone, supplier.CompanyName,
            supplier.TaxNumber, supplier.Email, supplier.Address, supplier.IsActive,
            supplier.TotalPurchases, supplier.TotalPaid, 0, 0,
            supplier.AttachmentUrl, supplier.AttachmentPublicId
        ));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierDto dto)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        supplier.Name        = dto.Name.Trim();
        supplier.Phone       = dto.Phone.Trim();
        supplier.CompanyName = dto.CompanyName?.Trim();
        supplier.TaxNumber   = dto.TaxNumber?.Trim();
        supplier.Email       = dto.Email?.Trim().ToLower();
        supplier.Address     = dto.Address?.Trim();


        supplier.IsActive    = dto.IsActive;
        supplier.AttachmentUrl = dto.AttachmentUrl;
        supplier.AttachmentPublicId = dto.AttachmentPublicId;
        supplier.MainAccountId = dto.MainAccountId;
        supplier.UpdatedAt   = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("UpdateSupplier", "Supplier", id.ToString(), $"Updated supplier info", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        var hasJournal = await _db.JournalLines.AnyAsync(l => (l.SupplierId == supplier.Id || (supplier.MainAccountId != null && l.AccountId == supplier.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft);
        decimal balance = hasJournal
            ? (await _db.JournalLines.Where(l => (l.SupplierId == supplier.Id || (supplier.MainAccountId != null && l.AccountId == supplier.MainAccountId.Value)) && l.JournalEntry.Status != JournalEntryStatus.Draft).SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0)
            : (supplier.OpeningBalance + supplier.TotalPurchases - supplier.TotalPaid);

        return Ok(new SupplierDto(
            supplier.Id, supplier.Name, supplier.Phone, supplier.CompanyName,
            supplier.TaxNumber, supplier.Email, supplier.Address, supplier.IsActive,
            supplier.TotalPurchases, supplier.TotalPaid,
            balance,
            await _db.PurchaseInvoices.CountAsync(i => i.SupplierId == supplier.Id),
            supplier.AttachmentUrl, supplier.AttachmentPublicId
        ));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await _db.Suppliers.Include(s => s.Invoices).FirstOrDefaultAsync(s => s.Id == id);
        if (supplier == null) return NotFound();

        if (supplier.Invoices.Any())
            return BadRequest(new { message = _t.Get("Suppliers.CannotDeleteWithInvoices") });

        _db.Suppliers.Remove(supplier);
        await _db.SaveChangesAsync();
        
        try { await _audit.LogAsync("DeleteSupplier", "Supplier", id.ToString(), $"Deleted supplier", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name)); } catch { }

        return NoContent();
    }

    [HttpPost("import-opening-balances")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportOpeningBalances(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Common.NoFileUploaded") });

        var successCount = 0;
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var allSuppliers = await _db.Suppliers.ToListAsync();

            for (int r = 2; r <= lastRow; r++)
            {
                var identifier = ws.Cell(r, 1).GetString().Trim(); // Name or Phone
                if (string.IsNullOrEmpty(identifier)) continue;

                var balStr = ws.Cell(r, 2).GetString().Trim();
                if (!decimal.TryParse(balStr, out var balance))
                {
                    errors.Add($"سطر {r}: الرصيد غير صحيح للمورد '{identifier}'");
                    continue;
                }

                var supplier = allSuppliers.FirstOrDefault(s => s.Name == identifier || s.Phone == identifier);
                if (supplier == null)
                {
                    errors.Add($"سطر {r}: المورد '{identifier}' غير موجود");
                    continue;
                }

                supplier.OpeningBalance = balance;
                supplier.UpdatedAt = TimeHelper.GetEgyptTime();
                successCount++;
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = _t.Get("Common.ProcessingError", ex.Message) });
        }

        return Ok(new { success = true, successCount, errors });
    }
}
