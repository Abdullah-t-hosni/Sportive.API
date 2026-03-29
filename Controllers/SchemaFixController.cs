using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SchemaFixController : ControllerBase
{
    private readonly AppDbContext _db;
    public SchemaFixController(AppDbContext db) => _db = db;

    [HttpGet("run-v3")]
    public async Task<IActionResult> Run()
    {
        try 
        {
            // MySQL / MariaDB syntax
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS VendorAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS InventoryAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS ExpenseAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS VatAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE PurchaseInvoices ADD COLUMN IF NOT EXISTS CashAccountId INT NULL;");
            await _db.Database.ExecuteSqlRawAsync("ALTER TABLE Suppliers ADD COLUMN IF NOT EXISTS MainAccountId INT NULL;");
            
            // Fix Chart of Accounts - Allow all accounts to be postable
            await _db.Database.ExecuteSqlRawAsync("UPDATE Accounts SET AllowPosting = 1 WHERE IsDeleted = 0;");
            
            return Ok(new { message = "Schema updated successfully (V3.1 - Account Posting Enabled)" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                error = ex.Message, 
                stack = ex.StackTrace,
                inner = ex.InnerException?.Message 
            });
        }
    }
}
