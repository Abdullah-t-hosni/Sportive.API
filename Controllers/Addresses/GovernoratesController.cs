using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Controllers.Addresses;

[ApiController]
[Route("api/addresses/governorates")]
[AllowAnonymous]
public class GovernoratesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICacheService _cache;

    public GovernoratesController(AppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> GetGovernorates()
    {
        var cached = await _cache.GetAsync<object>("Governorates_All");
        if (cached != null) return Ok(new { success = true, data = cached });

        var governorates = await _db.Governorates
            .AsNoTracking()
            .OrderBy(g => g.NameAr)
            .Select(g => new { g.Id, g.BostaId, g.NameAr, g.NameEn, g.Code })
            .ToListAsync();

        await _cache.SetAsync("Governorates_All", governorates, TimeSpan.FromMinutes(30));
        return Ok(new { success = true, data = governorates });
    }

    [HttpGet("{id:int}/districts")]
    public async Task<IActionResult> GetDistricts(int id)
    {
        var cacheKey = $"Districts_Gov_{id}";
        var cached = await _cache.GetAsync<object>(cacheKey);
        if (cached != null) return Ok(new { success = true, data = cached });

        var districts = await _db.Districts
            .AsNoTracking()
            .Where(d => d.GovernorateId == id)
            .OrderBy(d => d.NameAr)
            .Select(d => new { d.Id, d.BostaId, d.NameAr, d.NameEn, d.GovernorateId })
            .ToListAsync();

        await _cache.SetAsync(cacheKey, districts, TimeSpan.FromMinutes(30));
        return Ok(new { success = true, data = districts });
    }
}
