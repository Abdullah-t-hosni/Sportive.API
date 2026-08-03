using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;

namespace Sportive.API.Controllers.Addresses;

[ApiController]
[Route("api/addresses/governorates")]
[AllowAnonymous]
public class GovernoratesController : ControllerBase
{
    private readonly AppDbContext _db;

    public GovernoratesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetGovernorates()
    {
        var governorates = await _db.Governorates
            .OrderBy(g => g.NameAr)
            .Select(g => new { g.Id, g.BostaId, g.NameAr, g.NameEn, g.Code })
            .ToListAsync();

        return Ok(new { success = true, data = governorates });
    }

    [HttpGet("{id:int}/districts")]
    public async Task<IActionResult> GetDistricts(int id)
    {
        var districts = await _db.Districts
            .Where(d => d.GovernorateId == id)
            .OrderBy(d => d.NameAr)
            .Select(d => new { d.Id, d.BostaId, d.NameAr, d.NameEn, d.GovernorateId })
            .ToListAsync();

        return Ok(new { success = true, data = districts });
    }
}
