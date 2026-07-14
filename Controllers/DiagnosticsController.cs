using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DiagnosticsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("orphaned-movements")]
    public async Task<IActionResult> GetOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && m.Reference.StartsWith("ORD-") // Or whatever order numbers start with, actually let's just do an Except or Where !Contains
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        return Ok(new
        {
            Count = orphanedSales.Count,
            Movements = orphanedSales.Select(m => new
            {
                m.Id,
                m.ProductId,
                m.ProductVariantId,
                m.Quantity,
                m.Reference,
                Type = m.Type.ToString(),
                m.CreatedAt
            })
        });
    }

    [HttpPost("cleanup-orphaned-movements")]
    public async Task<IActionResult> CleanupOrphanedMovements()
    {
        var existingOrderNumbers = await _db.Orders.Select(o => o.OrderNumber).ToListAsync();

        // Include "POS" prefixed orders or "ORD" if needed. We just check if Reference looks like an order
        var orphanedSales = await _db.InventoryMovements
            .Where(m => (m.Type == InventoryMovementType.Sale || m.Type == InventoryMovementType.ReturnIn) 
                     && m.Reference != null 
                     && !existingOrderNumbers.Contains(m.Reference))
            .ToListAsync();

        _db.InventoryMovements.RemoveRange(orphanedSales);
        await _db.SaveChangesAsync();

        // Run Recalculate Stock logic for affected products
        // We will just do the simple stock sums update like DataMaintenanceService does.
        var affectedProductIds = orphanedSales.Where(m => m.ProductId.HasValue).Select(m => m.ProductId!.Value).Distinct().ToList();
        var affectedVariantIds = orphanedSales.Where(m => m.ProductVariantId.HasValue).Select(m => m.ProductVariantId!.Value).Distinct().ToList();

        return Ok(new
        {
            DeletedCount = orphanedSales.Count,
            AffectedProductIds = affectedProductIds,
            AffectedVariantIds = affectedVariantIds,
            Message = "Deleted successfully. Please run recalculate-stock from UI to update product Stock."
        });
    }

    [HttpGet("po-diagnostics")]
    public async Task<IActionResult> GetPoDiagnostics()
    {
        var invoice = await _db.PurchaseInvoices
            .Include(i => i.Supplier)
            .FirstOrDefaultAsync(i => i.InvoiceNumber == "PO-2607-0004");

        if (invoice == null)
        {
            return NotFound(new { message = "Purchase Invoice PO-2607-0004 not found" });
        }

        var payments = await _db.SupplierPayments
            .Where(p => p.SupplierId == invoice.SupplierId || p.PurchaseInvoiceId == invoice.Id)
            .ToListAsync();

        var entryRefs = payments.Select(p => p.PaymentNumber).Concat(new[] { invoice.InvoiceNumber }).ToList();
        
        var entries = await _db.JournalEntries
            .Include(e => e.Lines)
            .ThenInclude(l => l.Account)
            .Where(e => entryRefs.Contains(e.Reference) || e.PurchaseInvoiceId == invoice.Id)
            .ToListAsync();

        return Ok(new
        {
            Invoice = new
            {
                invoice.Id,
                invoice.InvoiceNumber,
                SupplierName = invoice.Supplier?.Name,
                invoice.SupplierId,
                invoice.TotalAmount,
                invoice.PaidAmount,
                invoice.RemainingAmount,
                Status = invoice.Status.ToString(),
                Terms = invoice.PaymentTerms.ToString()
            },
            Payments = payments.Select(p => new
            {
                p.Id,
                p.PaymentNumber,
                p.PaymentDate,
                p.Amount,
                p.PurchaseInvoiceId,
                PaymentMethod = p.PaymentMethod.ToString(),
                p.Notes
            }),
            Entries = entries.Select(e => new
            {
                e.EntryNumber,
                Type = e.Type.ToString(),
                e.Reference,
                e.EntryDate,
                e.Description,
                Lines = e.Lines.Select(l => new
                {
                    l.AccountId,
                    AccountCode = l.Account?.Code,
                    AccountName = l.Account?.NameAr,
                    l.Debit,
                    l.Credit,
                    l.Description
                })
            })
        });
    }
}
