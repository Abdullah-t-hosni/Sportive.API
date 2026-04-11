using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    public CustomersController(ICustomerService customers) => _customers = customers;

    [Authorize(Roles = "Admin,Manager")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null) =>
        Ok(await _customers.GetCustomersAsync(page, pageSize, search));

    /// <summary>بيانات RFM خفيفة لكل العملاء — بدون pagination أو addresses</summary>
    [Authorize(Roles = "Admin,Manager")]
    [HttpGet("rfm")]
    public async Task<IActionResult> GetRfm() =>
        Ok(await _customers.GetRfmDataAsync());

    [Authorize(Roles = "Admin,Manager")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        try { return Ok(await _customers.UpdateCustomerAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>إضافة عميل جديد — Admin, Manager, Cashier</summary>
    [Authorize(Roles = "Admin,Manager,Cashier,sttaf")]
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
    [Authorize(Roles = "Admin")]
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var result = await _customers.ToggleCustomerAsync(id);
        return result ? Ok() : NotFound();
    }

    /// <summary>حذف عميل بالكامل — Admin فقط</summary>
    [Authorize(Roles = "Admin")]
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

    /// <summary>إضافة عنوان</summary>
    [Authorize]
    [HttpPost("{customerId}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto) 
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();
        return Ok(await _customers.AddAddressAsync(customerId, dto));
    }

    /// <summary>حذف عنوان</summary>
    [Authorize]
    [HttpDelete("{customerId}/addresses/{addressId}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        try { await _customers.DeleteAddressAsync(customerId, addressId); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>تعيين عنوان افتراضي</summary>
    [Authorize]
    [HttpPatch("{customerId}/addresses/{addressId}/default")]
    public async Task<IActionResult> SetDefault(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
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
}
