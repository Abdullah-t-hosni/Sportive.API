using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
public class GlobalSearchController : ControllerBase
{
    private readonly MasterDbContext _context;

    public GlobalSearchController(MasterDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(new { tenants = Array.Empty<object>(), tickets = Array.Empty<object>(), invoices = Array.Empty<object>() });
        }

        var lowerQuery = query.ToLower();

        var tenants = await _context.Tenants
            .Where(t => t.Name.ToLower().Contains(lowerQuery) || t.Subdomain.ToLower().Contains(lowerQuery))
            .Select(t => new { id = t.TenantGuid, name = t.Name, type = "Tenant" })
            .Take(5)
            .ToListAsync();

        var tickets = await _context.SupportTickets
            .Where(t => t.Subject.ToLower().Contains(lowerQuery))
            .Select(t => new { id = t.Id, title = t.Subject, type = "Ticket" })
            .Take(5)
            .ToListAsync();

        var invoices = await _context.PlatformInvoices
            .Where(i => i.InvoiceNumber.ToLower().Contains(lowerQuery))
            .Select(i => new { id = i.Id, title = i.InvoiceNumber, type = "Invoice" })
            .Take(5)
            .ToListAsync();

        return Ok(new
        {
            tenants,
            tickets,
            invoices
        });
    }
}
