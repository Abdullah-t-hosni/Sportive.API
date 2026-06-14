using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [RequirePermission(ModuleKeys.Pos)]
    public class POSShiftClosuresController : ControllerBase
    {
        private readonly AppDbContext _db;

        public POSShiftClosuresController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePOSShiftClosureDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                int? resolvedBranchId = User.GetBranchId();
                bool canChangeBranch = await User.HasViewAllBranchesAsync(HttpContext);
                if (canChangeBranch && dto.BranchId.HasValue)
                {
                    resolvedBranchId = dto.BranchId.Value;
                }

                var closure = new POSShiftClosure
                {
                    StationId = dto.StationId,
                    BranchId = resolvedBranchId,
                    ClosureDate = dto.ClosureDate,
                    ClosedBy = !string.IsNullOrEmpty(dto.ClosedBy) ? dto.ClosedBy : (User.Identity?.Name ?? "Cashier"),
                    StartingBalance = dto.StartingBalance,
                    ExpectedCash = dto.ExpectedCash,
                    ActualCash = dto.ActualCash,
                    Variance = dto.Variance,
                    GrossSales = dto.GrossSales,
                    NetSales = dto.NetSales,
                    CashSales = dto.CashSales,
                    CardSales = dto.CardSales,
                    VodafoneCashSales = dto.VodafoneCashSales,
                    InstapaySales = dto.InstapaySales,
                    WalletSales = dto.WalletSales,
                    CreditSales = dto.CreditSales,
                    Expenses = dto.Expenses,
                    SafeDrops = dto.SafeDrops,
                    Returns = dto.Returns,
                    Discounts = dto.Discounts,
                    JournalEntryReference = dto.JournalEntryReference
                };

                _db.POSShiftClosures.Add(closure);
                await _db.SaveChangesAsync();

                return Ok(closure);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to create shift closure", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? date = null, [FromQuery] string? stationId = null, [FromQuery] int? branchId = null)
        {
            try
            {
                var query = _db.POSShiftClosures.AsQueryable();

                bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
                if (!canViewAll)
                {
                    int? isolatedBranchId = User.GetBranchId();
                    if (isolatedBranchId.HasValue)
                    {
                        query = query.Where(c => c.BranchId == isolatedBranchId.Value);
                    }
                }
                else if (branchId.HasValue)
                {
                    query = query.Where(c => c.BranchId == branchId.Value);
                }

                if (!string.IsNullOrEmpty(date))
                {
                    query = query.Where(c => c.ClosureDate == date);
                }

                if (!string.IsNullOrEmpty(stationId))
                {
                    query = query.Where(c => c.StationId == stationId);
                }

                var closures = await query
                    .Include(c => c.Branch)
                    .OrderBy(c => c.CreatedAt)
                    .ToListAsync();
                return Ok(closures);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to retrieve shift closures", error = ex.Message });
            }
        }
    }
}
