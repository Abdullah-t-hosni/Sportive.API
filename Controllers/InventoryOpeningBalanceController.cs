using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;
using System.Security.Claims;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class InventoryOpeningBalanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SequenceService _seq;
    private readonly IAccountingService _accounting;
    private readonly IInventoryService _inventory;

    public InventoryOpeningBalanceController(
        AppDbContext db, 
        SequenceService seq, 
        IAccountingService accounting,
        IInventoryService inventory)
    {
        _db = db;
        _seq = seq;
        _accounting = accounting;
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.InventoryOpeningBalances.AsNoTracking();
        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OpeningBalanceSummaryDto(
                x.Id, x.Reference, x.Date, x.TotalValue, x.Items.Count
            ))
            .ToListAsync();

        return Ok(new PaginatedResult<OpeningBalanceSummaryDto>(items, total, page, pageSize, (int)Math.Ceiling((double)total / pageSize)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var x = await _db.InventoryOpeningBalances
            .Include(b => b.Items).ThenInclude(i => i.Product)
            .Include(b => b.Items).ThenInclude(i => i.ProductVariant)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (x == null) return NotFound();

        return Ok(new OpeningBalanceDetailDto(
            x.Id, x.Reference, x.Date, x.Notes, x.TotalValue,
            x.Items.Select(i => new OpeningBalanceItemDto(
                i.Id, i.ProductId, i.Product?.NameAr, i.Product?.SKU,
                i.ProductVariantId, i.ProductVariant?.Size, i.ProductVariant?.ColorAr,
                i.Quantity, i.CostPrice, i.TotalCost
            )).ToList()
        ));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOpeningBalanceDto dto)
    {
        if (dto.Items == null || !dto.Items.Any())
            return BadRequest(new { message = "يجب إضافة صنف واحد على الأقل" });

        var refNo = await _seq.NextAsync("OB", async (db, pattern) =>
        {
            var max = await db.InventoryOpeningBalances
                .Where(x => EF.Functions.Like(x.Reference, pattern))
                .Select(x => x.Reference)
                .ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

        var ob = new InventoryOpeningBalance
        {
            Reference = refNo,
            Date      = dto.Date,
            Notes     = dto.Notes,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        decimal totalValue = 0;
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        foreach (var item in dto.Items)
        {
            var total = item.Quantity * item.CostPrice;
            ob.Items.Add(new InventoryOpeningBalanceItem
            {
                ProductId = item.ProductId,
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                CostPrice = item.CostPrice,
                CreatedAt = TimeHelper.GetEgyptTime()
            });
            totalValue += total;

            // 1. Update Inventory
            if (item.ProductId.HasValue)
            {
                await _inventory.LogMovementAsync(
                    InventoryMovementType.OpeningBalance, 
                    item.Quantity, 
                    item.ProductId, 
                    item.ProductVariantId, 
                    refNo, 
                    "ارصدة افتتاحية", 
                    userId,
                    item.CostPrice
                );

                // 2. Update Product Cost Price if provided
                var product = await _db.Products.FindAsync(item.ProductId.Value);
                if (product != null && item.CostPrice > 0)
                {
                    product.CostPrice = item.CostPrice;
                    product.UpdatedAt = TimeHelper.GetEgyptTime();
                }
            }
        }

        ob.TotalValue = totalValue;
        _db.InventoryOpeningBalances.Add(ob);
        await _db.SaveChangesAsync();

        // 3. Post Accounting Journal
        await PostJournalAsync(ob);

        return CreatedAtAction(nameof(GetById), new { id = ob.Id }, new { id = ob.Id, reference = ob.Reference });
    }

    private async Task PostJournalAsync(InventoryOpeningBalance ob)
    {
        // Get Inventory Account
        var inventoryAcct = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1106");
        // Get Opening Balances Account (Capital or dedicated Opening Balances)
        var openingAcct   = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "3101") 
                        ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "3");

        if (inventoryAcct == null || openingAcct == null) return;

        var dto = new CreateJournalEntryDto(
            EntryDate:   ob.Date,
            Reference:   ob.Reference,
            Description: $"قيد إثبات أرصدة افتتاحية للمخزون - {ob.Reference}",
            Lines:       new List<CreateJournalLineDto>
            {
                new (inventoryAcct.Id, ob.TotalValue, 0, "إثبات قيمة المخزون الافتتاحي"),
                new (openingAcct.Id,   0, ob.TotalValue, "حساب الأرصدة الافتتاحية / رأس المال")
            },
            Type:        JournalEntryType.OpeningBalance
        );

        await _accounting.PostManualEntryAsync(dto, User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}
