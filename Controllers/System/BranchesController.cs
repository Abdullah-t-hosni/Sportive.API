using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Utils;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly AppDbContext _db;

    public BranchesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var branches = await _db.Branches.OrderBy(b => b.Name).ToListAsync();
        return Ok(branches);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null) return NotFound();
        return Ok(branch);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BranchDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Branch name is required." });

        var branch = new Branch
        {
            Name = dto.Name.Trim(),
            Address = dto.Address?.Trim(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            IsActive = dto.IsActive,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync();

        // ── Auto-generate Financial Accounts for the new branch ──
        var currentAssetsAcc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "11");
        if (currentAssetsAcc != null)
        {
            var cashParent = await _db.Accounts.FirstOrDefaultAsync(a => a.NameAr.Contains("النقدية والصناديق"))
                ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1101")
                ?? currentAssetsAcc;

            var bankParent = await _db.Accounts.FirstOrDefaultAsync(a => a.NameAr.Contains("النقدية في البنك") || a.NameAr.Contains("البنك"))
                ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1102")
                ?? currentAssetsAcc;

            var walletParent = await _db.Accounts.FirstOrDefaultAsync(a => a.NameAr.Contains("النقدية في المحافظ") || a.NameAr.Contains("المحافظ"))
                ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1105")
                ?? currentAssetsAcc;

            async Task<string> GenerateNextChildCodeAsync(Account parent)
            {
                var prefix = parent.Code;
                var childCodes = await _db.Accounts
                    .Where(a => a.ParentId == parent.Id && a.Code.StartsWith(prefix))
                    .Select(a => a.Code)
                    .ToListAsync();

                int maxSuffix = 0;
                foreach (var code in childCodes)
                {
                    if (code.Length > prefix.Length)
                    {
                        var suffixStr = code.Substring(prefix.Length);
                        if (int.TryParse(suffixStr, out int parsed) && parsed > maxSuffix)
                        {
                            maxSuffix = parsed;
                        }
                    }
                }

                return $"{prefix}{(maxSuffix + 1):D2}";
            }

            var cashCode = await GenerateNextChildCodeAsync(cashParent);
            var bankCode = await GenerateNextChildCodeAsync(bankParent);
            
            var vfWalletCode = await GenerateNextChildCodeAsync(walletParent);
            var instaWalletCode = vfWalletCode;
            var prefix = walletParent.Code;
            if (instaWalletCode.Length > prefix.Length)
            {
                var suffixStr = instaWalletCode.Substring(prefix.Length);
                if (int.TryParse(suffixStr, out int parsed))
                {
                    instaWalletCode = $"{prefix}{(parsed + 1):D2}";
                }
            }

            if (cashParent.Id != currentAssetsAcc.Id)
            {
                cashParent.IsLeaf = false;
                cashParent.AllowPosting = false;
            }
            if (bankParent.Id != currentAssetsAcc.Id)
            {
                bankParent.IsLeaf = false;
                bankParent.AllowPosting = false;
            }
            if (walletParent.Id != currentAssetsAcc.Id)
            {
                walletParent.IsLeaf = false;
                walletParent.AllowPosting = false;
            }

            var accountsToAdd = new List<Account>
            {
                new Account { Code = cashCode, NameAr = $"نقدية كاشير - {branch.Name}", NameEn = $"Cashier - {branch.Name}", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = cashParent.Level + 1, ParentId = cashParent.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true, BranchId = branch.Id },
                new Account { Code = vfWalletCode, NameAr = $"فودافون كاش - {branch.Name}", NameEn = $"Vodafone Cash - {branch.Name}", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = walletParent.Level + 1, ParentId = walletParent.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true, BranchId = branch.Id },
                new Account { Code = instaWalletCode, NameAr = $"إنستاباي - {branch.Name}", NameEn = $"InstaPay - {branch.Name}", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = walletParent.Level + 1, ParentId = walletParent.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true, BranchId = branch.Id },
                new Account { Code = bankCode, NameAr = $"شبكات تحت التحصيل - {branch.Name}", NameEn = $"Networks Under Collection - {branch.Name}", Type = AccountType.Asset, Nature = AccountNature.Debit, Level = bankParent.Level + 1, ParentId = bankParent.Id, IsLeaf = true, AllowPosting = true, IsSystem = true, CreatedAt = TimeHelper.GetEgyptTime(), CanReceivePayment = true, BranchId = branch.Id }
            };

            _db.Accounts.AddRange(accountsToAdd);
            await _db.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetById), new { id = branch.Id }, branch);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] BranchDto dto)
    {
        var branch = await _db.Branches.FindAsync(id);
        if (branch == null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Branch name is required." });

        branch.Name = dto.Name.Trim();
        branch.Address = dto.Address?.Trim();
        branch.PhoneNumber = dto.PhoneNumber?.Trim();
        branch.IsActive = dto.IsActive;
        branch.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return Ok(branch);
    }

    [RequirePermission(ModuleKeys.Settings, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var branch = await _db.Branches.Include(b => b.Warehouses).FirstOrDefaultAsync(b => b.Id == id);
        if (branch == null) return NotFound();

        if (branch.Warehouses.Any())
            return BadRequest(new { message = "Cannot delete branch with warehouses. Delete the warehouses first." });

        var hasEmployees = await _db.Employees.AnyAsync(e => e.BranchId == id);
        if (hasEmployees)
            return BadRequest(new { message = "Cannot delete branch linked to employees." });

        _db.Branches.Remove(branch);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}

public class BranchDto
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
}
