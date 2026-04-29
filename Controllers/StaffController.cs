using Sportive.API.Attributes;
using Sportive.API.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Staff, requireEdit: true)]
public class StaffController : ControllerBase
{
    private readonly UserManager<AppUser>      _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext              _db;
    private readonly StaffPermissionService    _permService;
    private readonly SequenceService           _sequence;
    private readonly ICustomerService          _customerService;
    private readonly ICacheService             _cache;
    private readonly IAuditService             _audit;

    public StaffController(
        UserManager<AppUser>      users,
        RoleManager<IdentityRole> roles,
        AppDbContext              db,
        StaffPermissionService    permService,
        SequenceService           sequence,
        ICustomerService          customerService,
        ICacheService             cache,
        IAuditService             audit)
    {
        _users           = users;
        _roles           = roles;
        _db              = db;
        _permService     = permService;
        _sequence        = sequence;
        _customerService = customerService;
        _cache           = cache;
        _audit           = audit;
    }

    // GET /api/staff
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var staffRoles = AppRoles.StaffRoles.ToList();

        var staffUsers    = new List<object>();
        var employeeLinks = await _db.Employees
            .Where(e => e.AppUserId != null)
            .Select(e => new { e.AppUserId, e.Id, e.EmployeeNumber })
            .ToListAsync();

        foreach (var user in await _users.Users.ToListAsync())
        {
            var userRoles = await _users.GetRolesAsync(user);
            if (!userRoles.Any(r => staffRoles.Contains(r) && r != AppRoles.Customer))
                continue;

            var link = employeeLinks.FirstOrDefault(e => e.AppUserId == user.Id);

            staffUsers.Add(new {
                id             = user.Id,
                fullName       = user.FullName,
                email          = user.Email,
                phone          = user.PhoneNumber,
                isActive       = user.IsActive,
                roles          = userRoles.Where(r => r != AppRoles.Customer).ToList(),
                role           = GetPrimaryRole(userRoles),
                createdAt      = user.CreatedAt,
                employeeId     = link?.Id,
                employeeNumber = link?.EmployeeNumber,
            });
        }

        return Ok(staffUsers.OrderBy(s => ((dynamic)s).role));
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/staff
    // Ø¥Ù†Ø´Ø§Ø¡ Ù…ÙˆØ¸Ù Ø¬Ø¯ÙŠØ¯
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStaffDto dto)
    {
        // ØªØ­Ù‚Ù‚ Ù…Ù† ØµØ­Ø© Ø§Ù„Ø¯ÙˆØ±
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = $"Ø¯ÙˆØ± ØºÙŠØ± ØµØ§Ù„Ø­: {dto.Role}" });
        dto = dto with { Role = validRole };

        // ØªØ­Ù‚Ù‚ Ù…Ù† Ø§Ù„ØªÙƒØ±Ø§Ø±
        if (await _users.Users.AnyAsync(u => u.Email == dto.Email && u.UserName!.StartsWith("staff_")))
            return BadRequest(new { message = "Ø§Ù„Ø¥ÙŠÙ…ÙŠÙ„ Ù…Ø³ØªØ®Ø¯Ù… Ø¨Ø§Ù„ÙØ¹Ù„ Ù„Ù…ÙˆØ¸Ù Ø¢Ø®Ø±" });

        var phoneExists = await _users.Users.AnyAsync(u => u.PhoneNumber == dto.Phone && u.UserName!.StartsWith("staff_"));
        if (phoneExists)
            return BadRequest(new { message = "Ø±Ù‚Ù… Ø§Ù„ØªÙ„ÙŠÙÙˆÙ† Ù…Ø³ØªØ®Ø¯Ù… Ø¨Ø§Ù„ÙØ¹Ù„ Ù„Ù…ÙˆØ¸Ù Ø¢Ø®Ø±" });

        // Ø¥Ù†Ø´Ø§Ø¡ Ø§Ù„Ø£Ø¯ÙˆØ§Ø± Ù„Ùˆ Ù…Ø´ Ù…ÙˆØ¬ÙˆØ¯Ø©
        await EnsureRolesAsync();

        var user = new AppUser
        {
            UserName    = "staff_" + (!string.IsNullOrEmpty(dto.Phone) ? dto.Phone : dto.Email),
            Email       = dto.Email,
            FullName    = dto.FullName,
            PhoneNumber = dto.Phone,
            IsActive    = true,
            CreatedAt   = TimeHelper.GetEgyptTime(),
        };

        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _users.AddToRoleAsync(user, dto.Role);

        // Ø²Ø±Ø¹ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ø§ÙØªØ±Ø§Ø¶ÙŠØ© Ø¨Ù†Ø§Ø¡Ù‹ Ø¹Ù„Ù‰ Ø§Ù„Ø¯ÙˆØ±
        await _permService.SeedDefaultPermissionsAsync(user.Id, dto.Role);

        // âœ… Ø±Ø¨Ø· Ø£Ùˆ Ø¥Ù†Ø´Ø§Ø¡ Ø³Ø¬Ù„ HR ÙˆØªØ­Ø¶ÙŠØ± Ø§Ù„Ø­Ø³Ø§Ø¨Ø§Øª Ø§Ù„Ù…Ø­Ø§Ø³Ø¨ÙŠØ© (Ø§Ø®ØªÙŠØ§Ø±ÙŠ)
        if (dto.IsEmployee)
        {
            await EnsureEmployeeLinkAsync(user, dto.Role);
        }

        return Ok(new {
            id       = user.Id,
            fullName = user.FullName,
            role     = dto.Role,
            message  = $"ØªÙ… Ø¥Ù†Ø´Ø§Ø¡ {GetRoleAr(dto.Role)} Ø¨Ù†Ø¬Ø§Ø­ ÙˆÙ…Ø²Ø§Ù…Ù†ØªÙ‡ Ù…Ø¹ Ø§Ù„Ù€ HR"
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PUT /api/staff/{id}/role
    // ØªØºÙŠÙŠØ± Ø¯ÙˆØ± Ù…ÙˆØ¸Ù
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleDto dto)
    {
        var validRole = AppRoles.StaffRoles.FirstOrDefault(r => r.Equals(dto.Role, StringComparison.OrdinalIgnoreCase));
        if (validRole == null)
            return BadRequest(new { message = $"Ø¯ÙˆØ± ØºÙŠØ± ØµØ§Ù„Ø­: {dto.Role}" });
        dto = dto with { Role = validRole };

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        // Ù…Ù†Ø¹ ØªØºÙŠÙŠØ± Ø¯ÙˆØ± Ø§Ù„Ø£Ø¯Ù…Ù† Ø§Ù„Ø£ØµÙ„ÙŠ
        if (user.Email == "admin@sportive.com" && dto.Role != AppRoles.Admin)
            return BadRequest(new { message = "Ù„Ø§ ÙŠÙ…ÙƒÙ† ØªØºÙŠÙŠØ± Ø¯ÙˆØ± Ø§Ù„Ø£Ø¯Ù…Ù† Ø§Ù„Ø£ØµÙ„ÙŠ" });

        var currentRoles = await _users.GetRolesAsync(user);
        var staffRoles   = currentRoles.Where(r => r != AppRoles.Customer).ToList();

        // Ø§Ø­Ø°Ù Ø§Ù„Ø£Ø¯ÙˆØ§Ø± Ø§Ù„Ù‚Ø¯ÙŠÙ…Ø© ÙˆØ£Ø¶Ù Ø§Ù„Ø¬Ø¯ÙŠØ¯
        if (staffRoles.Any())
            await _users.RemoveFromRolesAsync(user, staffRoles);
        
        var result = await _users.AddToRoleAsync(user, dto.Role);
        if (!result.Succeeded)
            return BadRequest(new { message = "ÙØ´Ù„ ÙÙŠ ØªØºÙŠÙŠØ± Ø§Ù„Ø¯ÙˆØ±: " + result.Errors.FirstOrDefault()?.Description });

        // Ø¥Ø¹Ø§Ø¯Ø© Ø²Ø±Ø¹ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ø§ÙØªØ±Ø§Ø¶ÙŠØ© Ù„Ù„Ø¯ÙˆØ± Ø§Ù„Ø¬Ø¯ÙŠØ¯
        await _permService.SeedDefaultPermissionsAsync(user.Id, dto.Role);
        await _cache.RemoveAsync($"UserPermissions_{user.Id}");

        // âœ… ØªØ­Ø¯ÙŠØ« Ø³Ø¬Ù„ HR Ø§Ù„Ù…Ø±ØªØ¨Ø· Ø¥Ù† ÙˆØ¬Ø¯
        var hasEmployee = await _db.Employees.AnyAsync(e => e.AppUserId == user.Id);
        if (hasEmployee)
        {
            await EnsureEmployeeLinkAsync(user, dto.Role);
        }

        await _audit.LogAsync("ChangeRole", "User", user.Id, $"Role changed to {dto.Role}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        return Ok(new {
            id,
            newRole  = dto.Role,
            roleAr   = GetRoleAr(dto.Role),
            message  = "ØªÙ… ØªØºÙŠÙŠØ± Ø§Ù„Ø¯ÙˆØ± ÙˆØªØ­Ø¯ÙŠØ« Ø³Ø¬Ù„ Ø§Ù„Ù…ÙˆØ§Ø±Ø¯ Ø§Ù„Ø¨Ø´Ø±ÙŠØ© Ø¨Ù†Ø¬Ø§Ø­"
        });
    }

    private async Task EnsureEmployeeLinkAsync(AppUser user, string role)
    {
        // 1. Ø§Ø¨Ø­Ø« Ø¹Ù† Ù…ÙˆØ¸Ù Ù…Ø±ØªØ¨Ø· Ø¨Ù‡Ø°Ø§ Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù…
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == user.Id);
        
        if (employee == null)
        {
            // 2. Ø¥Ø°Ø§ Ù„Ù… ÙŠÙˆØ¬Ø¯ØŒ Ø§Ø¨Ø­Ø« Ø¹Ù† Ù…ÙˆØ¸Ù Ø¨Ù†ÙØ³ Ø§Ù„Ù‡Ø§ØªÙ ÙˆØºÙŠØ± Ù…Ø±ØªØ¨Ø· Ø¨Ù…Ø³ØªØ®Ø¯Ù… (Ø­Ø§Ù„Ø© Ø§Ø³ØªÙŠØ±Ø§Ø¯ Ø¨ÙŠØ§Ù†Ø§Øª Ù‚Ø¯ÙŠÙ…Ø©)
            employee = await _db.Employees.FirstOrDefaultAsync(e => e.Phone == user.PhoneNumber && (e.AppUserId == null || e.AppUserId == ""));
            
            if (employee != null)
            {
                employee.AppUserId = user.Id;
            }
            else
            {
                // 3. Ø¥Ù†Ø´Ø§Ø¡ Ø³Ø¬Ù„ Ø¬Ø¯ÙŠØ¯
                var empNo = await _sequence.NextAsync("EMP", async (db, pattern) =>
                {
                    var max = await db.Employees
                        .Where(e => EF.Functions.Like(e.EmployeeNumber, pattern))
                        .Select(e => e.EmployeeNumber).ToListAsync();
                    return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                              .DefaultIfEmpty(0).Max();
                });

                employee = new Employee
                {
                    EmployeeNumber = empNo,
                    Name = user.FullName,
                    Email = user.Email,
                    Phone = user.PhoneNumber,
                    AppUserId = user.Id,
                    JobTitle = role,
                    HireDate = TimeHelper.GetEgyptTime(),
                    Status = EmployeeStatus.Active,
                    CreatedAt = TimeHelper.GetEgyptTime()
                };
                _db.Employees.Add(employee);
            }
        }
        
        // ØªØ­Ø¯ÙŠØ« Ø§Ù„Ø¨ÙŠØ§Ù†Ø§Øª Ø§Ù„Ø£Ø³Ø§Ø³ÙŠØ© Ù„Ø¶Ù…Ø§Ù† Ø§Ù„ØªØ·Ø§Ø¨Ù‚
        employee.Name = user.FullName;
        employee.Email = user.Email;
        employee.Phone = user.PhoneNumber;
        employee.JobTitle = role; // Ø§Ù„Ù…Ø³Ù…Ù‰ Ø§Ù„ÙˆØ¸ÙŠÙÙŠ ÙŠØªØ¨Ø¹ Ø§Ù„Ø¯ÙˆØ±

        await _db.SaveChangesAsync();
        await _customerService.EnsureCustomerAccountAsync(0, isEmployee: true, employeeId: employee.Id);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/staff/{id}/link-employee
    // Ø±Ø¨Ø· Ù…Ø³ØªØ®Ø¯Ù… Ø­Ø§Ù„ÙŠ Ø¨Ø³Ø¬Ù„ Ù…ÙˆØ¸Ù (ÙŠØ¯ÙˆÙŠØ§Ù‹)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost("{id}/link-employee")]
    public async Task<IActionResult> LinkEmployee(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);
        var role = GetPrimaryRole(roles);

        await EnsureEmployeeLinkAsync(user, role);
        return Ok(new { message = "ØªÙ… Ø±Ø¨Ø· Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø¨Ø³Ø¬Ù„ Ø§Ù„Ù…ÙˆØ¸ÙÙŠÙ† ÙˆØ§Ù„Ù…Ø­Ø§Ø³Ø¨Ø© Ø¨Ù†Ø¬Ø§Ø­" });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PATCH /api/staff/{id}/toggle-active
    // ØªÙØ¹ÙŠÙ„ / ØªØ¹Ø·ÙŠÙ„ Ø§Ù„Ù…ÙˆØ¸Ù
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPatch("{id}/toggle-active")]
    public async Task<IActionResult> ToggleActive(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = "Ù„Ø§ ÙŠÙ…ÙƒÙ† ØªØ¹Ø·ÙŠÙ„ Ø§Ù„Ø£Ø¯Ù…Ù† Ø§Ù„Ø£ØµÙ„ÙŠ" });

        user.IsActive = !user.IsActive;
        await _users.UpdateAsync(user);

        return Ok(new { isActive = user.IsActive });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PUT /api/staff/{id}/reset-password
    // Ø¥Ø¹Ø§Ø¯Ø© ØªØ¹ÙŠÙŠÙ† ÙƒÙ„Ù…Ø© Ù…Ø±ÙˆØ± Ù…ÙˆØ¸Ù
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPut("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] StaffResetPasswordDto dto)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var token  = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, dto.NewPassword);

        return result.Succeeded
            ? Ok(new { message = "ØªÙ… ØªØºÙŠÙŠØ± ÙƒÙ„Ù…Ø© Ø§Ù„Ù…Ø±ÙˆØ± Ø¨Ù†Ø¬Ø§Ø­" })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // DELETE /api/staff/{id}
    // Ø­Ø°Ù Ù…ÙˆØ¸Ù Ù†Ù‡Ø§Ø¦ÙŠØ§Ù‹ Ø£Ùˆ ØªØ¹Ø·ÙŠÙ„Ù‡
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (user.Email == "admin@sportive.com")
            return BadRequest(new { message = "Ù„Ø§ ÙŠÙ…ÙƒÙ† Ø­Ø°Ù Ø§Ù„Ø£Ø¯Ù…Ù† Ø§Ù„Ø£ØµÙ„ÙŠ" });

        // Ù†ÙØ¶Ù„ Ø§Ù„ØªØ¹Ø·ÙŠÙ„ (Soft Delete) Ù„Ùˆ ÙƒØ§Ù† Ù‡Ù†Ø§Ùƒ Ø³Ø¬Ù„Ø§Øª Ù…Ø±ØªØ¨Ø·Ø© Ø¨Ù‡
        var hasOrders = await _db.Orders.AnyAsync(o => o.SalesPersonId == id);
        if (hasOrders)
        {
            user.IsActive = false;
            await _users.UpdateAsync(user);
            return Ok(new { message = "ØªÙ… ØªØ¹Ø·ÙŠÙ„ Ø§Ù„Ø­Ø³Ø§Ø¨ Ø¨Ø¯Ù„Ø§Ù‹ Ù…Ù† Ø§Ù„Ø­Ø°Ù Ù„ÙˆØ¬ÙˆØ¯ Ø³Ø¬Ù„Ø§Øª Ù…Ø¨ÙŠØ¹Ø§Øª Ù…Ø±ØªØ¨Ø·Ø© Ø¨Ù‡" });
        }

        // ÙÙƒ Ø§Ù„Ø±Ø¨Ø· Ù…Ø¹ Ø³Ø¬Ù„ Ø§Ù„Ù€ HR (Ù…Ø¹ Ø§Ù„Ø­ÙØ§Ø¸ Ø¹Ù„Ù‰ Ø§Ù„Ø³Ø¬Ù„)
        var linkedEmployee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == id);
        if (linkedEmployee != null)
        {
            linkedEmployee.AppUserId = null;
            linkedEmployee.UpdatedAt = TimeHelper.GetEgyptTime();
        }

        // Ø­Ø°Ù Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª
        var perms = await _db.UserModulePermissions.Where(p => p.UserAccountID == id).ToListAsync();
        _db.UserModulePermissions.RemoveRange(perms);
        await _db.SaveChangesAsync();

        var result = await _users.DeleteAsync(user);
        return result.Succeeded
            ? Ok(new { message = "ØªÙ… Ø­Ø°Ù Ø§Ù„Ù…ÙˆØ¸Ù Ø¨Ù†Ø¬Ø§Ø­" })
            : BadRequest(new { errors = result.Errors.Select(e => e.Description) });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/staff/{id}/permissions
    // Ø¬Ù„Ø¨ ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… Ø§Ù„Ø®Ø§ØµØ©
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var perms = await _db.UserModulePermissions
            .Where(p => p.UserAccountID == id)
            .Select(p => new { p.ModuleKey, p.CanView, p.CanEdit })
            .ToListAsync();

        return Ok(perms);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PUT /api/staff/{id}/permissions
    // ØªØ­Ø¯ÙŠØ« ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ù…Ø³ØªØ®Ø¯Ù… (Ø§Ù„Ø­Ù‚ ÙÙ‰ Ø§Ù„Ø±Ø¤ÙŠØ© Ø£Ùˆ Ø§Ù„ØªØ­ÙƒÙ…)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPut("{id}/permissions")]
    public async Task<IActionResult> UpdatePermissions(string id, [FromBody] List<UserModulePermissionDto> dto)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        // Ø­Ø°Ù Ø§Ù„Ù‚Ø¯ÙŠÙ…
        var existing = await _db.UserModulePermissions.Where(p => p.UserAccountID == id).ToListAsync();
        _db.UserModulePermissions.RemoveRange(existing);

        // Ø¥Ø¶Ø§ÙØ© Ø§Ù„Ø¬Ø¯ÙŠØ¯
        foreach (var p in dto)
        {
            _db.UserModulePermissions.Add(new UserModulePermission
            {
                UserAccountID = id,
                ModuleKey     = p.ModuleKey,
                CanView       = p.CanView,
                CanEdit       = p.CanEdit
            });
        }

        await _db.SaveChangesAsync();
        await _cache.RemoveAsync($"UserPermissions_{id}");
        
        await _audit.LogAsync("UpdatePermissions", "UserModulePermission", id, "Permissions updated manually", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Name));

        return Ok(new { message = "تم تحديث الصلاحيات بنجاح" });
    }

    // ==================================================================================================
    // GET /api/staff/{id}/profile
    // بروفايل كامل: بيانات المستخدم + HR + الصلاحيات
    // ==================================================================================================
    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound();

        var roles = await _users.GetRolesAsync(user);

        var employee = await _db.Employees
            .Where(e => e.AppUserId == id)
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.Name, e.JobTitle, e.Department,
                e.BaseSalary, e.HireDate, e.Status, e.Phone, e.Email
            })
            .FirstOrDefaultAsync();

        var perms = await _db.UserModulePermissions
            .Where(p => p.UserAccountID == id)
            .Select(p => new { p.ModuleKey, p.CanView, p.CanEdit })
            .ToListAsync();

        return Ok(new {
            id          = user.Id,
            fullName    = user.FullName,
            email       = user.Email,
            phone       = user.PhoneNumber,
            isActive    = user.IsActive,
            createdAt   = user.CreatedAt,
            roles       = roles.Where(r => r != AppRoles.Customer).ToList(),
            primaryRole = GetPrimaryRole(roles),
            employee,
            permissions = perms,
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // POST /api/staff/backfill-permissions
    // Ø²Ø±Ø¹ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ø§ÙØªØ±Ø§Ø¶ÙŠØ© Ù„Ù„Ù…Ø³ØªØ®Ø¯Ù…ÙŠÙ† Ø§Ù„Ù‚Ø¯Ø§Ù…Ù‰
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpPost("backfill-permissions")]
    [RequirePermission(ModuleKeys.Staff, requireEdit: true)]
    public async Task<IActionResult> BackfillPermissions()
    {
        await _permService.BackfillMissingPermissionsAsync(_users);
        return Ok(new { message = "ØªÙ… Ø²Ø±Ø¹ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª Ø§Ù„Ø§ÙØªØ±Ø§Ø¶ÙŠØ© Ù„Ù„Ù…Ø³ØªØ®Ø¯Ù…ÙŠÙ†." });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // GET /api/staff/roles
    // Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„Ø£Ø¯ÙˆØ§Ø± Ø§Ù„Ù…ØªØ§Ø­Ø©
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("roles")]
    public IActionResult GetRoles()
    {
        return Ok(new[]
        {
            new { value = AppRoles.Admin,      labelAr = "Ù…Ø¯ÙŠØ± Ø§Ù„Ù†Ø¸Ø§Ù…",      labelEn = "System Admin",  permissions = "ÙƒÙ„ Ø§Ù„ØµÙ„Ø§Ø­ÙŠØ§Øª" },
            new { value = AppRoles.Manager,    labelAr = "Ù…Ø¯ÙŠØ± Ø§Ù„ÙØ±Ø¹",       labelEn = "Branch Manager", permissions = "ÙƒÙ„ Ø´ÙŠØ¡ Ù…Ø§ Ø¹Ø¯Ø§ Ø¥Ø¹Ø¯Ø§Ø¯Ø§Øª Ø§Ù„Ù†Ø¸Ø§Ù…" },
            new { value = AppRoles.Cashier,    labelAr = "ÙƒØ§Ø´ÙŠØ±",            labelEn = "Cashier",        permissions = "POS ÙÙ‚Ø·" },
            new { value = AppRoles.Accountant, labelAr = "Ù…Ø­Ø§Ø³Ø¨",            labelEn = "Accountant",     permissions = "Ø§Ù„ØªÙ‚Ø§Ø±ÙŠØ± Ø§Ù„Ù…Ø§Ù„ÙŠØ© ÙˆØ§Ù„Ù…Ø­Ø§Ø³Ø¨Ø©" },
            new { value = AppRoles.Staff,      labelAr = "Ù…ÙˆØ¸Ù",             labelEn = "Staff",          permissions = "Ø§Ù„Ø·Ù„Ø¨Ø§Øª ÙˆØ§Ù„Ù…Ù†ØªØ¬Ø§Øª (Ø¹Ø±Ø¶)" },
        });
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    private async Task EnsureRolesAsync()
    {
        foreach (var role in AppRoles.StaffRoles)
            if (!await _roles.RoleExistsAsync(role))
                await _roles.CreateAsync(new IdentityRole(role));
    }

    private static string GetPrimaryRole(IList<string> roles)
    {
        foreach (var r in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Accountant, AppRoles.Cashier, AppRoles.Staff })
            if (roles.Contains(r)) return r;
        return AppRoles.Customer;
    }

    private static string GetRoleAr(string role) => role switch
    {
        AppRoles.Admin      => "Ù…Ø¯ÙŠØ± Ø§Ù„Ù†Ø¸Ø§Ù…",
        AppRoles.Manager    => "Ù…Ø¯ÙŠØ± Ø§Ù„ÙØ±Ø¹",
        AppRoles.Cashier    => "ÙƒØ§Ø´ÙŠØ±",
        AppRoles.Accountant => "Ù…Ø­Ø§Ø³Ø¨",
        AppRoles.Staff      => "Ù…ÙˆØ¸Ù",
        _                   => role
    };
}

// â”€â”€ DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
public record CreateStaffDto(
    string FullName,
    string Email,
    string Phone,
    string Password,
    string Role,
    bool IsEmployee = true
);

public record ChangeRoleDto(string Role);
public record StaffResetPasswordDto(string NewPassword);
public record UserModulePermissionDto(string ModuleKey, bool CanView, bool CanEdit);

