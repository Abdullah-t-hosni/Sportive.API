using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

// ══════════════════════════════════════════════════════
// 1. FIXED ASSET CATEGORIES
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/fixed-asset-categories")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class FixedAssetCategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public FixedAssetCategoriesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.FixedAssetCategories
            .Include(c => c.AssetAccount)
            .Include(c => c.AccumDepreciationAccount)
            .Include(c => c.DepreciationExpenseAccount)
            .Include(c => c.Assets)
            .OrderBy(c => c.Name)
            .Select(c => new FixedAssetCategoryDto(
                c.Id, c.Name, c.Description, c.IsActive,
                c.AssetAccountId,              c.AssetAccount != null              ? c.AssetAccount.NameAr              : null,
                c.AccumDepreciationAccountId,  c.AccumDepreciationAccount != null  ? c.AccumDepreciationAccount.NameAr  : null,
                c.DepreciationExpenseAccountId,c.DepreciationExpenseAccount != null ? c.DepreciationExpenseAccount.NameAr : null,
                c.Assets.Count
            )).ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFixedAssetCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("اسم الفئة مطلوب.");

        var cat = new FixedAssetCategory
        {
            Name                         = dto.Name.Trim(),
            Description                  = dto.Description?.Trim(),
            AssetAccountId               = dto.AssetAccountId,
            AccumDepreciationAccountId   = dto.AccumDepreciationAccountId,
            DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId,
            CreatedAt                    = TimeHelper.GetEgyptTime()
        };
        _db.FixedAssetCategories.Add(cat);
        await _db.SaveChangesAsync();
        return Ok(new { id = cat.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFixedAssetCategoryDto dto)
    {
        var cat = await _db.FixedAssetCategories.FindAsync(id);
        if (cat == null) return NotFound();

        cat.Name                         = dto.Name.Trim();
        cat.Description                  = dto.Description?.Trim();
        cat.IsActive                     = dto.IsActive;
        cat.AssetAccountId               = dto.AssetAccountId;
        cat.AccumDepreciationAccountId   = dto.AccumDepreciationAccountId;
        cat.DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId;
        cat.UpdatedAt                    = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cat = await _db.FixedAssetCategories
            .Include(c => c.Assets)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return NotFound();
        if (cat.Assets.Any())
            return BadRequest("لا يمكن حذف فئة تحتوي على أصول.");

        _db.FixedAssetCategories.Remove(cat);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

// ══════════════════════════════════════════════════════
// 2. FIXED ASSETS
// ══════════════════════════════════════════════════════

[ApiController]
[Route("api/fixed-assets")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class FixedAssetsController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly SequenceService _seq;

    public FixedAssetsController(AppDbContext db, SequenceService seq)
    {
        _db  = db;
        _seq = seq;
    }

    // ─── helpers ──────────────────────────────────────

    /// <summary>حساب قسط الإهلاك الشهري بطريقة القسط الثابت</summary>
    private static decimal CalcStraightLineMonthly(FixedAsset a)
    {
        var depreciable = a.PurchaseCost - a.SalvageValue;
        if (depreciable <= 0 || a.UsefulLifeYears <= 0) return 0;
        return Math.Round(depreciable / (a.UsefulLifeYears * 12), 2);
    }

    /// <summary>
    /// يرجع حسابات الأصل: أولاً من الأصل نفسه، وإن لم تكن فمن الفئة.
    /// </summary>
    private (int? assetAcc, int? accumAcc, int? expenseAcc) ResolveAccounts(FixedAsset a, FixedAssetCategory cat)
        => (
            a.AssetAccountId               ?? cat.AssetAccountId,
            a.AccumDepreciationAccountId   ?? cat.AccumDepreciationAccountId,
            a.DepreciationExpenseAccountId ?? cat.DepreciationExpenseAccountId
        );

    private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    // ─── GET /api/fixed-assets ────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search     = null,
        [FromQuery] int?    categoryId = null,
        [FromQuery] AssetStatus? status = null,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20)
    {
        var q = _db.FixedAssets
            .Include(a => a.Category)
            .AsQueryable();

        if (categoryId.HasValue) q = q.Where(a => a.CategoryId == categoryId.Value);
        if (status.HasValue)     q = q.Where(a => a.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(a => a.Name.Contains(search) || a.AssetNumber.Contains(search)
                           || (a.SerialNumber != null && a.SerialNumber.Contains(search)));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new FixedAssetDto(
                a.Id, a.AssetNumber, a.Name, a.Description,
                a.CategoryId, a.Category.Name,
                a.PurchaseDate, a.PurchaseCost, a.SalvageValue,
                a.DepreciationMethod, a.UsefulLifeYears, a.DepreciationStartDate,
                a.AccumulatedDepreciation, a.PurchaseCost - a.AccumulatedDepreciation,
                a.Status, a.Location, a.SerialNumber, a.Supplier,
                a.PurchaseInvoiceId, a.Notes, a.AttachmentUrl, a.AttachmentPublicId,
                a.AssetAccountId, a.AccumDepreciationAccountId, a.DepreciationExpenseAccountId,
                a.CreatedAt
            )).ToListAsync();

        return Ok(new PaginatedResult<FixedAssetDto>(items, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    // ─── GET /api/fixed-assets/{id} ───────────────────

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var a = await _db.FixedAssets
            .Include(x => x.Category)
            .Include(x => x.Depreciations.OrderBy(d => d.DepreciationDate))
            .Include(x => x.Disposals)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (a == null) return NotFound();

        var assetDto = ToDto(a);
        var depDtos  = a.Depreciations.Select(d => ToDepDto(d, a.Name));
        var disDtos  = a.Disposals.FirstOrDefault() is AssetDisposal dis ? ToDisDto(dis, a.Name) : null;

        return Ok(new FixedAssetDetailDto(assetDto, depDtos, disDtos));
    }

    // ─── POST /api/fixed-assets ───────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFixedAssetDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("اسم الأصل مطلوب.");
        if (!await _db.FixedAssetCategories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest("الفئة غير موجودة.");

        var assetNo = await _seq.NextAsync("FA", async (db, pattern) =>
        {
            var max = await db.FixedAssets
                .Where(a => EF.Functions.Like(a.AssetNumber, pattern))
                .Select(a => a.AssetNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

        var asset = new FixedAsset
        {
            AssetNumber                  = assetNo,
            Name                         = dto.Name.Trim(),
            Description                  = dto.Description?.Trim(),
            CategoryId                   = dto.CategoryId,
            PurchaseDate                 = dto.PurchaseDate,
            PurchaseCost                 = dto.PurchaseCost,
            SalvageValue                 = dto.SalvageValue,
            DepreciationMethod           = dto.DepreciationMethod,
            UsefulLifeYears              = dto.UsefulLifeYears,
            DepreciationStartDate        = dto.DepreciationStartDate,
            Location                     = dto.Location?.Trim(),
            SerialNumber                 = dto.SerialNumber?.Trim(),
            Supplier                     = dto.Supplier?.Trim(),
            PurchaseInvoiceId            = dto.PurchaseInvoiceId,
            Notes                        = dto.Notes?.Trim(),
            AttachmentUrl                = dto.AttachmentUrl,
            AttachmentPublicId           = dto.AttachmentPublicId,
            AssetAccountId               = dto.AssetAccountId,
            AccumDepreciationAccountId   = dto.AccumDepreciationAccountId,
            DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId,
            Status                       = AssetStatus.Active,
            CreatedAt                    = TimeHelper.GetEgyptTime(),
            CreatedByUserId              = UserId
        };

        _db.FixedAssets.Add(asset);
        await _db.SaveChangesAsync();
        return Ok(new { id = asset.Id, assetNumber = asset.AssetNumber });
    }

    // ─── PUT /api/fixed-assets/{id} ───────────────────

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFixedAssetDto dto)
    {
        var asset = await _db.FixedAssets.FindAsync(id);
        if (asset == null) return NotFound();
        if (asset.Status == AssetStatus.Disposed)
            return BadRequest("لا يمكن تعديل أصل مستبعد.");
        if (!await _db.FixedAssetCategories.AnyAsync(c => c.Id == dto.CategoryId))
            return BadRequest("الفئة غير موجودة.");

        asset.Name                         = dto.Name.Trim();
        asset.Description                  = dto.Description?.Trim();
        asset.CategoryId                   = dto.CategoryId;
        asset.PurchaseDate                 = dto.PurchaseDate;
        asset.PurchaseCost                 = dto.PurchaseCost;
        asset.SalvageValue                 = dto.SalvageValue;
        asset.DepreciationMethod           = dto.DepreciationMethod;
        asset.UsefulLifeYears              = dto.UsefulLifeYears;
        asset.DepreciationStartDate        = dto.DepreciationStartDate;
        asset.Location                     = dto.Location?.Trim();
        asset.SerialNumber                 = dto.SerialNumber?.Trim();
        asset.Supplier                     = dto.Supplier?.Trim();
        asset.PurchaseInvoiceId            = dto.PurchaseInvoiceId;
        asset.Notes                        = dto.Notes?.Trim();
        asset.AttachmentUrl                = dto.AttachmentUrl;
        asset.AttachmentPublicId           = dto.AttachmentPublicId;
        asset.AssetAccountId               = dto.AssetAccountId;
        asset.AccumDepreciationAccountId   = dto.AccumDepreciationAccountId;
        asset.DepreciationExpenseAccountId = dto.DepreciationExpenseAccountId;
        asset.UpdatedAt                    = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ─── DELETE /api/fixed-assets/{id} ────────────────

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var asset = await _db.FixedAssets
            .Include(a => a.Depreciations)
            .Include(a => a.Disposals)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        if (asset.Depreciations.Any() || asset.Disposals.Any())
            return BadRequest("لا يمكن حذف أصل له قيود إهلاك أو استبعاد — قم بالأرشفة بدلاً من الحذف.");

        _db.FixedAssets.Remove(asset);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ══════════════════════════════════════════════════
    // تبويبة الإهلاك
    // ══════════════════════════════════════════════════

    // GET /api/fixed-assets/{id}/depreciations
    [HttpGet("{id}/depreciations")]
    public async Task<IActionResult> GetDepreciations(int id)
    {
        var asset = await _db.FixedAssets.FindAsync(id);
        if (asset == null) return NotFound();

        var list = await _db.AssetDepreciations
            .Where(d => d.FixedAssetId == id)
            .OrderBy(d => d.DepreciationDate)
            .Select(d => new AssetDepreciationDto(
                d.Id, d.DepreciationNumber, d.FixedAssetId, asset.Name,
                d.DepreciationDate, d.PeriodYear, d.PeriodMonth,
                d.DepreciationAmount, d.AccumulatedBefore, d.AccumulatedAfter,
                d.BookValueAfter, d.Notes, d.JournalEntryId, d.CreatedAt
            )).ToListAsync();

        return Ok(list);
    }

    // POST /api/fixed-assets/{id}/depreciations  — ترحيل قسط إهلاك لأصل واحد
    [HttpPost("{id}/depreciations")]
    public async Task<IActionResult> PostDepreciation(int id, [FromBody] PostDepreciationDto dto)
    {
        if (dto.FixedAssetId != id)
            return BadRequest("معرّف الأصل غير متطابق.");

        var asset = await _db.FixedAssets
            .Include(a => a.Category)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        if (asset.Status != AssetStatus.Active)
            return BadRequest($"لا يمكن ترحيل إهلاك على أصل بحالة: {asset.Status}.");

        // تحقق من عدم تكرار نفس الشهر
        if (await _db.AssetDepreciations.AnyAsync(d =>
                d.FixedAssetId == id &&
                d.PeriodYear   == dto.PeriodYear &&
                d.PeriodMonth  == dto.PeriodMonth))
            return BadRequest($"الإهلاك لشهر {dto.PeriodMonth}/{dto.PeriodYear} مرحّل مسبقاً.");

        // حساب مبلغ الإهلاك
        var amount = dto.OverrideAmount ?? CalcStraightLineMonthly(asset);
        // لا يتجاوز القيمة الدفترية - القيمة التخريدية
        var remaining = asset.PurchaseCost - asset.AccumulatedDepreciation - asset.SalvageValue;
        if (remaining <= 0) return BadRequest("الأصل مستهلك بالكامل ولا يوجد مبلغ للإهلاك.");
        amount = Math.Min(amount, remaining);

        var accumBefore = asset.AccumulatedDepreciation;
        var accumAfter  = accumBefore + amount;

        // توليد رقم مستند
        var depNo = await _seq.NextAsync("DEP", async (db, pattern) =>
        {
            var max = await db.AssetDepreciations
                .Where(d => EF.Functions.Like(d.DepreciationNumber, pattern))
                .Select(d => d.DepreciationNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

        var dep = new AssetDepreciation
        {
            DepreciationNumber = depNo,
            FixedAssetId       = id,
            DepreciationDate   = dto.DepreciationDate,
            PeriodYear         = dto.PeriodYear,
            PeriodMonth        = dto.PeriodMonth,
            DepreciationAmount = amount,
            AccumulatedBefore  = accumBefore,
            AccumulatedAfter   = accumAfter,
            BookValueAfter     = asset.PurchaseCost - accumAfter,
            Notes              = dto.Notes,
            CreatedAt          = TimeHelper.GetEgyptTime(),
            CreatedByUserId    = UserId
        };

        // تحديث مجمع الإهلاك على الأصل
        asset.AccumulatedDepreciation = accumAfter;
        if (asset.PurchaseCost - accumAfter <= asset.SalvageValue)
            asset.Status = AssetStatus.FullyDepreciated;
        asset.UpdatedAt = TimeHelper.GetEgyptTime();

        _db.AssetDepreciations.Add(dep);

        // ── قيد محاسبي ───────────────────────────────────
        var (_, accumAccId, expenseAccId) = ResolveAccounts(asset, asset.Category);
        JournalEntry? je = null;

        if (accumAccId.HasValue && expenseAccId.HasValue)
        {
            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = dto.DepreciationDate,
                Type            = JournalEntryType.AssetDepreciation,
                Status          = JournalEntryStatus.Posted,
                Description     = $"إهلاك {asset.Name} — {dto.PeriodMonth}/{dto.PeriodYear}",
                Reference       = depNo,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines = new List<JournalLine>
                {
                    new() { AccountId = expenseAccId.Value, Debit  = amount, Credit = 0,      Description = $"مصروف إهلاك — {asset.Name}" },
                    new() { AccountId = accumAccId.Value,   Debit  = 0,      Credit = amount, Description = $"مجمع إهلاك — {asset.Name}" }
                }
            };
            _db.JournalEntries.Add(je);
        }

        await _db.SaveChangesAsync();

        if (je != null) dep.JournalEntryId = je.Id;
        await _db.SaveChangesAsync();

        return Ok(new { id = dep.Id, depreciationNumber = dep.DepreciationNumber, amount, journalEntryId = je?.Id });
    }

    // POST /api/fixed-assets/depreciations/run-batch  — ترحيل إهلاك شهري لجميع الأصول النشطة
    [HttpPost("depreciations/run-batch")]
    public async Task<IActionResult> RunBatchDepreciation([FromBody] RunBatchDepreciationDto dto)
    {
        var assets = await _db.FixedAssets
            .Include(a => a.Category)
            .Where(a => a.Status == AssetStatus.Active)
            .Where(a => a.DepreciationStartDate == null || a.DepreciationStartDate.Value <= dto.AsOfDate)
            .ToListAsync();

        int posted = 0, skipped = 0;
        var details = new List<object>();

        foreach (var asset in assets)
        {
            // تحقق من عدم تكرار
            if (await _db.AssetDepreciations.AnyAsync(d =>
                    d.FixedAssetId == asset.Id &&
                    d.PeriodYear   == dto.PeriodYear &&
                    d.PeriodMonth  == dto.PeriodMonth))
            {
                skipped++;
                continue;
            }

            var amount    = CalcStraightLineMonthly(asset);
            var remaining = asset.PurchaseCost - asset.AccumulatedDepreciation - asset.SalvageValue;
            if (remaining <= 0) { skipped++; continue; }
            amount = Math.Min(amount, remaining);

            var accumBefore = asset.AccumulatedDepreciation;
            var accumAfter  = accumBefore + amount;

            var depNo = await _seq.NextAsync("DEP", async (db, pattern) =>
            {
                var max = await db.AssetDepreciations
                    .Where(d => EF.Functions.Like(d.DepreciationNumber, pattern))
                    .Select(d => d.DepreciationNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            var dep = new AssetDepreciation
            {
                DepreciationNumber = depNo,
                FixedAssetId       = asset.Id,
                DepreciationDate   = dto.AsOfDate,
                PeriodYear         = dto.PeriodYear,
                PeriodMonth        = dto.PeriodMonth,
                DepreciationAmount = amount,
                AccumulatedBefore  = accumBefore,
                AccumulatedAfter   = accumAfter,
                BookValueAfter     = asset.PurchaseCost - accumAfter,
                Notes              = $"إهلاك دفعي — {dto.PeriodMonth}/{dto.PeriodYear}",
                CreatedAt          = TimeHelper.GetEgyptTime(),
                CreatedByUserId    = UserId
            };

            asset.AccumulatedDepreciation = accumAfter;
            if (asset.PurchaseCost - accumAfter <= asset.SalvageValue)
                asset.Status = AssetStatus.FullyDepreciated;
            asset.UpdatedAt = TimeHelper.GetEgyptTime();

            _db.AssetDepreciations.Add(dep);

            // قيد محاسبي
            var (_, accumAccId, expenseAccId) = ResolveAccounts(asset, asset.Category);
            if (accumAccId.HasValue && expenseAccId.HasValue)
            {
                var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
                {
                    var max = await db.JournalEntries
                        .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                        .Select(e => e.EntryNumber).ToListAsync();
                    return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                              .DefaultIfEmpty(0).Max();
                });

                var je = new JournalEntry
                {
                    EntryNumber     = jeNo,
                    EntryDate       = dto.AsOfDate,
                    Type            = JournalEntryType.AssetDepreciation,
                    Status          = JournalEntryStatus.Posted,
                    Description     = $"إهلاك {asset.Name} — {dto.PeriodMonth}/{dto.PeriodYear}",
                    Reference       = depNo,
                    CreatedByUserId = UserId,
                    CreatedAt       = TimeHelper.GetEgyptTime(),
                    Lines = new List<JournalLine>
                    {
                        new() { AccountId = expenseAccId.Value, Debit  = amount, Credit = 0,      Description = $"مصروف إهلاك — {asset.Name}" },
                        new() { AccountId = accumAccId.Value,   Debit  = 0,      Credit = amount, Description = $"مجمع إهلاك — {asset.Name}" }
                    }
                };
                _db.JournalEntries.Add(je);
                await _db.SaveChangesAsync();
                dep.JournalEntryId = je.Id;
            }

            await _db.SaveChangesAsync();
            posted++;
            details.Add(new { assetId = asset.Id, assetNumber = asset.AssetNumber, name = asset.Name, amount, depNo });
        }

        return Ok(new { posted, skipped, details });
    }

    // ══════════════════════════════════════════════════
    // تبويبة الاستبعادات
    // ══════════════════════════════════════════════════

    // GET /api/fixed-assets/{id}/disposal
    [HttpGet("{id}/disposal")]
    public async Task<IActionResult> GetDisposal(int id)
    {
        var dis = await _db.AssetDisposals
            .Include(d => d.FixedAsset)
            .FirstOrDefaultAsync(d => d.FixedAssetId == id);
        if (dis == null) return NotFound();
        return Ok(ToDisDto(dis, dis.FixedAsset.Name));
    }

    // GET /api/fixed-assets/disposals  — كل الاستبعادات
    [HttpGet("disposals")]
    public async Task<IActionResult> GetAllDisposals(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var total = await _db.AssetDisposals.CountAsync();
        var list  = await _db.AssetDisposals
            .Include(d => d.FixedAsset)
            .OrderByDescending(d => d.DisposalDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(d => ToDisDto(d, d.FixedAsset.Name))
            .ToListAsync();

        return Ok(new PaginatedResult<AssetDisposalDto>(list, total, page, pageSize,
            (int)Math.Ceiling((double)total / pageSize)));
    }

    // POST /api/fixed-assets/{id}/dispose
    [HttpPost("{id}/dispose")]
    public async Task<IActionResult> Dispose(int id, [FromBody] PostDisposalDto dto)
    {
        if (dto.FixedAssetId != id)
            return BadRequest("معرّف الأصل غير متطابق.");

        var asset = await _db.FixedAssets
            .Include(a => a.Category)
            .Include(a => a.Disposals)
            .FirstOrDefaultAsync(a => a.Id == id);
        if (asset == null) return NotFound();
        if (asset.Status == AssetStatus.Disposed)
            return BadRequest("الأصل مستبعد بالفعل.");
        if (asset.Disposals.Any())
            return BadRequest("يوجد مستند استبعاد مسبق لهذا الأصل.");

        // أرقام وقت الاستبعاد
        var bookValue   = asset.PurchaseCost - asset.AccumulatedDepreciation;
        var accumAtDis  = asset.AccumulatedDepreciation;

        var disNo = await _seq.NextAsync("DIS", async (db, pattern) =>
        {
            var max = await db.AssetDisposals
                .Where(d => EF.Functions.Like(d.DisposalNumber, pattern))
                .Select(d => d.DisposalNumber).ToListAsync();
            return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                      .DefaultIfEmpty(0).Max();
        });

        var disposal = new AssetDisposal
        {
            DisposalNumber         = disNo,
            FixedAssetId           = id,
            DisposalType           = dto.DisposalType,
            DisposalDate           = dto.DisposalDate,
            BookValueAtDisposal    = bookValue,
            AccumulatedAtDisposal  = accumAtDis,
            SaleProceeds           = dto.SaleProceeds,
            ProceedsAccountId      = dto.ProceedsAccountId,
            GainAccountId          = dto.GainAccountId,
            LossAccountId          = dto.LossAccountId,
            Buyer                  = dto.Buyer?.Trim(),
            Notes                  = dto.Notes?.Trim(),
            AttachmentUrl          = dto.AttachmentUrl,
            AttachmentPublicId     = dto.AttachmentPublicId,
            CreatedAt              = TimeHelper.GetEgyptTime(),
            CreatedByUserId        = UserId
        };

        asset.Status    = AssetStatus.Disposed;
        asset.UpdatedAt = TimeHelper.GetEgyptTime();

        _db.AssetDisposals.Add(disposal);

        // ── قيد محاسبي ────────────────────────────────────
        var (assetAccId, accumAccId, _) = ResolveAccounts(asset, asset.Category);
        JournalEntry? je = null;

        if (assetAccId.HasValue && accumAccId.HasValue)
        {
            var gainLoss   = dto.SaleProceeds - bookValue;
            var gainAccId  = dto.GainAccountId;
            var lossAccId  = dto.LossAccountId;

            var jeNo = await _seq.NextAsync("JE", async (db, pattern) =>
            {
                var max = await db.JournalEntries
                    .Where(e => EF.Functions.Like(e.EntryNumber, pattern))
                    .Select(e => e.EntryNumber).ToListAsync();
                return max.Select(n => int.TryParse(n.Split('-').LastOrDefault(), out var v) ? v : 0)
                          .DefaultIfEmpty(0).Max();
            });

            je = new JournalEntry
            {
                EntryNumber     = jeNo,
                EntryDate       = dto.DisposalDate,
                Type            = JournalEntryType.AssetDisposal,
                Status          = JournalEntryStatus.Posted,
                Description     = $"استبعاد {asset.Name} ({dto.DisposalType})",
                Reference       = disNo,
                CreatedByUserId = UserId,
                CreatedAt       = TimeHelper.GetEgyptTime(),
                Lines           = new List<JournalLine>()
            };

            // مدين: مجمع الإهلاك
            if (accumAtDis > 0)
                je.Lines.Add(new() { AccountId = accumAccId.Value, Debit = accumAtDis, Credit = 0, Description = "مجمع إهلاك — استبعاد" });

            // مدين: المتحصلات (إن كانت > 0)
            if (dto.SaleProceeds > 0 && dto.ProceedsAccountId.HasValue)
                je.Lines.Add(new() { AccountId = dto.ProceedsAccountId.Value, Debit = dto.SaleProceeds, Credit = 0, Description = "متحصلات الاستبعاد" });

            // مدين: خسارة الاستبعاد (إن وجدت)
            if (gainLoss < 0 && lossAccId.HasValue)
                je.Lines.Add(new() { AccountId = lossAccId.Value, Debit = Math.Abs(gainLoss), Credit = 0, Description = "خسارة استبعاد أصل" });

            // دائن: الأصل بتكلفته الأصلية
            je.Lines.Add(new() { AccountId = assetAccId.Value, Debit = 0, Credit = asset.PurchaseCost, Description = $"استبعاد {asset.Name}" });

            // دائن: ربح الاستبعاد (إن وجد)
            if (gainLoss > 0 && gainAccId.HasValue)
                je.Lines.Add(new() { AccountId = gainAccId.Value, Debit = 0, Credit = gainLoss, Description = "ربح استبعاد أصل" });

            _db.JournalEntries.Add(je);
        }

        await _db.SaveChangesAsync();

        if (je != null) disposal.JournalEntryId = je.Id;
        await _db.SaveChangesAsync();

        return Ok(new { id = disposal.Id, disposalNumber = disposal.DisposalNumber, journalEntryId = je?.Id });
    }

    // ─── mapping helpers ──────────────────────────────

    private static FixedAssetDto ToDto(FixedAsset a) => new(
        a.Id, a.AssetNumber, a.Name, a.Description,
        a.CategoryId, a.Category?.Name ?? "",
        a.PurchaseDate, a.PurchaseCost, a.SalvageValue,
        a.DepreciationMethod, a.UsefulLifeYears, a.DepreciationStartDate,
        a.AccumulatedDepreciation, a.PurchaseCost - a.AccumulatedDepreciation,
        a.Status, a.Location, a.SerialNumber, a.Supplier,
        a.PurchaseInvoiceId, a.Notes, a.AttachmentUrl, a.AttachmentPublicId,
        a.AssetAccountId, a.AccumDepreciationAccountId, a.DepreciationExpenseAccountId,
        a.CreatedAt
    );

    private static AssetDepreciationDto ToDepDto(AssetDepreciation d, string assetName) => new(
        d.Id, d.DepreciationNumber, d.FixedAssetId, assetName,
        d.DepreciationDate, d.PeriodYear, d.PeriodMonth,
        d.DepreciationAmount, d.AccumulatedBefore, d.AccumulatedAfter,
        d.BookValueAfter, d.Notes, d.JournalEntryId, d.CreatedAt
    );

    private static AssetDisposalDto ToDisDto(AssetDisposal d, string assetName) => new(
        d.Id, d.DisposalNumber, d.FixedAssetId, assetName,
        d.DisposalType, d.DisposalDate,
        d.BookValueAtDisposal, d.AccumulatedAtDisposal,
        d.SaleProceeds, d.SaleProceeds - d.BookValueAtDisposal,
        d.Buyer, d.Notes, d.AttachmentUrl,
        d.JournalEntryId, d.CreatedAt
    );
}

// DTO خاص بالدفعة الشهرية
public record RunBatchDepreciationDto(
    int      PeriodYear,
    int      PeriodMonth,
    DateTime AsOfDate
);
