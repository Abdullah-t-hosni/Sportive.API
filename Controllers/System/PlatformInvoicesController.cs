using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models.System;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
public class PlatformInvoicesController : ControllerBase
{
    private readonly MasterDbContext _context;

    public PlatformInvoicesController(MasterDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices()
    {
        var invoices = await _context.PlatformInvoices
            .Include(i => i.Tenant)
            .OrderByDescending(i => i.IssueDate)
            .ToListAsync();

        return Ok(invoices);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetInvoice(int id)
    {
        var invoice = await _context.PlatformInvoices
            .Include(i => i.Tenant)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] PlatformInvoice invoice)
    {
        _context.PlatformInvoices.Add(invoice);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
    }
}
