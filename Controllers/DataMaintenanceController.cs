using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Microsoft.AspNetCore.Identity;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class DataMaintenanceController : ControllerBase
{
    private readonly IDataMaintenanceService _service;
    private readonly IWebHostEnvironment _env;
    private readonly UserManager<AppUser> _userManager;

    public DataMaintenanceController(IDataMaintenanceService service, IWebHostEnvironment env, UserManager<AppUser> userManager)
    {
        _service = service;
        _env = env;
        _userManager = userManager;
    }

    [HttpPost("wipe-customers")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> WipeCustomers()
    {
        if (!_env.IsDevelopment()) return StatusCode(403, new { success = false, message = "هذه الخاصية متاحة فقط في بيئة التطوير." });

        var (success, message) = await _service.WipeCustomersAsync(User.Identity?.Name);
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("factory-reset/request-otp")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> RequestFactoryResetOtp()
    {
        if (!_env.IsDevelopment()) return StatusCode(403, new { success = false, message = "هذه الخاصية متاحة فقط في بيئة التطوير." });

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "User not found" });

        var (success, message) = await _service.RequestFactoryResetOtpAsync(user);
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    public record FactoryResetRequest(string Password, string Otp, string Confirmation);

    [HttpPost("factory-reset")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> FactoryReset([FromBody] FactoryResetRequest req)
    {
        if (!_env.IsDevelopment()) return StatusCode(403, new { success = false, message = "هذه الخاصية متاحة فقط في بيئة التطوير." });

        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized(new { success = false, message = "User not found" });

        var (success, message) = await _service.FactoryResetAsync(user, req.Password, req.Otp, req.Confirmation, User.Identity?.Name);
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpGet("fix-tree"), HttpPost("fix-tree")]
    public async Task<IActionResult> FixTree()
    {
        var (success, message, count) = await _service.FixTreeAsync(User.Identity?.Name);
        return success ? Ok(new { success, message, count }) : BadRequest(new { success, message });
    }

    [HttpGet("debug-account/{code}")]
    public IActionResult DebugAccount(string code)
    {
        return BadRequest(new { message = "هذا الـ Endpoint تم تعطيله مؤقتاً." });
    }

    [HttpGet("debug-supplier/{id}")]
    public IActionResult DebugSupplier(int id)
    {
        return BadRequest(new { message = "هذا الـ Endpoint تم تعطيله مؤقتاً." });
    }

    [HttpGet("sync-accounts")]
    public async Task<IActionResult> SyncAccounts()
    {
        var (success, message) = await _service.SyncAccountsAsync(User.Identity?.Name);
        return success ? Ok(new { message }) : BadRequest(new { message });
    }

    [HttpGet("rebuild"), HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild() => await FixTree();

    [HttpGet("purge-deleted"), HttpPost("purge-deleted")]
    public async Task<IActionResult> PurgeDeleted()
    {
        if (!_env.IsDevelopment()) return StatusCode(403, new { success = false, message = "هذه الخاصية متاحة فقط في بيئة التطوير." });

        var (success, purged, message) = await _service.PurgeDeletedAsync(User.Identity?.Name);
        return success ? Ok(new { success, purged }) : BadRequest(new { success, message });
    }

    [HttpPost("fix-pos-orders")]
    public async Task<IActionResult> FixPosOrders()
    {
        var (success, message) = await _service.FixPosOrdersAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("cleanup-duplicates")]
    public async Task<IActionResult> CleanupDuplicates()
    {
        var (success, message) = await _service.CleanupDuplicatesAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-order-accounting")]
    public async Task<IActionResult> SyncOrderAccounting()
    {
        var (success, message) = await _service.SyncOrderAccountingAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-payment-accounting")]
    public async Task<IActionResult> SyncPaymentAccounting()
    {
        var (success, message) = await _service.SyncPaymentAccountingAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-purchase-journal-entries")]
    public async Task<IActionResult> SyncPurchaseJournalEntries()
    {
        var (success, message, details) = await _service.SyncPurchaseJournalEntriesAsync();
        return success ? Ok(new { success, message, details }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-entity-ids")]
    public async Task<IActionResult> SyncEntityIds()
    {
        var (success, message) = await _service.SyncEntityIdsAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-sub-accounts")]
    public async Task<IActionResult> SyncSubAccounts()
    {
        var (success, message) = await _service.SyncSubAccountsAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("sync-ledger-source")]
    public async Task<IActionResult> SyncLedgerSource()
    {
        var (success, message) = await _service.SyncLedgerSourceAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }

    [HttpPost("fix-utc-times")]
    public async Task<IActionResult> FixUtcTimes()
    {
        var (success, message) = await _service.FixUtcTimesAsync();
        return success ? Ok(new { success, message }) : BadRequest(new { success, message });
    }
}
