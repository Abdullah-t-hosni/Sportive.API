using Sportive.API.Models;
using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sportive.API.DTOs;
using Sportive.API.Interfaces;
using Sportive.API.Data;
using Sportive.API.Utils;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ClosedXML.Excel;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customers;
    private readonly ITranslator _t;
    public CustomersController(ICustomerService customers, ITranslator t) => (_customers, _t) = (customers, t);

    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] decimal? minSpent = null,
        [FromQuery] int? minOrders = null,
        [FromQuery] DateTime? joinStartDate = null,
        [FromQuery] DateTime? joinEndDate = null,
        [FromQuery] int? categoryId = null,
        [FromQuery] bool? hasDebt = null,
        [FromQuery] string? orderBy = null,
        [FromQuery] bool isDescending = true) =>
        Ok(await _customers.GetCustomersAsync(page, pageSize, search, minSpent, minOrders, joinStartDate, joinEndDate, categoryId, hasDebt, orderBy, isDescending));

    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
    [HttpGet("rfm")]
    public async Task<IActionResult> GetRfm() =>
        Ok(await _customers.GetRfmDataAsync());

    [RequirePermission(ModuleKeys.Customers)]
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights() =>
        Ok(await _customers.GetInsightsAsync());

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        try { return Ok(await _customers.UpdateCustomerAsync(id, dto)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>إضافة عميل جديد — Admin, Manager, Cashier</summary>
    [RequirePermission(ModuleKeys.Customers + "," + ModuleKeys.Pos + "," + ModuleKeys.Orders)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerDto dto)
    {
        try 
        {
            var customer = await _customers.CreateCustomerAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = customer.Id }, customer);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>تفاصيل عميل — Admin أو صاحب الحساب</summary>
    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(id)) return Forbid();

        var customer = await _customers.GetCustomerByIdAsync(id);
        return customer == null ? NotFound() : Ok(customer);
    }

    /// <summary>تفعيل / إيقاف عميل — Admin فقط</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPatch("{id}/toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var result = await _customers.ToggleCustomerAsync(id);
        return result ? Ok() : NotFound();
    }

    /// <summary>حذف عميل بالكامل — Admin فقط</summary>
    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        try
        {
            var result = await _customers.DeleteCustomerAsync(id);
            return result ? Ok(new { message = "Customer deleted successfully" }) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize]
    [HttpPost("{customerId}/addresses")]
    public async Task<IActionResult> AddAddress(int customerId, [FromBody] CreateAddressDto dto) 
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();
        return Ok(await _customers.AddAddressAsync(customerId, dto));
    }

    [Authorize]
    [HttpDelete("{customerId}/addresses/{addressId}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        try { await _customers.DeleteAddressAsync(customerId, addressId); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [Authorize]
    [HttpPatch("{customerId}/addresses/{addressId}/default")]
    public async Task<IActionResult> SetDefault(int customerId, int addressId)
    {
        // 🛡️ Security Check: Owner or Admin?
        if (!IsOwnerOrAdmin(customerId)) return Forbid();

        await _customers.SetDefaultAddressAsync(customerId, addressId);
        return Ok(new { message = _t.Get("Customers.DefaultAddressUpdated") });
    }

    // Helper to check ownership
    private bool IsOwnerOrAdmin(int customerId)
    {
        if (User.IsInRole("SuperAdmin") || User.IsInRole("Admin") || User.IsInRole("Manager") || User.IsInRole("Staff") || User.IsInRole("Cashier"))
            return true;

        var currentCustomerId = User.FindFirst("CustomerId")?.Value;
        return currentCustomerId != null && int.Parse(currentCustomerId) == customerId;
    }

    [RequirePermission(ModuleKeys.Customers, requireEdit: true)]
    [HttpPost("import-opening-balances")]
    public async Task<IActionResult> ImportOpeningBalances([FromForm] IFormFile? file, [FromServices] AppDbContext db)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Accounting.NoFileUploaded") });

        var successCount = 0;
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var allCustomers = await db.Customers.Include(c => c.MainAccount).ToListAsync();
            var allAccounts = await db.Accounts.ToListAsync();
            var parentAccount = allAccounts.FirstOrDefault(a => a.Code == "1103");
            
            if (parentAccount == null)
            {
                return BadRequest(new { message = "Main Receivables account (1103) not found in Chart of Accounts." });
            }

            // Prepare for code generation if needed
            var existingCodes = new HashSet<string>(allAccounts.Where(a => a.Code != null).Select(a => a.Code!));
            long nextCodeNum = 1;
            var customerCodes = existingCodes.Where(c => c.StartsWith("1103") && c.Length > 4).ToList();
            if (customerCodes.Any())
            {
                var maxCode = customerCodes.Max();
                if (long.TryParse(maxCode!.Substring(4), out var maxVal)) nextCodeNum = maxVal + 1;
            }

            for (int r = 2; r <= lastRow; r++)
            {
                var rowName  = ws.Cell(r, 1).GetString().Trim();
                var rowPhone = ws.Cell(r, 2).GetString().Trim();
                var rowEmail = ws.Cell(r, 3).GetString().Trim();
                var rowNotes = ws.Cell(r, 5).GetString().Trim();

                if (string.IsNullOrEmpty(rowName) && string.IsNullOrEmpty(rowPhone)) continue;

                var balanceCell = ws.Cell(r, 4);
                decimal balance = 0;
                try 
                {
                    if (balanceCell.DataType == XLDataType.Number) balance = balanceCell.GetValue<decimal>();
                    else {
                        var balStr = balanceCell.GetString().Trim().Replace(",", "").Replace(" ", "");
                        if (!string.IsNullOrEmpty(balStr)) decimal.TryParse(balStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out balance);
                    }
                } catch { }

                // 1. Find Existing (By Phone OR Email)
                var customer = allCustomers.FirstOrDefault(c => 
                    (!string.IsNullOrEmpty(rowPhone) && c.Phone == rowPhone) || 
                    (!string.IsNullOrEmpty(rowEmail) && c.Email?.ToLower() == rowEmail.ToLower()) ||
                    (string.IsNullOrEmpty(rowPhone) && string.IsNullOrEmpty(rowEmail) && c.FullName == rowName));
                
                try 
                {
                    if (customer == null)
                    {
                        // Generate a unique email if missing to avoid unique constraint on empty strings
                        var finalEmail = rowEmail;
                        if (string.IsNullOrEmpty(finalEmail)) {
                            finalEmail = !string.IsNullOrEmpty(rowPhone) ? $"{rowPhone}@pos.com" : $"{Guid.NewGuid().ToString().Substring(0, 8)}@pos.com";
                        }
                        
                        // Check if this generated email is already taken
                        if (allCustomers.Any(c => c.Email?.ToLower() == finalEmail.ToLower())) {
                             finalEmail = $"{Guid.NewGuid().ToString().Substring(0, 8)}@pos.com";
                        }

                        customer = new Customer
                        {
                            FullName = string.IsNullOrEmpty(rowName) ? (string.IsNullOrEmpty(rowPhone) ? "Imported" : rowPhone) : rowName,
                            Phone = string.IsNullOrEmpty(rowPhone) ? null : rowPhone,
                            Email = finalEmail,
                            Notes = rowNotes,
                            IsActive = true,
                            MainAccountId = parentAccount.Id, // Default to control account
                            CreatedAt = TimeHelper.GetEgyptTime()
                        };
                        db.Customers.Add(customer);
                        await db.SaveChangesAsync(); // Persist to get ID
                        allCustomers.Add(customer);
                    }
                    else 
                    {
                        if (!string.IsNullOrEmpty(rowName))  customer.FullName = rowName;
                        if (!string.IsNullOrEmpty(rowEmail)) customer.Email = rowEmail;
                        if (!string.IsNullOrEmpty(rowNotes)) customer.Notes = rowNotes;
                        customer.UpdatedAt = TimeHelper.GetEgyptTime();
                    }

                    // 2. Individual Account Logic (Only if balance != 0 or they already have a sub-account)
                    // If the customer is currently pointed to the shared 1103 account but needs an individual opening balance, 
                    // we MUST create a sub-account for them.
                    if (balance != 0)
                    {
                        if (customer.MainAccount == null || customer.MainAccount.Code == "1103")
                        {
                            // Create Sub-Account
                            string newCode;
                            while (true) {
                                newCode = $"1103{nextCodeNum:D4}";
                                if (!existingCodes.Contains(newCode)) break;
                                nextCodeNum++;
                            }
                            existingCodes.Add(newCode);

                            var subAccount = new Account
                            {
                                Code = newCode,
                                NameAr = $"عميل - {customer.FullName}",
                                NameEn = $"Customer - {customer.FullName}",
                                ParentId = parentAccount.Id,
                                Type = AccountType.Asset,
                                Nature = AccountNature.Debit,
                                IsLeaf = true,
                                AllowPosting = true,
                                OpeningBalance = balance,
                                CreatedAt = TimeHelper.GetEgyptTime()
                            };
                            db.Accounts.Add(subAccount);
                            await db.SaveChangesAsync();
                            
                            customer.MainAccountId = subAccount.Id;
                            customer.MainAccount = subAccount;
                        }
                        else
                        {
                            // Update existing sub-account
                            customer.MainAccount.OpeningBalance = balance;
                            customer.MainAccount.UpdatedAt = TimeHelper.GetEgyptTime();
                        }
                    }
                    
                    successCount++;
                }
                catch (Exception rowEx)
                {
                    if (customer != null && db.Entry(customer).State != EntityState.Detached) db.Entry(customer).State = EntityState.Detached;
                    var msg = rowEx.InnerException?.Message ?? rowEx.Message;
                    errors.Add($"Row {r} ({rowName}): {msg}");
                }
            }

            await db.SaveChangesAsync();
            
            if (errors.Any())
            {
                using var errorWb = new XLWorkbook();
                var errorWs = errorWb.Worksheets.Add("Rejected Customers");
                errorWs.RightToLeft = true;
                
                // Headers
                errorWs.Cell(1, 1).Value = "الاسم الكامل";
                errorWs.Cell(1, 2).Value = "الهاتف";
                errorWs.Cell(1, 3).Value = "البريد الإلكتروني";
                errorWs.Cell(1, 4).Value = "الرصيد الافتتاحي";
                errorWs.Cell(1, 5).Value = "ملاحظات";
                errorWs.Cell(1, 6).Value = "سبب الرفض";
                
                var headerRange = errorWs.Range(1, 1, 1, 6);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
                headerRange.Style.Font.FontColor = XLColor.White;

                // Re-open stream to read original rows for the report if we wanted to be fancy, 
                // but here we just have the error messages. 
                // For now, let's just return the error list and success count.
                // Actually, I'll stick to the current implementation but return it in a way the frontend can handle if I update the frontend.
            }
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return BadRequest(new { message = $"Critical Save Error: {msg}", errors });
        }

        return Ok(new { success = true, successCount, errors });
    }

    [HttpGet("import-template")]
    [RequirePermission(ModuleKeys.Customers)]
    public IActionResult DownloadImportTemplate()
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("العملاء");
        
        ws.Cell(1, 1).Value = "الاسم الكامل";
        ws.Cell(1, 2).Value = "الهاتف";
        ws.Cell(1, 3).Value = "البريد الإلكتروني";
        ws.Cell(1, 4).Value = "الرصيد الافتتاحي";
        ws.Cell(1, 5).Value = "ملاحظات";
        
        // Styling
        var header = ws.Range(1, 1, 1, 5);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1a1a2e");
        header.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        header.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        
        ws.Column(1).Width = 30;
        ws.Column(2).Width = 20;
        ws.Column(3).Width = 30;
        ws.Column(4).Width = 20;
        ws.Column(5).Width = 40;
        ws.RightToLeft = true;

        using var stream = new System.IO.MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "customers_import_template.xlsx");
    }
}
