using Sportive.API.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Interfaces;
using Sportive.API.Services;
using Sportive.API.DTOs;
using Sportive.API.Utils;
using ClosedXML.Excel;

namespace Sportive.API.Controllers;

[ApiController, Route("api/[controller]")]
[RequirePermission(ModuleKeys.AccountingMain + "," + ModuleKeys.Pos + "," + ModuleKeys.PurchasesMain + "," + ModuleKeys.Settings + "," + ModuleKeys.HrAdvances + "," + ModuleKeys.HrPayroll)]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAccountingService _accounting;
    private readonly AccountingCoreService _core;
    private readonly ITranslator _t;
    public AccountsController(AppDbContext db, IAccountingService accounting, AccountingCoreService core, ITranslator t) {
        _db = db;
        _accounting = accounting;
        _core = core;
        _t = t;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool onlyActive   = false,
        [FromQuery] bool? isLeaf      = null,
        [FromQuery] bool? allowPosting = null)
    {
        var q = _db.Accounts.AsQueryable();
        if (onlyActive) q = q.Where(a => a.IsActive);
        if (isLeaf.HasValue) q = q.Where(a => a.IsLeaf == isLeaf.Value);
        if (allowPosting.HasValue) q = q.Where(a => a.AllowPosting == allowPosting.Value);
        if (Request.Query.TryGetValue("canReceivePayment", out var cv) && bool.TryParse(cv, out var canVal))
        {
            q = q.Where(a => a.CanReceivePayment == canVal);
        }

        var accounts = await q.OrderBy(a => a.Code).ToListAsync();
        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id);
        if (account == null) return NotFound();
        return Ok(account);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountDto dto)
    {
        if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code))
            return BadRequest(_t.Get("Accounting.AccountCodeExists"));

        if (dto.OpeningBalance != 0 && !dto.IsLeaf)
            return BadRequest(_t.Get("Accounting.NoParentOpeningBalance"));

        var account = new Account
        {
            Code           = dto.Code ?? string.Empty,
            NameAr         = dto.NameAr,
            NameEn         = dto.NameEn,
            Type           = dto.Type,
            Nature         = dto.Nature,
            ParentId       = dto.ParentId,
            OpeningBalance = dto.OpeningBalance,
            IsActive       = true,
            Level          = dto.Level,
            IsLeaf         = dto.IsLeaf,
            AllowPosting   = dto.AllowPosting,
            CanReceivePayment = dto.CanReceivePayment
        };

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAccountDto dto)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        if (dto.OpeningBalance != 0 && !account.IsLeaf)
            return BadRequest(_t.Get("Accounting.NoParentOpeningBalance"));

        if (!string.IsNullOrEmpty(dto.Code) && account.Code != dto.Code)
        {
            if (await _db.Accounts.AnyAsync(a => a.Code == dto.Code && a.Id != id))
                return BadRequest(_t.Get("Accounting.AccountCodeExists"));
            account.Code = dto.Code;
        }

        if (dto.ParentId != account.ParentId)
        {
            account.ParentId = dto.ParentId;
            if (dto.ParentId.HasValue)
            {
                var parent = await _db.Accounts.FindAsync(dto.ParentId.Value);
                if (parent != null)
                {
                    account.Level = parent.Level + 1;
                }
            }
            else
            {
                account.Level = 1;
            }
        }

        if (dto.Type.HasValue)
        {
            account.Type = dto.Type.Value;
        }

        if (dto.Nature.HasValue)
        {
            account.Nature = dto.Nature.Value;
        }

        account.NameAr            = dto.NameAr;
        account.NameEn            = dto.NameEn;
        account.IsActive          = dto.IsActive;
        account.AllowPosting      = dto.AllowPosting;
        account.CanReceivePayment = dto.CanReceivePayment;
        account.OpeningBalance    = dto.OpeningBalance;

        await _db.SaveChangesAsync();
        return Ok(account);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account == null) return NotFound();

        if (await _db.JournalLines.AnyAsync(l => l.AccountId == id))
            return BadRequest(_t.Get("Accounting.CannotDeleteWithTransactions"));

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("template-opening-balances")]
    public async Task<IActionResult> GetOpeningBalancesTemplate()
    {
        var accounts = await _db.Accounts.Where(a => a.AllowPosting && a.IsActive).OrderBy(a => a.Code).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(_t.Get("Accounting.OpeningBalancesSheet"));
        ws.RightToLeft = true;

        var headers = new[] { _t.Get("Accounting.AccountCodeHeader"), _t.Get("Accounting.AccountNameHeader"), _t.Get("Accounting.OpeningBalanceHeader"), _t.Get("Accounting.NatureHeader"), _t.Get("Accounting.TypeHeader") };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a1a2e");
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var a in accounts)
        {
            ws.Cell(row, 1).Value = a.Code;
            ws.Cell(row, 2).Value = a.NameAr;
            ws.Cell(row, 3).Value = a.OpeningBalance;
            ws.Cell(row, 4).Value = a.Nature == AccountNature.Debit ? _t.Get("Accounting.Debit") : _t.Get("Accounting.Credit");
            ws.Cell(row, 5).Value = a.Type.ToString();
            
            // Format existing data to look like reference
            ws.Row(row).Style.Font.FontColor = XLColor.Gray;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 15;
        ws.Column(2).Width = 35;
        ws.Column(3).Width = 20;

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "OpeningBalances_Template.xlsx");
    }

    [HttpPost("import-opening-balances")]
    public async Task<IActionResult> ImportOpeningBalances(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { message = _t.Get("Accounting.NoFileUploaded") });

        var successCount = 0;
        var errors = new List<string>();

        try
        {
            using var stream = file.OpenReadStream();
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.FirstOrDefault() ?? throw new Exception(_t.Get("Accounting.EmptyFile"));
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var allAccounts = await _db.Accounts.ToListAsync();
            
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var firstRow = ws.Row(1);
            string Normalize(string s) => new string(s.Where(c => char.IsLetterOrDigit(c)).ToArray()).ToLower();

            var lastColUsed = ws.LastColumnUsed();
            var lastCol = lastColUsed?.ColumnNumber() ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var hRaw = firstRow.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(hRaw)) continue;
                headers[Normalize(hRaw)] = c;
            }

            int GetCol(params string[] aliases) {
                foreach (var a in aliases) {
                    if (headers.TryGetValue(Normalize(a), out var idx)) return idx;
                }
                return -1;
            }

            int colCode = GetCol(_t.Get("Accounting.AccountCodeHeader").Replace("*","").Trim(), "Code", "Account Code");
            int colBal  = GetCol(_t.Get("Accounting.OpeningBalanceHeader").Replace("*","").Trim(), "Balance", "Opening Balance");

            if (colCode == -1 || colBal == -1)
                return BadRequest(new { message = _t.Get("Accounting.MissingColumns") });

            for (int r = 2; r <= lastRow; r++)
            {
                var code = ws.Cell(r, colCode).GetString().Trim();
                if (string.IsNullOrEmpty(code)) continue;

                var balStr = ws.Cell(r, colBal).GetString().Trim();
                if (!decimal.TryParse(balStr, out var balance))
                {
                    errors.Add(_t.Get("Accounting.InvalidBalanceAtRow", r, code));
                    continue;
                }

                var account = allAccounts.FirstOrDefault(a => a.Code == code);
                if (account == null)
                {
                    errors.Add(_t.Get("Accounting.AccountNotFoundAtRow", r, code));
                    continue;
                }

                if (!account.IsLeaf)
                {
                    errors.Add(_t.Get("Accounting.NotALeafAccountAtRow", r, code));
                    continue;
                }

                account.OpeningBalance = balance;
                account.UpdatedAt = TimeHelper.GetEgyptTime();
                successCount++;
            }

            await _db.SaveChangesAsync();
            await _accounting.SyncEntityBalancesAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = _t.Get("Accounting.ProcessingError", ex.Message) });
        }

        return Ok(new { success = true, successCount, errors });
    }

    [HttpGet("mapping-registry")]
    [AllowPosAccess]
    public IActionResult GetMappingRegistry()
    {
        var registry = new List<object>
        {
            // --- Sales & POS ---
            new { key = MappingKeys.Sales, description = _t.Get("Accounting.MappingRegistry.Sales") },
            new { key = MappingKeys.SalesReturn, description = _t.Get("Accounting.MappingRegistry.SalesReturn") },
            new { key = MappingKeys.SalesDiscount, description = _t.Get("Accounting.MappingRegistry.SalesDiscount") },
            new { key = MappingKeys.Customer, description = _t.Get("Accounting.Mapping.CustomerIntermediary") },
            
            // --- Cash & Payments (POS) ---
            new { key = MappingKeys.PosCash, description = _t.Get("Accounting.MappingRegistry.PosCash") },
            new { key = MappingKeys.PosBank, description = _t.Get("Accounting.Mapping.BankVisaPos") },
            new { key = MappingKeys.PosVodafone, description = _t.Get("Accounting.Mapping.VodafoneCashPos") },
            new { key = MappingKeys.PosInstaPay, description = _t.Get("Accounting.MappingRegistry.PosInstaPay") },
            
            // --- Cash & Payments (Website) ---
            new { key = MappingKeys.WebCash, description = _t.Get("Accounting.MappingRegistry.WebCash") },
            new { key = MappingKeys.WebVodafone, description = _t.Get("Accounting.Mapping.VodafoneCashWeb") },
            new { key = MappingKeys.WebInstaPay, description = _t.Get("Accounting.MappingRegistry.WebInstaPay") },
            
            // --- Purchases & Inventory ---
            new { key = MappingKeys.Inventory, description = _t.Get("Accounting.MappingRegistry.Inventory") },
            new { key = MappingKeys.InventoryVariance, description = _t.Get("Accounting.Mapping.InventoryVariance") },
            new { key = MappingKeys.Supplier, description = _t.Get("Accounting.Mapping.SupplierIntermediary") },
            new { key = MappingKeys.PurchaseDiscount, description = _t.Get("Accounting.MappingRegistry.PurchaseDiscount") },
            new { key = MappingKeys.VatInput, description = _t.Get("Accounting.MappingRegistry.VatInput") },
            new { key = MappingKeys.VatOutput, description = _t.Get("Accounting.MappingRegistry.VatOutput") },
            new { key = MappingKeys.DeliveryRevenue, description = _t.Get("Accounting.MappingRegistry.DeliveryRevenue") },
            new { key = MappingKeys.PaymentVoucherCash, description = _t.Get("Accounting.Mapping.DefaultPaymentCash") },
 
            // --- HR & Payroll ---
            new { key = MappingKeys.SalaryExpense, description = _t.Get("Accounting.Mapping.SalaryExpense") },
            new { key = MappingKeys.SalariesPayable, description = _t.Get("Accounting.MappingRegistry.SalariesPayable") },
            new { key = MappingKeys.EmployeeAdvances, description = _t.Get("Accounting.Mapping.EmployeeAdvances") },
            new { key = MappingKeys.EmployeeBonuses, description = _t.Get("Accounting.Mapping.EmployeeBonuses") },
            new { key = MappingKeys.EmployeeDeductions, description = _t.Get("Accounting.Mapping.EmployeeDeductions") },
 
            // --- Fixed Assets ---
            new { key = MappingKeys.DepreciationExpense, description = _t.Get("Accounting.Mapping.DepreciationExpense") },
            new { key = MappingKeys.AccumulatedDepreciation, description = _t.Get("Accounting.MappingRegistry.AccumulatedDepreciation") },
 
            // --- POS Closures ---
            new { key = MappingKeys.PosDailyClosure, description = _t.Get("Accounting.Mapping.PosDailyClosure") },
        };
        return Ok(registry);
    }

    [HttpGet("mappings")]
    [AllowPosAccess]
    public async Task<IActionResult> GetMappings()
    {
        var mappings = await _db.AccountSystemMappings.ToDictionaryAsync(m => m.Key, m => m.AccountId);
        return Ok(mappings);
    }

    [HttpPut("mappings")]
    public async Task<IActionResult> SaveMappingsPut([FromBody] Dictionary<string, int?>? body)
        => await SaveMappingsInternal(body);

    [HttpPost("mappings")]
    public async Task<IActionResult> SaveMappingsPost([FromBody] Dictionary<string, int?>? body)
        => await SaveMappingsInternal(body);

    private async Task<IActionResult> SaveMappingsInternal(Dictionary<string, int?>? body)
    {
        if (body == null) return BadRequest(_t.Get("Accounting.MissingMappingData"));

        foreach (var kvp in body)
        {
            var mapping = await _db.AccountSystemMappings.FirstOrDefaultAsync(m => m.Key == kvp.Key);
            if (mapping != null)
            {
                mapping.AccountId = kvp.Value;
                mapping.UpdatedAt = TimeHelper.GetEgyptTime();
            }
            else
            {
                _db.AccountSystemMappings.Add(new AccountSystemMapping { Key = kvp.Key, AccountId = kvp.Value });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpGet("fix-tree"), HttpPost("fix-tree")]
    public async Task<IActionResult> FixTree()
    {
        try
        {
            var allAccounts = await _db.Accounts.ToListAsync();
            
            // 1. AUTO-LINK BY CODES
            foreach (var a in allAccounts.OrderBy(x => x.Code.Length))
            {
                var code = a.Code;
                if (string.IsNullOrEmpty(code) || code.Length <= 1) {
                    a.ParentId = null;
                    continue;
                }

                // Find the longest existing prefix
                for (int len = code.Length - 1; len >= 1; len--)
                {
                    var prefix = code.Substring(0, len);
                    var parentCandidate = allAccounts.FirstOrDefault(p => p.Code == prefix && p.Id != a.Id);
                    if (parentCandidate != null)
                    {
                        a.ParentId = parentCandidate.Id;
                        break;
                    }
                }
            }

            await _db.SaveChangesAsync();

            // 2. RECURE_STRUCTURE_METRICS (Levels and Leaves)
            foreach (var a in allAccounts) { a.Level = 1; a.IsLeaf = true; }

            void UpdateLevels(int? parentId, int level)
            {
                var children = allAccounts.Where(a => a.ParentId == parentId).ToList();
                foreach (var child in children)
                {
                    child.Level = level;
                    var hasChildren = allAccounts.Any(a => a.ParentId == child.Id);
                    child.IsLeaf = !hasChildren;
                    UpdateLevels(child.Id, level + 1);
                }
            }

            UpdateLevels(null, 1);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, message = _t.Get("Accounting.FixTreeSuccess"), count = allAccounts.Count });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("rebuild"), HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild() => await FixTree();

    [HttpGet("sync-balances"), HttpPost("sync-balances")]
    public async Task<IActionResult> SyncBalances()
    {
        try
        {
            await _accounting.SyncEntityBalancesAsync();
            return Ok(new { success = true, message = _t.Get("Accounting.SyncBalancesSuccess") });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree()
    {
        var all = await _db.Accounts
            .Include(a => a.Lines)
            .Include(a => a.Parent)
            .OrderBy(a => a.Code)
            .ToListAsync();

        var balances = all.ToDictionary(a => a.Id, a => a.Lines.Sum(l => l.Debit - l.Credit));

        List<AccountDto> BuildTree(int? parentId)
        {
            return all
                .Where(a => a.ParentId == parentId)
                .Select(a => {
                    var children = BuildTree(a.Id);
                    var netLinesAmount = balances.GetValueOrDefault(a.Id, 0);
                    
                    var directCurrentBal = a.Nature == AccountNature.Debit ? netLinesAmount : -netLinesAmount;
                    
                    decimal totalCurrentBalance = directCurrentBal;
                    
                    if (children.Any()) 
                    {
                        totalCurrentBalance += children.Sum(c => c.CurrentBalance);
                    }
                    else 
                    {
                        totalCurrentBalance += a.OpeningBalance;
                    }

                    return new AccountDto(
                        a.Id, a.Code, a.NameAr, a.NameEn, a.Description,
                        a.Type.ToString(), a.Nature.ToString(), a.ParentId,
                        a.Parent?.NameAr, a.Level, a.IsLeaf, a.AllowPosting,
                        a.IsActive, a.IsSystem, a.CanReceivePayment, a.OpeningBalance,
                        totalCurrentBalance, children
                    );
                })
                .ToList();
        }

        var tree = BuildTree(null);
        return Ok(tree);
    }

    [HttpPost("initialize-payment-flags")]
    public async Task<IActionResult> InitializePaymentFlags()
    {
        var accounts = await _db.Accounts.ToListAsync();
        
        foreach (var a in accounts) a.CanReceivePayment = false;

        var allowList = new[] {
            "نقدية الكاشير", "فودافون كاش الكاشير", "انستاباي الكاشير",
            "حساب البنك", "نقدية الموقع", "فودافون كاش الموقع", "انستاباي الموقع",
            "نقدية الحسابات", "فودافون كاش الحسابات", "انستاباي الحسابات",
            "جاري الدكتور", "جاري ابراهيم", "جاري حتاته"
        };

        var results = new List<string>();

        foreach (var name in allowList)
        {
            var match = accounts.FirstOrDefault(a => a.NameAr.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                match.CanReceivePayment = true;
                results.Add($"Allowed: {match.NameAr}");
            }
        }
        
        await _db.SaveChangesAsync();
        return Ok(results);
    }

    [HttpPost("post-opening-balances")]
    public async Task<IActionResult> PostOpeningBalancesToJournal()
    {
        var accounts = await _db.Accounts.Where(a => a.IsLeaf && a.OpeningBalance != 0).ToListAsync();
        if (!accounts.Any())
            return BadRequest(_t.Get("Accounting.NoOpeningBalancesToPost"));

        var yearStart = new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        if (await _db.JournalEntries.AnyAsync(e => e.Type == JournalEntryType.OpeningBalance && e.EntryDate >= yearStart && e.Reference == "OPE-INITIAL"))
            return BadRequest(_t.Get("Accounting.OpeningBalancesAlreadyPosted"));

        var openingEquityId = await _core.GetRequiredMappedAccountAsync(MappingKeys.OpeningEquity);

        decimal totalDr = 0;
        decimal totalCr = 0;
        var lines = new List<CreateJournalLineDto>();

        foreach (var a in accounts)
        {
            var isDebit = a.Nature == AccountNature.Debit;
            var dr = isDebit ? a.OpeningBalance : 0;
            var cr = isDebit ? 0 : a.OpeningBalance;

            lines.Add(new CreateJournalLineDto(
                a.Id, dr, cr, _t.Get("Accounting.InitialOpeningBalanceEntry")
            ));

            totalDr += dr;
            totalCr += cr;
        }

        var diff = totalDr - totalCr;
        if (diff != 0)
        {
            lines.Add(new CreateJournalLineDto(
                openingEquityId,
                diff < 0 ? Math.Abs(diff) : 0,
                diff > 0 ? diff : 0,
                _t.Get("Accounting.OpeningBalanceEquityBalancing")
            ));
        }

        var dto = new CreateJournalEntryDto(
            EntryDate: yearStart,
            Reference: "OPE-INITIAL",
            Description: _t.Get("Accounting.OpeningBalancesJournalDescription"),
            Lines: lines,
            Type: JournalEntryType.OpeningBalance,
            CostCenter: (int)OrderSource.General
        );

        var entry = await _accounting.PostManualEntryAsync(dto, User);
        return Ok(new { success = true, entryId = entry.Id, entryNumber = entry.EntryNumber });
    }
}
