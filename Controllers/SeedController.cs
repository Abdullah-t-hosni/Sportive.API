using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

/// <summary>
/// POST /api/seed/admin
/// يُنشئ يوزر الأدمن بـ hash صحيح — استخدمه مرة واحدة بعد إعادة الضبط
/// احذف هذا الـ endpoint بعد الاستخدام أو اتركه محمياً بـ secret key
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SeedController : ControllerBase
{
    private readonly UserManager<AppUser>  _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AppDbContext _db;

    public SeedController(
        UserManager<AppUser>  users,
        RoleManager<IdentityRole> roles,
        AppDbContext db)
    {
        _users = users;
        _roles = roles;
        _db    = db;
    }

    // POST /api/seed/admin
    // Body: { "secretKey": "sportive-reset-2025" }
    [HttpPost("admin")]
    public async Task<IActionResult> CreateAdmin([FromBody] SeedAdminDto dto)
    {
        // حماية بسيطة — key سري
        const string SECRET = "sportive-reset-2025";
        if (dto.SecretKey != SECRET)
            return Unauthorized(new { message = "Secret key غلط" });

        // تحقق ما فيش يوزر موجود
        var existing = await _users.FindByEmailAsync("admin@sportive.com");
        if (existing != null)
            return BadRequest(new { message = "الأدمن موجود بالفعل" });

        // تأكد الأدوار موجودة
        foreach (var role in new[] { "Admin", "Customer", "Staff", "Cashier" })
        {
            if (!await _roles.RoleExistsAsync(role))
                await _roles.CreateAsync(new IdentityRole(role));
        }

        // إنشاء الأدمن
        var admin = new AppUser
        {
            UserName      = "admin@sportive.com",
            Email         = "admin@sportive.com",
            FirstName     = "Admin",
            LastName      = "Sportive",
            PhoneNumber   = "01000000000",
            IsActive      = true,
            EmailConfirmed= true,
        };

        var result = await _users.CreateAsync(admin, "Admin@123456");
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        await _users.AddToRoleAsync(admin, "Admin");

        return Ok(new {
            message  = "✅ تم إنشاء الأدمن بنجاح",
            email    = "admin@sportive.com",
            password = "Admin@123456",
            note     = "غيّر الـ Password بعد أول تسجيل دخول",
        });
    }
}

public record SeedAdminDto(string SecretKey);
