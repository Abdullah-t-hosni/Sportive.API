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
using System.Collections.Generic;
using Sportive.API.Utils;

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

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,SuperAdmin,Manager")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var closure = await _db.POSShiftClosures.FindAsync(id);
                if (closure == null)
                {
                    return NotFound(new { message = "Shift closure not found" });
                }

                // Delete associated journal entry if reference is provided
                if (!string.IsNullOrEmpty(closure.JournalEntryReference))
                {
                    var journalEntry = await _db.JournalEntries
                        .Include(j => j.Lines)
                        .FirstOrDefaultAsync(j => j.Reference == closure.JournalEntryReference);

                    if (journalEntry != null)
                    {
                        _db.JournalLines.RemoveRange(journalEntry.Lines);
                        _db.JournalEntries.Remove(journalEntry);
                    }
                }

                _db.POSShiftClosures.Remove(closure);
                await _db.SaveChangesAsync();

                return Ok(new { message = "Shift closure deleted successfully and associated accounting entries reversed" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to delete shift closure", error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,SuperAdmin,Manager")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdatePOSShiftClosureDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var closure = await _db.POSShiftClosures.FindAsync(id);
                if (closure == null)
                {
                    return NotFound(new { message = "Shift closure not found" });
                }

                closure.StartingBalance = dto.StartingBalance;
                closure.ActualCash = dto.ActualCash;
                closure.ExpectedCash = dto.ExpectedCash;
                closure.Variance = dto.Variance;
                closure.GrossSales = dto.GrossSales;
                closure.NetSales = dto.NetSales;
                closure.CashSales = dto.CashSales;
                closure.CardSales = dto.CardSales;
                closure.VodafoneCashSales = dto.VodafoneCashSales;
                closure.InstapaySales = dto.InstapaySales;
                closure.WalletSales = dto.WalletSales;
                closure.CreditSales = dto.CreditSales;
                closure.Expenses = dto.Expenses;
                closure.SafeDrops = dto.SafeDrops;
                closure.Returns = dto.Returns;
                closure.Discounts = dto.Discounts;
                closure.UpdatedAt = TimeHelper.GetEgyptTime();

                if (!string.IsNullOrEmpty(closure.JournalEntryReference))
                {
                    var journalEntry = await _db.JournalEntries
                        .Include(j => j.Lines)
                        .FirstOrDefaultAsync(j => j.Reference == closure.JournalEntryReference);

                    if (journalEntry != null)
                    {
                        var mappings = await _db.AccountSystemMappings
                            .AsNoTracking()
                            .ToDictionaryAsync(m => m.Key.ToLower(), m => m.AccountId);

                        mappings.TryGetValue(MappingKeys.PosCash.ToLower(), out var posCashId);
                        mappings.TryGetValue(MappingKeys.Cash.ToLower(), out var mainCashId);
                        var effectiveDrawerId = (posCashId != 0 && posCashId != null) ? posCashId.Value : (mainCashId != 0 && mainCashId != null ? mainCashId.Value : 0);

                        mappings.TryGetValue(MappingKeys.PosDailyClosure.ToLower(), out var closureAccountIdVal);
                        var closureAccountId = (closureAccountIdVal != 0 && closureAccountIdVal != null) ? closureAccountIdVal.Value : 0;

                        int? overShortId = null;
                        if (mappings.TryGetValue("overshortaccountid", out var id1) && id1.HasValue) overShortId = id1.Value;
                        else if (mappings.TryGetValue("surplusshortageaccountid", out var id2) && id2.HasValue) overShortId = id2.Value;
                        
                        if (overShortId == null || overShortId == 0)
                        {
                            var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.NameAr.Contains("عجز") || a.NameAr.Contains("زيادة") || a.NameEn.Contains("Short") || a.NameEn.Contains("Overage"));
                            if (acc != null) overShortId = acc.Id;
                        }

                        _db.JournalLines.RemoveRange(journalEntry.Lines);
                        journalEntry.Lines.Clear();

                        var roundedExpected = Math.Round(dto.ExpectedCash * 100) / 100;
                        var roundedActual = Math.Round(dto.ActualCash * 100) / 100;
                        var roundedVariance = Math.Round((roundedActual - roundedExpected) * 100) / 100;
                        var hasVariance = Math.Abs(roundedVariance) >= 0.01m;

                        var lines = new List<JournalLine>();

                        lines.Add(new JournalLine
                        {
                            AccountId = closureAccountId != 0 ? closureAccountId : 0,
                            Debit = roundedActual,
                            Credit = 0,
                            Description = $"إغلاق وتسوية يومية لوردية الكاشير - {closure.ClosureDate}",
                            BranchId = closure.BranchId
                        });

                        if (hasVariance && overShortId.HasValue && overShortId.Value != 0)
                        {
                            if (roundedVariance > 0)
                            {
                                lines.Add(new JournalLine
                                {
                                    AccountId = effectiveDrawerId,
                                    Debit = 0,
                                    Credit = roundedExpected,
                                    Description = "تصفية رصيد درج الكاشير للربط بالدفاتر",
                                    BranchId = closure.BranchId
                                });
                                lines.Add(new JournalLine
                                {
                                    AccountId = overShortId.Value,
                                    Debit = 0,
                                    Credit = roundedVariance,
                                    Description = $"التحقق من الفائض أو العجز في الدرج - زيادة بقيمة {roundedVariance}",
                                    BranchId = closure.BranchId
                                });
                            }
                            else
                            {
                                lines.Add(new JournalLine
                                {
                                    AccountId = overShortId.Value,
                                    Debit = Math.Abs(roundedVariance),
                                    Credit = 0,
                                    Description = $"التحقق من الفائض أو العجز في الدرج - عجز بقيمة {Math.Abs(roundedVariance)}",
                                    BranchId = closure.BranchId
                                });
                                lines.Add(new JournalLine
                                {
                                    AccountId = effectiveDrawerId,
                                    Debit = 0,
                                    Credit = roundedExpected,
                                    Description = "تصفية رصيد درج الكاشير للربط بالدفاتر",
                                    BranchId = closure.BranchId
                                });
                            }
                        }
                        else
                        {
                            lines.Add(new JournalLine
                            {
                                AccountId = effectiveDrawerId,
                                Debit = 0,
                                Credit = roundedExpected,
                                Description = "تصفية رصيد درج الكاشير للربط بالدفاتر",
                                BranchId = closure.BranchId
                            });
                        }

                        foreach (var l in lines)
                        {
                            journalEntry.Lines.Add(l);
                        }

                        journalEntry.UpdatedAt = TimeHelper.GetEgyptTime();
                    }
                }

                await _db.SaveChangesAsync();
                return Ok(closure);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Failed to update shift closure", error = ex.Message });
            }
        }
    }
}
