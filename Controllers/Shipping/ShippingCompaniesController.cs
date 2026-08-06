using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Sportive.API.Interfaces;

namespace Sportive.API.Controllers.Shipping
{
    [ApiController]
    [Route("api/shipping-companies")]
    [Authorize] // Adjust authorization as per project rules
    public class ShippingCompaniesController : ControllerBase
    {
        private readonly AppDbContext _db;

        public ShippingCompaniesController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var companies = await _db.ShippingCompanies
                .Include(c => c.Account)
                .OrderBy(c => c.NameAr)
                .ToListAsync();

            return Ok(companies);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var company = await _db.ShippingCompanies
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (company == null) return NotFound();
            return Ok(company);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ShippingCompanyDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NameAr))
                return BadRequest(new { message = "الاسم باللغة العربية مطلوب" });

            var company = new ShippingCompany
            {
                NameAr = dto.NameAr,
                NameEn = dto.NameEn,
                ContactInfo = dto.ContactInfo,
                IntegrationType = dto.IntegrationType,
                ApiKey = dto.ApiKey,
                UseSandbox = dto.UseSandbox,
                IsActive = dto.IsActive
            };

            // إنشاء حساب محاسبي تلقائياً تحت حساب 1108
            var parentAccount = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1108");
            if (parentAccount != null)
            {
                var newAccount = new Account
                {
                    NameAr = dto.NameAr,
                    NameEn = dto.NameEn,
                    ParentId = parentAccount.Id,
                    Type = AccountType.Asset,
                    Nature = AccountNature.Debit,
                    CanReceivePayment = true,
                    Level = parentAccount.Level + 1,
                    IsActive = true
                };

                // Gap-filling code generation starting from 01 onwards
                int nextSeq = 1;
                string candidateCode = $"{parentAccount.Code}{nextSeq:D2}";
                while (await _db.Accounts.AnyAsync(a => a.Code == candidateCode))
                {
                    nextSeq++;
                    candidateCode = $"{parentAccount.Code}{nextSeq:D2}";
                }

                newAccount.Code = candidateCode;
                _db.Accounts.Add(newAccount);
                company.Account = newAccount;
            }

            _db.ShippingCompanies.Add(company);
            await _db.SaveChangesAsync();

            return Ok(company);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] ShippingCompanyDto dto)
        {
            var company = await _db.ShippingCompanies
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (company == null) return NotFound();

            company.NameAr = dto.NameAr;
            company.NameEn = dto.NameEn;
            company.ContactInfo = dto.ContactInfo;
            company.IntegrationType = dto.IntegrationType;
            company.ApiKey = dto.ApiKey;
            company.UseSandbox = dto.UseSandbox;
            company.IsActive = dto.IsActive;

            if (company.Account != null)
            {
                company.Account.NameAr = dto.NameAr;
                company.Account.NameEn = dto.NameEn;
            }

            await _db.SaveChangesAsync();
            return Ok(company);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var company = await _db.ShippingCompanies
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (company == null) return NotFound();

            // Cannot delete if they have order history
            var hasOrders = await _db.Orders.AnyAsync(o => o.ShippingCompanyId == id);
            if (hasOrders)
            {
                return BadRequest(new { message = "لا يمكن حذف الشركة لأنها مرتبطة بطلبات. يمكنك إيقاف تفعيلها بدلاً من ذلك." });
            }

            if (company.Account != null)
            {
                // check if account has transactions
                var hasTransactions = await _db.JournalLines.AnyAsync(l => l.AccountId == company.Account.Id);
                if (hasTransactions)
                {
                     return BadRequest(new { message = "لا يمكن حذف الشركة لأن الحساب المحاسبي المرتبط بها يحتوي على حركات مالية." });
                }
                _db.Accounts.Remove(company.Account);
            }

            _db.ShippingCompanies.Remove(company);
            await _db.SaveChangesAsync();

            return Ok();
        }
    }

    public class ShippingCompanyDto
    {
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string? ContactInfo { get; set; }
        public ShippingIntegrationType IntegrationType { get; set; }
        public string? ApiKey { get; set; }
        public bool UseSandbox { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
