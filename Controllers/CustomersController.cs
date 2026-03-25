using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    public CustomersController(ICustomerService customers) => _customers = customers;

    /// <summary>كل العملاء — Admin فقط</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null) =>
        Ok(await _customers.GetCustomersAsync(page, pageSize, search));

    /// <summary>تفاصيل عميل — Admin أو صاحب الحساب</summary>
    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
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
        var result = await _customers.DeleteCustomerAsync(id);
        return result ? Ok(new { message = "Customer deleted successfully" }) : NotFound();
    }

    /// <summary>إضافة عنوان</summary>
    [Authorize]
    [HttpPost("{customerId}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto) =>
        Ok(await _customers.AddAddressAsync(customerId, dto));

    /// <summary>حذف عنوان</summary>
    [Authorize]
    [HttpDelete("{customerId}/addresses/{addressId}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
    {
        try { await _customers.DeleteAddressAsync(customerId, addressId); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>تعيين عنوان افتراضي</summary>
    [Authorize]
    [HttpPatch("{customerId}/addresses/{addressId}/default")]
    public async Task<IActionResult> SetDefault(int customerId, int addressId)
    {
        await _customers.SetDefaultAddressAsync(customerId, addressId);
        return Ok(new { message = "Default address updated" });
    }
}
