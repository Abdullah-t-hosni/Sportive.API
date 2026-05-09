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
    private readonly ITranslator _t;
    public CustomersController(ICustomerService customers, ITranslator t) => (_customers, _t) = (customers, t);

    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
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

    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
    [HttpGet("rfm")]
    public async Task<IActionResult> GetRfm() =>
        Ok(await _customers.GetRfmDataAsync());

    [RequirePermission(ModuleKeys.Customers)]
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights() =>
        Ok(await _customers.GetInsightsAsync());

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        try { return Ok(await _customers.UpdateCustomerAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>إضافة عميل جديد — Admin, Manager, Cashier</summary>
    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
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

    /// <summary>تفاصيل عميل — Admin أو صاحب الحساب</summary>
    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        var customer = await _customers.GetCustomerByIdAsync(id);
        return customer == null ? NotFound() : Ok(customer);
    }

    /// <summary>تفعيل / إيقاف عميل — Admin فقط</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var result = await _customers.ToggleCustomerAsync(id);
        return result ? Ok() : NotFound();
    }

    /// <summary>حذف عميل بالكامل — Admin فقط</summary>
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

    [Authorize]
    [HttpPost("{customerId}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto) 
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();
        return Ok(await _customers.AddAddressAsync(customerId, dto));
    }

    [Authorize]
    [HttpDelete("{customerId}/addresses/{addressId}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        try { await _customers.DeleteAddressAsync(customerId, addressId); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize]
    [HttpPatch("{customerId}/addresses/{addressId}/default")]
    public async Task<IActionResult> SetDefault(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        await _customers.SetDefaultAddressAsync(customerId, addressId);
        return Ok(new { message = _t.Get("Customers.DefaultAddressUpdated") });
    }

    // Helper to check ownership
    private bool IsOwnerOrAdmin(int customerId)
    {
        if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier"))
            return true;

        var currentCustomerId = User.FindFirst("CustomerId")?.Value;
        return currentCustomerId != null && int.Parse(currentCustomerId) == customerId;
    }

    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPost("import-opening-balances")]
    public async Task<IActionResult> ImportOpeningBalances(IFormFile file, [FromServices] AppDbContext db)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Accounting.NoFileUploaded") });

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
                    errors.Add(_t.Get("Accounting.InvalidBalanceAtRow", r, identifier));
                    continue;
                }

                var customer = allCustomers.FirstOrDefault(c => c.FullName == identifier || c.Phone == identifier);
                if (customer == null)
                {
                    errors.Add(_t.Get("Accounting.CustomerNotFoundAtRow", r, identifier));
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
            return BadRequest(new { message = _t.Get("Accounting.ProcessingError", ex.Message) });
        }

        return Ok(new { success = true, successCount, errors });
    }
}
