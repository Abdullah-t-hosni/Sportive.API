using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models.System;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
public class SupportTicketsController : ControllerBase
{
    private readonly MasterDbContext _context;

    public SupportTicketsController(MasterDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetTickets()
    {
        var tickets = await _context.SupportTickets
            .Include(t => t.Tenant)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
        
        return Ok(tickets);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTicket(int id)
    {
        var ticket = await _context.SupportTickets
            .Include(t => t.Tenant)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound();
        return Ok(ticket);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTicket([FromBody] SupportTicket ticket)
    {
        _context.SupportTickets.Add(ticket);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTicket), new { id = ticket.Id }, ticket);
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] TicketStatus newStatus)
    {
        var ticket = await _context.SupportTickets.FindAsync(id);
        if (ticket == null) return NotFound();

        ticket.Status = newStatus;
        if (newStatus == TicketStatus.Resolved || newStatus == TicketStatus.Closed)
        {
            ticket.ResolvedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(ticket);
    }
}
