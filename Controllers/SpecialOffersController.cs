using Sportive.API.Interfaces;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.Promotions, requireEdit: true)]
public class SpecialOffersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITranslator _t;

    public SpecialOffersController(AppDbContext db, ITranslator t)
    {
        _db = db;
        _t = t;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.SpecialOffers
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var offer = await _db.SpecialOffers.FindAsync(id);
        if (offer == null) return NotFound();
        return Ok(offer);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SpecialOfferDto dto)
    {
        if (dto.ValidFrom >= dto.ValidTo)
            return BadRequest(new { message = _t.Get("Discounts.StartDateBeforeEndDate") });

        var offer = new SpecialOffer
        {
            Name = dto.Name,
            Description = dto.Description,
            ThresholdQuantity = dto.ThresholdQuantity,
            DiscountPercentage = dto.DiscountPercentage,
            IsFullDiscount = dto.IsFullDiscount,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            IsActive = dto.IsActive,
            ApplyTo = dto.ApplyTo
        };

        _db.SpecialOffers.Add(offer);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = offer.Id }, offer);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SpecialOfferDto dto)
    {
        var offer = await _db.SpecialOffers.FindAsync(id);
        if (offer == null) return NotFound();

        offer.Name = dto.Name;
        offer.Description = dto.Description;
        offer.ThresholdQuantity = dto.ThresholdQuantity;
        offer.DiscountPercentage = dto.DiscountPercentage;
        offer.IsFullDiscount = dto.IsFullDiscount;
        offer.ValidFrom = dto.ValidFrom;
        offer.ValidTo = dto.ValidTo;
        offer.IsActive = dto.IsActive;
        offer.ApplyTo = dto.ApplyTo;
        offer.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(offer);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var offer = await _db.SpecialOffers.FindAsync(id);
        if (offer == null) return NotFound();
        _db.SpecialOffers.Remove(offer);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record SpecialOfferDto(
    string Name,
    string? Description,
    int ThresholdQuantity,
    decimal DiscountPercentage,
    bool IsFullDiscount,
    DateTime ValidFrom,
    DateTime ValidTo,
    bool IsActive,
    DiscountApplyTo ApplyTo
);
