using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Data;
using Sportive.API.Utils;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    public CustomersController(ICustomerService customers) => _customers = customers;

    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] decimal? minSpent = null,
        [FromQuery] int? minOrders = null,
        [FromQuery] DateTime? joinStartDate = null,
        [FromQuery] DateTime? joinEndDate = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool? hasDebt = null,
        [FromQuery] string? orderBy = null,
        [FromQuery] bool isDescending = true) =>
        Ok(await _customers.GetCustomersAsync(page, pageSize, search, minSpent, minOrders, joinStartDate, joinEndDate, categoryId, hasDebt, orderBy, isDescending));

    /// <summary>Ø¨ÙŠØ§Ù†Ø§Øª RFM Ø®ÙÙŠÙØ© Ù„ÙƒÙ„ Ø§Ù„Ø¹Ù…Ù„Ø§Ø¡ â€” Ø¨Ø¯ÙˆÙ† pagination Ø£Ùˆ addresses</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpGet("rfm")]
    public async Task<IActionResult> GetRfm() =>
        Ok(await _customers.GetRfmDataAsync());

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        // ðŸ›¡ï¸ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        try { return Ok(await _customers.UpdateCustomerAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>Ø¥Ø¶Ø§ÙØ© Ø¹Ù…ÙŠÙ„ Ø¬Ø¯ÙŠØ¯ â€” Admin, Manager, Cashier</summary>
    [RequirePermission(ModuleKeys.Customers)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerDto dto)
    {
        try 
        {
            var customer = await _customers.CreateCustomerAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ØªÙØ§ØµÙŠÙ„ Ø¹Ù…ÙŠÙ„ â€” Admin Ø£Ùˆ ØµØ§Ø­Ø¨ Ø§Ù„Ø­Ø³Ø§Ø¨</summary>
    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // ðŸ›¡ï¸ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        var customer = await _customers.GetCustomerByIdAsync(id);
        return customer == null ? NotFound() : Ok(customer);
    }

    /// <summary>ØªÙØ¹ÙŠÙ„ / Ø¥ÙŠÙ‚Ø§Ù Ø¹Ù…ÙŠÙ„ â€” Admin ÙÙ‚Ø·</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var result = await _customers.ToggleCustomerAsync(id);
        return result ? Ok() : NotFound();
    }

    /// <summary>Ø­Ø°Ù Ø¹Ù…ÙŠÙ„ Ø¨Ø§Ù„ÙƒØ§Ù…Ù„ â€” Admin ÙÙ‚Ø·</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        try
        {
            var result = await _customers.DeleteCustomerAsync(id);
            return result ? Ok(new { message = "Customer deleted successfully" }) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Ø¥Ø¶Ø§ÙØ© Ø¹Ù†ÙˆØ§Ù†</summary>
    [Authorize]
    [HttpPost("{customerId}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto) 
    {
        // ðŸ›¡ï¸ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();
        return Ok(await _customers.AddAddressAsync(customerId, dto));
    }

    /// <summary>Ø­Ø°Ù Ø¹Ù†ÙˆØ§Ù†</summary>
    [Authorize]
    [HttpDelete("{customerId}/addresses/{addressId}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
    {
        // ðŸ›¡ï¸ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        try { await _customers.DeleteAddressAsync(customerId, addressId); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>ØªØ¹ÙŠÙŠÙ† Ø¹Ù†ÙˆØ§Ù† Ø§ÙØªØ±Ø§Ø¶ÙŠ</summary>
    [Authorize]
    [HttpPatch("{customerId}/addresses/{addressId}/default")]
    public async Task<IActionResult> SetDefault(int customerId, int addressId)
    {
        // ðŸ›¡ï¸ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        await _customers.SetDefaultAddressAsync(customerId, addressId);
        return Ok(new { message = "Default address updated" });
    }

    // Helper to check ownership
    private bool IsOwnerOrAdmin(int customerId)
    {
        if (User.IsInRole("Admin") || User.IsInRole("Manager"))
            return true;

        var currentCustomerId = User.FindFirst("CustomerId")?.Value;
        return currentCustomerId != null && int.Parse(currentCustomerId) == customerId;
    }

    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPost("import-opening-balances")]
    public async Task<IActionResult> ImportOpeningBalances(IFormFile file, [FromServices] AppDbContext db)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = "Ù„Ù… ÙŠØªÙ… Ø±ÙØ¹ Ù…Ù„Ù" });

        var successCount = 0;
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var allCustomers = await db.Customers.Include(c => c.MainAccount).ToListAsync();

            for (int r = 2; r <= lastRow; r++)
            {
                var identifier = ws.Cell(r, 1).GetString().Trim(); // Name or Phone
                if (string.IsNullOrEmpty(identifier)) continue;

                var balStr = ws.Cell(r, 2).GetString().Trim();
                if (!decimal.TryParse(balStr, out var balance))
                {
                    errors.Add($"Ø³Ø·Ø± {r}: Ø§Ù„Ø±ØµÙŠØ¯ ØºÙŠØ± ØµØ­ÙŠØ­ Ù„Ù„Ø¹Ù…ÙŠÙ„ '{identifier}'");
                    continue;
                }

                var customer = allCustomers.FirstOrDefault(c => c.FullName == identifier || c.Phone == identifier);
                if (customer == null)
                {
                    errors.Add($"Ø³Ø·Ø± {r}: Ø§Ù„Ø¹Ù…ÙŠÙ„ '{identifier}' ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯");
                    continue;
                }

                if (customer.MainAccount != null)
                {
                    customer.MainAccount.OpeningBalance = balance;
                    customer.MainAccount.UpdatedAt = TimeHelper.GetEgyptTime();
                }
                
                customer.UpdatedAt = TimeHelper.GetEgyptTime();
                successCount++;
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"Ø®Ø·Ø£ ÙÙŠ Ø§Ù„Ù…Ø¹Ø§Ù„Ø¬Ø©: {ex.Message}" });
        }

        return Ok(new { success = true, successCount, errors });
    }
}

