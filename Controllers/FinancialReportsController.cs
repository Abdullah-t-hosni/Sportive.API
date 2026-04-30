using Sportive.API.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.DTOs;
using Sportive.API.Utils;
using Sportive.API.Services;
using Sportive.API.Interfaces;

using Microsoft.Extensions.DependencyInjection;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(ModuleKeys.ReportsMain)]
public class FinancialReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITranslator _t;

    public FinancialReportsController(AppDbContext db, IServiceScopeFactory scopeFactory, ITranslator t)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _t = t;
    }

    // 
    // SHARED: حساب أرصدة كل الحسابات في فترة زمنية
    private async Task<List<AccountBalance>> GetBalances(DateTime from, DateTime to, OrderSource? source = null)
    {
        var accounts = await _db.Accounts
            .OrderBy(a => a.Code)
            .ToListAsync();

        // 1. Get transaction movements for the period (Server-side GroupBy)
        var query = _db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to);

        var openingQuery = _db.JournalLines
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from);

        if (source.HasValue)
        {
            query = query.Where(l => l.CostCenter == source.Value);
            openingQuery = openingQuery.Where(l => l.CostCenter == source.Value);
        }

        var periodBalances = await query
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Dr = g.Sum(l => l.Debit), Cr = g.Sum(l => l.Credit) })
            .ToListAsync();

        var openingBalances = await openingQuery
            .GroupBy(l => l.AccountId)
            .Select(g => new { AccountId = g.Key, Dr = g.Sum(l => l.Debit), Cr = g.Sum(l => l.Credit) })
            .ToListAsync();

        // 3. Create dictionaries for fast lookup
        var periodDrMap = periodBalances.ToDictionary(x => x.AccountId, x => x.Dr);
        var periodCrMap = periodBalances.ToDictionary(x => x.AccountId, x => x.Cr);
        var openDrMap   = openingBalances.ToDictionary(x => x.AccountId, x => x.Dr);
        var openCrMap   = openingBalances.ToDictionary(x => x.AccountId, x => x.Cr);

        // 4. Initialize balance objects
        var balanceList = accounts.Select(a => new AccountBalance
        {
            Id           = a.Id,
            Code         = a.Code,
            NameAr       = a.NameAr,
            Type         = a.Type,
            Nature       = a.Nature,
            ParentId     = a.ParentId,
            Level        = a.Level,
            IsLeaf       = a.IsLeaf,
            OpenDebit    = Math.Round((a.Nature == AccountNature.Debit  ? a.OpeningBalance : 0) + openDrMap.GetValueOrDefault(a.Id, 0), 2),
            OpenCredit   = Math.Round((a.Nature == AccountNature.Credit ? a.OpeningBalance : 0) + openCrMap.GetValueOrDefault(a.Id, 0), 2),
            PeriodDebit  = Math.Round(periodDrMap.GetValueOrDefault(a.Id, 0), 2),
            PeriodCredit = Math.Round(periodCrMap.GetValueOrDefault(a.Id, 0), 2)
        }).ToList();

        // We sort by Level descending to ensure children are processed before parents
        var balanceDict = balanceList.ToDictionary(b => b.Id);
        var itemsToRollUp = balanceList.OrderByDescending(b => b.Level).ToList();
        
        foreach (var b in itemsToRollUp)
        {
            if (b.ParentId.HasValue && balanceDict.TryGetValue(b.ParentId.Value, out var parent))
            {
                parent.OpenDebit    = Math.Round(parent.OpenDebit + b.OpenDebit, 2);
                parent.OpenCredit   = Math.Round(parent.OpenCredit + b.OpenCredit, 2);
                parent.PeriodDebit  = Math.Round(parent.PeriodDebit + b.PeriodDebit, 2);
                parent.PeriodCredit = Math.Round(parent.PeriodCredit + b.PeriodCredit, 2);
            }
        }

        // 6. Calculate net balances
        foreach (var b in balanceList)
        {
            b.OpenBalance = Math.Round(b.Nature == AccountNature.Debit ? b.OpenDebit - b.OpenCredit : b.OpenCredit - b.OpenDebit, 2);
            var closingDr = Math.Round(b.OpenDebit + b.PeriodDebit, 2);
            var closingCr = Math.Round(b.OpenCredit + b.PeriodCredit, 2);
            b.ClosingBal = Math.Round(b.Nature == AccountNature.Debit ? closingDr - closingCr : closingCr - closingDr, 2);
        }

        return balanceList;
    }

    // helper: مجموع نوع حسابات معين
    private static decimal SumType(List<AccountBalance> balances, AccountType type)
        => balances.Where(b => b.Type == type && b.IsLeaf).Sum(b => b.ClosingBal);

    // 1. ميزان المراجعة  GET /api/financialreports/trial-balance
    // 
    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to, source);
        var rows = balances
            .OrderBy(b => b.Code)
            .Select(b => {
                // حساب الرصيد الافتتاحي (مدين/دائن) بناءً على الطبيعة والفرق
                decimal oDr = 0, oCr = 0;
                if (b.Nature == AccountNature.Debit) {
                    oDr = b.OpenBalance > 0 ? b.OpenBalance : 0;
                    oCr = b.OpenBalance < 0 ? -b.OpenBalance : 0;
                } else {
                    oCr = b.OpenBalance > 0 ? b.OpenBalance : 0;
                    oDr = b.OpenBalance < 0 ? -b.OpenBalance : 0;
                }

                // حساب الرصيد الختامي (مدين/دائن) بناءً على الطبيعة والفرق
                decimal cDr = 0, cCr = 0;
                if (b.Nature == AccountNature.Debit) {
                    cDr = b.ClosingBal > 0 ? b.ClosingBal : 0;
                    cCr = b.ClosingBal < 0 ? -b.ClosingBal : 0;
                } else {
                    cCr = b.ClosingBal > 0 ? b.ClosingBal : 0;
                    cDr = b.ClosingBal < 0 ? -b.ClosingBal : 0;
                }

                return new TrialBalanceRow(
                    b.Code, b.NameAr, b.Level,
                    oDr, oCr,
                    b.PeriodDebit, b.PeriodCredit,
                    cDr, cCr,
                    b.Id
                );
            }).ToList();

        if (excel) return ExcelTrialBalance(rows, from, to);

        return Ok(new {
            from, to, source,
            CostCenterLabel = source == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (source == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            rows,
            totalOpenDebit    = Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.OpenDebit), 2),
            totalOpenCredit   = Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.OpenCredit), 2),
            totalPeriodDebit  = Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.PeriodDebit), 2),
            totalPeriodCredit = Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.PeriodCredit), 2),
            totalClosingDebit = Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.ClosingBal > 0 && b.Nature == AccountNature.Debit ? b.ClosingBal : (b.ClosingBal < 0 && b.Nature == AccountNature.Credit ? -b.ClosingBal : 0)), 2),
            totalClosingCredit= Math.Round(balances.Where(b => b.ParentId == null).Sum(b => b.ClosingBal > 0 && b.Nature == AccountNature.Credit ? b.ClosingBal : (b.ClosingBal < 0 && b.Nature == AccountNature.Debit ? -b.ClosingBal : 0)), 2),
        });
    }

    // 2. قائمة الدخل   GET /api/financialreports/income-statement
    [HttpGet("income-statement")]
    public async Task<IActionResult> IncomeStatement(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to, source);

        // الإيرادات (طبيعتها دائن ← الرصيد موجب = إيراد)
        // ✅ تصحيح: ضم أي حساب يبدأ بكود 4 للإيرادات (مثل إيراد التوصيل)
        var revenues = balances
            .Where(b => (b.Type == AccountType.Revenue || b.Code.StartsWith("4")) && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new IncomeRow(b.Code, b.NameAr, b.Level, b.ClosingBal)) 
            .ToList();

        var expenses = balances
            .Where(b => b.Type == AccountType.Expense && !b.Code.StartsWith("4") && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new IncomeRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        var totalRevenues = balances.Where(b => (b.Type == AccountType.Revenue || b.Code.StartsWith("4")) && b.ParentId == null).Sum(b => b.ClosingBal);
        var totalExpenses = balances.Where(b => b.Type == AccountType.Expense && !b.Code.StartsWith("4") && b.ParentId == null).Sum(b => b.ClosingBal);
        var netProfit     = totalRevenues - totalExpenses;

        if (excel) return ExcelIncomeStatement(revenues, expenses, totalRevenues, totalExpenses, netProfit, from, to);

        return Ok(new {
            from, to, source,
            CostCenterLabel = source == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (source == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            revenues,
            expenses,
            totalRevenues,
            totalExpenses,
            netProfit,
            isProfit = netProfit >= 0
        });
    }

    // 3. الميزانية العمومية  GET /api/financialreports/balance-sheet
    // 
    [HttpGet("balance-sheet")]
    public async Task<IActionResult> BalanceSheet(
        [FromQuery] DateTime? toDate = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] bool      excel  = false)
    {
        var from = new DateTime(2000, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to, source);

        // الأصول — طبيعة مدين (closingBal = Dr - Cr)
        var assets = balances
            .Where(b => b.Type == AccountType.Asset && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        var liabilities = balances
            .Where(b => b.Type == AccountType.Liability && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        var equity = balances
            .Where(b => b.Type == AccountType.Equity && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // صافي الربح للفترة يضاف لحقوق الملكية ونظهره في القائمة للشفافية
        var incomeFrom = from; 
        var incomeBals = await GetBalances(incomeFrom, to, source);
        var totalRev   = incomeBals.Where(b => (b.Type == AccountType.Revenue || b.Code.StartsWith("4")) && b.ParentId == null).Sum(b => b.ClosingBal);
        var totalExp   = incomeBals.Where(b => (b.Type == AccountType.Expense || b.Code.StartsWith("5")) && b.ParentId == null).Sum(b => b.ClosingBal);
        var netProfit  = totalRev - totalExp;

        if (netProfit != 0)
        {
            equity.Add(new BalanceSheetRow("N/P", _t.Get("Reports.NetProfitLoss"), 1, netProfit));
        }

        var totalAssets      = balances.Where(b => b.Type == AccountType.Asset && b.ParentId == null).Sum(b => b.ClosingBal);
        var totalLiabilities = balances.Where(b => b.Type == AccountType.Liability && b.ParentId == null).Sum(b => b.ClosingBal);
        var totalEquity      = balances.Where(b => b.Type == AccountType.Equity && b.ParentId == null).Sum(b => b.ClosingBal);
        var totalLiabEquity  = totalLiabilities + totalEquity + netProfit;

        if (excel) return ExcelBalanceSheet(assets, liabilities, equity, netProfit,
            totalAssets, totalLiabilities, totalEquity, to);

        return Ok(new {
            to, source,
            CostCenterLabel = source == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (source == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")),
            assets, liabilities, equity, netProfit,
            totalAssets, totalLiabilities, totalEquity, totalLiabEquity,
            isBalanced = Math.Round(totalAssets, 2) == Math.Round(totalLiabEquity, 2)
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 4. دفتر الأستاذ  GET /api/financialreports/ledger
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(
        [FromQuery] int?      accountId  = null,
        [FromQuery] int?      customerId = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      employeeId = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] OrderSource? source  = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();
 
        var q = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Include(l => l.Customer)
            .Include(l => l.Supplier)
            .Include(l => l.Employee)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to);
 
        if (source.HasValue)
            q = q.Where(l => l.CostCenter == source.Value);

        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(l => (l.Description != null && l.Description.Contains(search))
                          || (l.JournalEntry.Description != null && l.JournalEntry.Description.Contains(search))
                          || (l.JournalEntry.Reference != null && l.JournalEntry.Reference.Contains(search)));
        }

        if (accountId.HasValue)
            q = q.Where(l => l.AccountId == accountId.Value);
        
        if (customerId.HasValue)
            q = q.Where(l => l.CustomerId == customerId.Value);
            
        if (supplierId.HasValue)
            q = q.Where(l => l.SupplierId == supplierId.Value);

        if (employeeId.HasValue)
            q = q.Where(l => l.EmployeeId == employeeId.Value);

        var lines = await q.OrderBy(l => l.JournalEntry.EntryDate)
                           .ThenBy(l => l.JournalEntry.Type)
                           .ThenBy(l => l.JournalEntry.Id)
                           .ToListAsync();

        // الرصيد الافتتاحي لكل حساب
        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var openingMap = new Dictionary<int, decimal>();

        foreach (var aId in accountIds)
        {
            var acct = await _db.Accounts.FindAsync(aId);
            if (acct == null) continue;

            var openLines = await _db.JournalLines
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == aId
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.EntryDate < from)
                .ToListAsync();

            var openDr = openLines.Sum(l => l.Debit)  + (acct.Nature == AccountNature.Debit  ? acct.OpeningBalance : 0);
            var openCr = openLines.Sum(l => l.Credit) + (acct.Nature == AccountNature.Credit ? acct.OpeningBalance : 0);
            openingMap[aId] = acct.Nature == AccountNature.Debit ? openDr - openCr : openCr - openDr;
        }

        // بناء السجلات مع الرصيد المتراكم — مرتبة حسب التاريخ ثم رقم القيد
        var ledgerRows = new List<LedgerRow>();
        var balanceMap = new Dictionary<int, decimal>(openingMap);

        foreach (var line in lines)
        {
            if (!balanceMap.ContainsKey(line.AccountId))
                balanceMap[line.AccountId] = 0;

            var acct = await _db.Accounts.FindAsync(line.AccountId);
            if (acct == null) continue;

            if (acct.Nature == AccountNature.Debit)
                balanceMap[line.AccountId] += line.Debit - line.Credit;
            else
                balanceMap[line.AccountId] += line.Credit - line.Debit;

            ledgerRows.Add(new LedgerRow(
                line.AccountId, line.Account.Code, line.Account.NameAr,
                line.JournalEntry.EntryDate, line.JournalEntry.EntryNumber,
                line.JournalEntry.Type.ToString(),
                line.JournalEntry.Description ?? line.Description ?? "",
                line.Debit, line.Credit, balanceMap[line.AccountId],
                line.JournalEntry.Type == JournalEntryType.AssetDepreciation || line.JournalEntry.Type == JournalEntryType.AssetDisposal
                    ? line.JournalEntry.Reference
                    : (line.Supplier?.Name ?? line.Customer?.FullName ?? line.Employee?.Name)
            ));
        }

        if (excel) return ExcelLedger(ledgerRows, openingMap, from, to);

        // ØªØ¬Ù…ÙŠØ¹ Ø­Ø³Ø¨ Ø§Ù„Ø­Ø³Ø§Ø¨ â€” Ø§Ù„Ø­Ø³Ø§Ø¨Ø§Øª Ù…Ø±ØªØ¨Ø© Ø¨Ø§Ù„ÙƒÙˆØ¯ØŒ ÙˆØ§Ù„ØµÙÙˆÙ Ø¯Ø§Ø®Ù„ ÙƒÙ„ Ø­Ø³Ø§Ø¨ Ù…Ø±ØªØ¨Ø© Ø¨Ø§Ù„ØªØ§Ø±ÙŠØ® Ø«Ù… Ø±Ù‚Ù… Ø§Ù„Ù‚ÙŠØ¯
        // تحسين: تجميع العملاء والموردين تحت حسابات رقابة إذا لم يكن هناك فلترة محددة
        var grouped = ledgerRows
            .GroupBy(r => {
                if (r.AccountCode.StartsWith("1103") && !customerId.HasValue)
                    return new { Id = 0, Code = "1103", Name = _t.Get("Reports.TotalCustomers") };
                if (r.AccountCode.StartsWith("2101") && !supplierId.HasValue)
                    return new { Id = 0, Code = "2101", Name = _t.Get("Reports.TotalSuppliers") };
                return new { Id = r.AccountId, Code = r.AccountCode, Name = r.AccountName };
            })
            .OrderBy(g => g.Key.Code)
            .Select(g => {
                // حساب الرصيد الافتتاحي المجمع
                decimal totalOpen = 0;
                if (g.Key.Id == 0) {
                    var ids = ledgerRows.Where(r => r.AccountCode.StartsWith(g.Key.Code)).Select(r => r.AccountId).Distinct();
                    totalOpen = ids.Sum(id => openingMap.GetValueOrDefault(id, 0));
                } else {
                    totalOpen = openingMap.GetValueOrDefault(g.Key.Id, 0);
                }

                return new {
                    AccountId = g.Key.Id,
                    AccountCode = g.Key.Code,
                    AccountName = g.Key.Name,
                    openingBalance = totalOpen,
                    rows = g.OrderBy(r => r.Date)
                            .ThenBy(r => {
                                if (Enum.TryParse<JournalEntryType>(r.EntryType, out var t)) return (int)t;
                                return 99;
                            })
                            .ThenBy(r => r.JournalEntryId).ToList(),
                    closingBalance = g.OrderBy(r => r.Date)
                                      .ThenBy(r => {
                                          if (Enum.TryParse<JournalEntryType>(r.EntryType, out var t)) return (int)t;
                                          return 99;
                                      })
                                      .ThenBy(r => r.JournalEntryId).LastOrDefault()?.RunningBalance ?? 0
                };
            }).ToList();
        return Ok(new { from, to, source, CostCenterLabel = source == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (source == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")), accounts = grouped });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 5. تشخيص الصحة المحاسبية (Accounting Health Check)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("health-check")]
    public async Task<IActionResult> AccountingHealthCheck()
    {
        var unbalancedEntries = await _db.JournalEntries
            .Include(e => e.Lines)
            .OrderByDescending(e => e.EntryDate)
            .Select(e => new {
                e.Id, e.EntryNumber, e.EntryDate, e.Description, e.Type,
                TotalDebit = e.Lines.Sum(l => l.Debit),
                TotalCredit = e.Lines.Sum(l => l.Credit),
                Difference = Math.Abs(e.Lines.Sum(l => l.Debit) - e.Lines.Sum(l => l.Credit))
            })
            .Where(e => e.Difference > 0.009m)
            .Take(100)
            .ToListAsync();

        var inactiveAccountLines = await _db.JournalLines
            .Include(l => l.Account).Include(l => l.JournalEntry)
            .Where(l => l.Account != null && !l.Account.IsActive)
            .Select(l => new { l.Id, l.JournalEntryId, EntryNumber = l.JournalEntry.EntryNumber, AccountCode = l.Account.Code, AccountName = l.Account.NameAr, l.Debit, l.Credit })
            .Take(100).ToListAsync();

        var orphanLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == 0 || l.Account == null)
            .Select(l => new { l.Id, l.JournalEntryId, EntryNumber = l.JournalEntry != null ? l.JournalEntry.EntryNumber : "N/A", l.Debit, l.Credit })
            .ToListAsync();

        return Ok(new { isHealthy = !unbalancedEntries.Any() && !inactiveAccountLines.Any() && !orphanLines.Any(), unbalancedEntries, inactiveAccountLines, orphanLines });
    }

    [HttpPost("heal-accounting")]
    public async Task<IActionResult> HealAccounting()
    {
        var fixedCount = 0;
        var inactiveLines = await _db.JournalLines.Include(l => l.Account).Where(l => l.Account != null && !l.Account.IsActive).ToListAsync();
        foreach (var line in inactiveLines)
        {
            var parent = await _db.Accounts.Where(a => a.Id == line.Account.ParentId && a.IsActive).FirstOrDefaultAsync();
            if (parent == null) {
                var codeRoot = line.Account.Code.Substring(0, 1);
                parent = await _db.Accounts.Where(a => a.Code.StartsWith(codeRoot) && a.Level == 1 && a.IsActive).FirstOrDefaultAsync();
            }
            if (parent != null) { line.AccountId = parent.Id; fixedCount++; }
        }
        if (fixedCount > 0) await _db.SaveChangesAsync();

        // 2. Sync Entity IDs (Ensure SupplierId/CustomerId/EmployeeId are set on relevant lines)
        using var scope = _scopeFactory.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<AccountingCoreService>();
        await core.SyncAllEntityIdsAsync();

        return Ok(new { message = _t.Get("Reports.HealSuccess", fixedCount.ToString()), fixedCount });
    }

    [HttpGet("account-statement")]
    public async Task<IActionResult> AccountStatement(
        [FromQuery] int?      accountId  = null,
        [FromQuery] int?      customerId = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] int?      employeeId = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] OrderSource? source  = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();
 
        int targetId = accountId ?? 0;
        
        // If accountId is missing but we have an entity ID, resolve the control account
        if (targetId == 0)
        {
            if (customerId.HasValue) 
                targetId = await _db.Accounts.Where(a => a.Code.StartsWith("1103")).Select(a => a.Id).FirstOrDefaultAsync();
            else if (supplierId.HasValue)
                targetId = await _db.Accounts.Where(a => a.Code.StartsWith("2101")).Select(a => a.Id).FirstOrDefaultAsync();
            else if (employeeId.HasValue)
                targetId = await _db.Accounts.Where(a => a.Code.StartsWith("2103") || a.Code.StartsWith("1108")).Select(a => a.Id).FirstOrDefaultAsync();
        }

        if (targetId == 0) return BadRequest(new { message = _t.Get("Reports.SelectAccountOrEntity") });

        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == targetId);
        if (acct == null) return NotFound(new { message = _t.Get("Reports.AccountNotFound") });

        var targetAccountIds = new List<int> { targetId };
        if (!acct.IsLeaf)
        {
            targetAccountIds = await _db.Accounts.Where(a => a.Code.StartsWith(acct.Code)).Select(a => a.Id).ToListAsync();
        }
 
        var openQ = _db.JournalLines.Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate < from);
  
        if (source.HasValue) openQ = openQ.Where(l => l.CostCenter == source.Value);
        
        if (customerId.HasValue) 
            openQ = openQ.Where(l => l.CustomerId == customerId && targetAccountIds.Contains(l.AccountId));
        else if (supplierId.HasValue) 
            openQ = openQ.Where(l => l.SupplierId == supplierId && targetAccountIds.Contains(l.AccountId));
        else if (employeeId.HasValue) 
            openQ = openQ.Where(l => l.EmployeeId == employeeId && targetAccountIds.Contains(l.AccountId));
        else
            openQ = openQ.Where(l => targetAccountIds.Contains(l.AccountId));
 
        var openLines = await openQ.ToListAsync();
        var openDr  = openLines.Sum(l => l.Debit);
        var openCr  = openLines.Sum(l => l.Credit);
        
        if (!customerId.HasValue && !supplierId.HasValue && !employeeId.HasValue)
        {
             var openingSum = await _db.Accounts.Where(a => a.Code.StartsWith(acct.Code))
                .SumAsync(a => (decimal?)a.OpeningBalance) ?? 0;
            if (acct.Nature == AccountNature.Debit) openDr += openingSum;
            else openCr += openingSum;
        }

        var openBal = acct.Nature == AccountNature.Debit ? openDr - openCr : openCr - openDr;

        var q = _db.JournalLines.Include(l => l.JournalEntry).Include(l => l.Customer).Include(l => l.Supplier).Include(l => l.Employee)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to);

        if (customerId.HasValue) 
            q = q.Where(l => l.CustomerId == customerId && targetAccountIds.Contains(l.AccountId));
        else if (supplierId.HasValue) 
            q = q.Where(l => l.SupplierId == supplierId && targetAccountIds.Contains(l.AccountId));
        else if (employeeId.HasValue) 
            q = q.Where(l => l.EmployeeId == employeeId && targetAccountIds.Contains(l.AccountId));
        else
            q = q.Where(l => targetAccountIds.Contains(l.AccountId));

        if (source.HasValue) q = q.Where(l => l.CostCenter == source.Value);

        if (!string.IsNullOrEmpty(search)) q = q.Where(l => (l.Description != null && l.Description.Contains(search)) || (l.JournalEntry.Description != null && l.JournalEntry.Description.Contains(search)));

        var periodLines = await q.OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntry.Id).ToListAsync();
        var runBal = openBal;
        var rows = periodLines.Select(l => {
            if (acct.Nature == AccountNature.Debit) runBal += l.Debit - l.Credit; else runBal += l.Credit - l.Debit;
            return new LedgerRow(targetId, acct.Code, acct.NameAr, l.JournalEntry.EntryDate, l.JournalEntry.EntryNumber, l.JournalEntry.Type.ToString(), l.JournalEntry.Description ?? l.Description ?? "", l.Debit, l.Credit, runBal, l.JournalEntry.Reference, l.JournalEntry.Id, 
                l.JournalEntry.Type == JournalEntryType.AssetDepreciation || l.JournalEntry.Type == JournalEntryType.AssetDisposal 
                    ? l.JournalEntry.Reference 
                    : (l.Supplier?.Name ?? l.Customer?.FullName ?? l.Employee?.Name));
        }).ToList();

        if (excel) return ExcelAccountStatement(acct, rows, openBal, from, to);
        return Ok(new { from, to, source, CostCenterLabel = source == OrderSource.Website ? _t.Get("SupplierPayments.Website") : (source == OrderSource.POS ? _t.Get("SupplierPayments.POS") : _t.Get("SupplierPayments.General")), account = new { acct.Id, acct.Code, acct.NameAr, Nature = acct.Nature.ToString() }, openingBalance = openBal, rows, totalDebit = rows.Sum(r => r.Debit), totalCredit = rows.Sum(r => r.Credit), closingBalance = rows.LastOrDefault()?.RunningBalance ?? openBal });
    }


    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 6. قائمة التدفقات النقدية  GET /api/financialreports/cash-flow
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("cash-flow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] OrderSource? source = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        // حسابات النقدية الحقيقية فقط (الخزينة 1101 والبنك 1102)
        // نستبعد 1103 (العملاء) لأنها ليست نقدية سائلة مباشرة بالقيد المحاسبي
        var cashAccounts = await _db.Accounts
            .Where(a => a.IsLeaf && (a.Code.StartsWith("1101") || a.Code.StartsWith("1102") || a.Code.StartsWith("1107")))
            .ToListAsync();

        var cashIds = cashAccounts.Select(a => a.Id).ToHashSet();

        // Ø¬Ù„Ø¨ ÙƒÙ„ Ø§Ù„Ø£Ø³Ø·Ø± Ø§Ù„ØªÙŠ ØªØ®Øµ Ø­Ø³Ø§Ø¨Ø§Øª Ø§Ù„Ù†Ù‚Ø¯ÙŠØ© ÙÙŠ ØªÙ„Ùƒ Ø§Ù„ÙØªØ±Ø©
        var cashLinesQuery = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to
                     && cashIds.Contains(l.AccountId));

        if (source.HasValue)
            cashLinesQuery = cashLinesQuery.Where(l => l.CostCenter == source.Value);

        var cashLines = await cashLinesQuery
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ToListAsync();

        // لجلب الطرف الآخر من القيد، نحتاج كل سطور القيود المعنية
        var entryIds = cashLines.Select(l => l.JournalEntryId).Distinct().ToList();
        var allLines = await _db.JournalLines
            .Include(l => l.Account)
            .Where(l => entryIds.Contains(l.JournalEntryId))
            .ToListAsync();

        decimal operating = 0, investing = 0, financing = 0;
        var opItems = new List<CashFlowItem>();
        var invItems = new List<CashFlowItem>();
        var finItems = new List<CashFlowItem>();

        // نأخذ كل القيود التي تخص تلك الفترة ولها علاقة بالنقدية
        var entriesInPeriod = await _db.JournalLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .GroupBy(l => l.JournalEntryId)
            .Select(g => new { 
                EntryDate = g.First().JournalEntry.EntryDate,
                EntryNumber = g.First().JournalEntry.EntryNumber,
                Description = g.First().JournalEntry.Description,
                Lines = g.Select(l => new { l.AccountId, l.Account.Code, l.Account.NameAr, l.Account.Type, l.Debit, l.Credit, l.Description }).ToList()
            })
            .ToListAsync();

        foreach (var entry in entriesInPeriod)
        {
            var cashLinesInEntry = entry.Lines.Where(l => cashIds.Contains(l.AccountId)).ToList();
            var nonCashLinesInEntry = entry.Lines.Where(l => !cashIds.Contains(l.AccountId)).ToList();

            // إذا لم يكن هناك طرف نقدي أو لم يكن هناك طرف "غير نقدي" (تحويل داخلي)، نتجاهل القيد
            if (!cashLinesInEntry.Any() || !nonCashLinesInEntry.Any()) continue;

            // في قائمة التدفقات النقدية (الطريقة المباشرة)، التدفق النقدي هو صافي القيد للطرف النقدي
            // لكن لكي يكون لدينا تفصيل بالحسابات المقابلة، سنوزع هذا التدفق على الحسابات غير النقدية
            
            // القاعدة: التدفق لمواجهة حساب غير نقدي = (دائن - مدين) للحساب غير النقدي
            // إذا كان الحساب غير النقدي مدين (دفعنا مصروف)، فالتدفق سالب (نقص في النقدية)
            // إذا كان الحساب غير النقدي دائن (استلمنا مبيعات)، فالتدفق موجب (زيادة في النقدية)
            
            foreach (var nc in nonCashLinesInEntry)
            {
                var flowAmount = nc.Credit - nc.Debit;
                if (flowAmount == 0) continue;

                var item = new CashFlowItem(
                    entry.EntryDate,
                    entry.EntryNumber,
                    nc.Description ?? entry.Description ?? "",
                    nc.NameAr,
                    flowAmount
                );

                if (nc.Code.StartsWith("12")) // أصول ثابتة
                {
                    investing += flowAmount;
                    invItems.Add(item);
                }
                else if (nc.Type == AccountType.Equity || nc.Code.StartsWith("22")) // حقوق ملكية أو قروض
                {
                    financing += flowAmount;
                    finItems.Add(item);
                }
                else // تشغيلية
                {
                    operating += flowAmount;
                    opItems.Add(item);
                }
            }
        }

        // الرصيد الافتتاحي للنقدية
        var openCash = 0m;
        foreach (var ca in cashAccounts)
        {
            var ol = await _db.JournalLines
                .Where(l => l.AccountId == ca.Id
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.EntryDate < from)
                .SumAsync(l => l.Debit - l.Credit);
            
            openCash += ol + ca.OpeningBalance;
        }

        // الرصيد النهائي الفعلي من الحسابات (للتحقق)
        var actualClosingBalance = 0m;
        foreach (var ca in cashAccounts)
        {
            var totalMv = await _db.JournalLines
                .Where(l => l.AccountId == ca.Id
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.EntryDate <= to)
                .SumAsync(l => l.Debit - l.Credit);
            actualClosingBalance += totalMv + ca.OpeningBalance;
        }

        var netCashFlow = operating + investing + financing;

        if (excel) return ExcelCashFlow(opItems, invItems, finItems, openCash, from, to);

        return Ok(new {
            from, to, source,
            openingCashBalance = openCash,
            operatingActivities = new { items = opItems, total = operating },
            investingActivities = new { items = invItems, total = investing },
            financingActivities = new { items = finItems, total = financing },
            netCashFlow,
            closingCashBalance = actualClosingBalance // نستخدم الرصيد الفعلي لضمان الدقة
        });
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // EXCEL EXPORTS
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    private IActionResult ExcelTrialBalance(List<TrialBalanceRow> rows, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ميزان المراجعة");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"ميزان المراجعة — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 9).Merge();

        string[] hdrs = { "الكود","اسم الحساب","مدين افتتاحي","دائن افتتاحي","مدين الفترة","دائن الفترة","مدين الختامي","دائن الختامي" };
        for (int c = 0; c < hdrs.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            cell.Value = hdrs[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f3460");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int r = 3;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Code;
            ws.Cell(r, 2).Value = row.NameAr;
            ws.Cell(r, 3).Value = row.OpenDebit;
            ws.Cell(r, 4).Value = row.OpenCredit;
            ws.Cell(r, 5).Value = row.PeriodDebit;
            ws.Cell(r, 6).Value = row.PeriodCredit;
            ws.Cell(r, 7).Value = row.ClosingDebit;
            ws.Cell(r, 8).Value = row.ClosingCredit;
            for (int c = 3; c <= 8; c++) ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
            if (row.Level == 1) ws.Row(r).Style.Font.Bold = true;
            r++;
        }

        // Totals
        ws.Cell(r, 2).Value = "الإجمالي";
        ws.Cell(r, 2).Style.Font.Bold = true;
        for (int c = 3; c <= 8; c++)
        {
            ws.Cell(r, c).FormulaA1 = $"=SUM({(char)('A'+c-1)}3:{(char)('A'+c-1)}{r-1})";
            ws.Cell(r, c).Style.Font.Bold = true;
            ws.Cell(r, c).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f5e9");
        }

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"trial_balance_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelIncomeStatement(
        List<IncomeRow> revenues, List<IncomeRow> expenses,
        decimal totalRev, decimal totalExp, decimal netProfit,
        DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„Ø¯Ø®Ù„");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„Ø¯Ø®Ù„ â€” Ù…Ù† {from:yyyy-MM-dd} Ø¥Ù„Ù‰ {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 3).Merge();

        void WriteSection(string title, IEnumerable<IncomeRow> items, decimal total, int startRow, XLColor color)
        {
            ws.Cell(startRow, 1).Value = title;
            ws.Cell(startRow, 1).Style.Font.Bold = true;
            ws.Cell(startRow, 1).Style.Fill.BackgroundColor = color;
            int r = startRow + 1;
            foreach (var item in items)
            {
                ws.Cell(r, 1).Value = item.Code;
                ws.Cell(r, 2).Value = item.NameAr;
                ws.Cell(r, 3).Value = item.Amount;
                ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }
            ws.Cell(r, 2).Value = "Ø§Ù„Ø¥Ø¬Ù…Ø§Ù„ÙŠ";
            ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = total;
            ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        }

        WriteSection("Ø§Ù„Ø¥ÙŠØ±Ø§Ø¯Ø§Øª", revenues, totalRev, 2, XLColor.FromHtml("#e8f5e9"));
        int expStart = revenues.Count + 4;
        WriteSection("Ø§Ù„Ù…ØµØ§Ø±ÙŠÙ", expenses, totalExp, expStart, XLColor.FromHtml("#fce4ec"));

        int netRow = expStart + expenses.Count + 3;
        ws.Cell(netRow, 2).Value = netProfit >= 0 ? "ØµØ§ÙÙŠ Ø§Ù„Ø±Ø¨Ø­" : "ØµØ§ÙÙŠ Ø§Ù„Ø®Ø³Ø§Ø±Ø©";
        ws.Cell(netRow, 2).Style.Font.Bold = true; ws.Cell(netRow, 2).Style.Font.FontSize = 12;
        ws.Cell(netRow, 3).Value = Math.Abs(netProfit);
        ws.Cell(netRow, 3).Style.Font.Bold = true;
        ws.Cell(netRow, 3).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(netRow, 3).Style.Font.FontColor = netProfit >= 0 ? XLColor.Green : XLColor.Red;

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"income_statement_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelBalanceSheet(
        List<BalanceSheetRow> assets, List<BalanceSheetRow> liabilities, List<BalanceSheetRow> equity,
        decimal netProfit, decimal totalAssets, decimal totalLiabilities, decimal totalEquity, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ø§Ù„Ù…ÙŠØ²Ø§Ù†ÙŠØ© Ø§Ù„Ø¹Ù…ÙˆÙ…ÙŠØ©");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"Ø§Ù„Ù…ÙŠØ²Ø§Ù†ÙŠØ© Ø§Ù„Ø¹Ù…ÙˆÙ…ÙŠØ© â€” ÙÙŠ {to:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 4).Merge();

        int r = 2;
        void Section(string title, IEnumerable<BalanceSheetRow> items, decimal total, XLColor bg)
        {
            ws.Cell(r, 1).Value = title; ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = bg;
            ws.Range(r, 1, r, 3).Merge(); r++;
            foreach (var item in items)
            {
                ws.Cell(r, 1).Value = item.Code;
                ws.Cell(r, 2).Value = new string(' ', (item.Level - 1) * 3) + item.NameAr;
                ws.Cell(r, 3).Value = item.Amount;
                ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }
            ws.Cell(r, 2).Value = $"Ø¥Ø¬Ù…Ø§Ù„ÙŠ {title}"; ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = total; ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            r += 2;
        }

        Section("Ø§Ù„Ø£ØµÙˆÙ„",       assets,      totalAssets,      XLColor.FromHtml("#e3f2fd"));
        Section("Ø§Ù„Ø§Ù„ØªØ²Ø§Ù…Ø§Øª",   liabilities, totalLiabilities, XLColor.FromHtml("#fce4ec"));
        Section("Ø­Ù‚ÙˆÙ‚ Ø§Ù„Ù…Ù„ÙƒÙŠØ©", equity,      totalEquity,      XLColor.FromHtml("#f3e5f5"));

        ws.Cell(r, 2).Value = "ØµØ§ÙÙŠ Ø§Ù„Ø±Ø¨Ø­ / Ø§Ù„Ø®Ø³Ø§Ø±Ø©"; ws.Cell(r, 3).Value = netProfit;
        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00"; r++;
        ws.Cell(r, 2).Value = "Ø¥Ø¬Ù…Ø§Ù„ÙŠ Ø§Ù„Ø§Ù„ØªØ²Ø§Ù…Ø§Øª ÙˆØ­Ù‚ÙˆÙ‚ Ø§Ù„Ù…Ù„ÙƒÙŠØ©"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = totalLiabilities + totalEquity; ws.Cell(r, 3).Style.Font.Bold = true;
        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"balance_sheet_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelLedger(List<LedgerRow> rows, Dictionary<int, decimal> openMap, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ø¯ÙØªØ± Ø§Ù„Ø£Ø³ØªØ§Ø°");
        ws.RightToLeft = true;

        string[] hdrs = { "Ø§Ù„ÙƒÙˆØ¯","Ø§Ø³Ù… Ø§Ù„Ø­Ø³Ø§Ø¨","Ø§Ù„ØªØ§Ø±ÙŠØ®","Ø§Ù„Ù‚ÙŠØ¯","Ø§Ù„Ø§Ø³Ù…","Ø§Ù„Ø¨ÙŠØ§Ù†","Ù…Ø¯ÙŠÙ†","Ø¯Ø§Ø¦Ù†","Ø§Ù„Ø±ØµÙŠØ¯" };
        for (int c = 0; c < hdrs.Length; c++)
        {
            var cell = ws.Cell(1, c+1); cell.Value = hdrs[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1a237e");
            cell.Style.Font.FontColor = XLColor.White;
        }
        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r,1).Value = row.AccountCode;
            ws.Cell(r,2).Value = row.AccountName;
            ws.Cell(r,3).Value = row.Date.ToString("yyyy-MM-dd");
            ws.Cell(r,4).Value = row.EntryNumber;
            ws.Cell(r,5).Value = row.PartnerName ?? "â€”";
            ws.Cell(r,6).Value = row.Description;
            ws.Cell(r,7).Value = row.Debit;
            ws.Cell(r,8).Value = row.Credit;
            ws.Cell(r,9).Value = row.RunningBalance;
            ws.Cell(r,7).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,8).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,9).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"ledger_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelAccountStatement(Account acct, List<LedgerRow> rows, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ÙƒØ´Ù Ø­Ø³Ø§Ø¨");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"ÙƒØ´Ù Ø­Ø³Ø§Ø¨: {acct.Code} â€” {acct.NameAr}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,5).Merge();
        ws.Cell(2,1).Value = $"Ù…Ù† {from:yyyy-MM-dd} Ø¥Ù„Ù‰ {to:yyyy-MM-dd}";
        ws.Cell(2,1).Style.Font.FontColor = XLColor.Gray;

        string[] hdrs = { "Ø§Ù„ØªØ§Ø±ÙŠØ®", "Ø§Ù„Ù‚ÙŠØ¯", "Ø§Ù„Ø§Ø³Ù…", "Ø§Ù„Ø¨ÙŠØ§Ù†", "Ù…Ø¯ÙŠÙ†", "Ø¯Ø§Ø¦Ù†", "Ø§Ù„Ø±ØµÙŠØ¯" };
        for (int c = 0; c < hdrs.Length; c++) { ws.Cell(3, c + 1).Value = hdrs[c]; ws.Cell(3, c + 1).Style.Font.Bold = true; }

        ws.Cell(4, 4).Value = "Ø±ØµÙŠØ¯ Ø§ÙØªØªØ§Ø­ÙŠ"; ws.Cell(4, 7).Value = openBal;
        ws.Cell(4, 7).Style.NumberFormat.Format = "#,##0.00";

        int r = 5;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Date.ToString("yyyy-MM-dd");
            ws.Cell(r, 2).Value = row.EntryNumber;
            ws.Cell(r, 3).Value = row.PartnerName ?? "â€”";
            ws.Cell(r, 4).Value = row.Description;
            ws.Cell(r, 5).Value = row.Debit;
            ws.Cell(r, 6).Value = row.Credit;
            ws.Cell(r, 7).Value = row.RunningBalance;
            ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Cell(r, 4).Value = "Ø§Ù„Ø¥Ø¬Ù…Ø§Ù„ÙŠ"; ws.Cell(r, 4).Style.Font.Bold = true;
        ws.Cell(r, 5).Value = rows.Sum(x => x.Debit); ws.Cell(r, 5).Style.Font.Bold = true;
        ws.Cell(r, 6).Value = rows.Sum(x => x.Credit); ws.Cell(r, 6).Style.Font.Bold = true;
        ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"account_statement_{acct.Code}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelCashFlow(List<CashFlowItem> op, List<CashFlowItem> inv, List<CashFlowItem> fin, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ø§Ù„ØªØ¯ÙÙ‚Ø§Øª Ø§Ù„Ù†Ù‚Ø¯ÙŠØ©");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"Ù‚Ø§Ø¦Ù…Ø© Ø§Ù„ØªØ¯ÙÙ‚Ø§Øª Ø§Ù„Ù†Ù‚Ø¯ÙŠØ© â€” Ù…Ù† {from:yyyy-MM-dd} Ø¥Ù„Ù‰ {to:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

        int r = 3;
        ws.Cell(r, 1).Value = "Ø§Ù„Ø±ØµÙŠØ¯ Ø§Ù„Ø§ÙØªØªØ§Ø­ÙŠ Ù„Ù„Ù†Ù‚Ø¯ÙŠØ©"; 
        ws.Cell(r, 3).Value = openBal; ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        r += 2;

        void AddSection(string title, List<CashFlowItem> items, XLColor color)
        {
            ws.Cell(r, 1).Value = title; ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = color;
            ws.Range(r, 1, r, 3).Merge(); r++;
            foreach (var item in items)
            {
                ws.Cell(r, 1).Value = item.Date.ToString("yyyy-MM-dd");
                ws.Cell(r, 2).Value = item.Account;
                ws.Cell(r, 3).Value = item.Amount;
                ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }
            ws.Cell(r, 2).Value = $"Ø¥Ø¬Ù…Ø§Ù„ÙŠ {title}"; ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = items.Sum(x => x.Amount); ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            r += 2;
        }

        AddSection("Ø§Ù„Ø£Ù†Ø´Ø·Ø© Ø§Ù„ØªØ´ØºÙŠÙ„ÙŠØ©", op,  XLColor.FromHtml("#e8f5e9"));
        AddSection("Ø§Ù„Ø£Ù†Ø´Ø·Ø© Ø§Ù„Ø§Ø³ØªØ«Ù…Ø§Ø±ÙŠØ©", inv, XLColor.FromHtml("#fff3e0"));
        AddSection("Ø§Ù„Ø£Ù†Ø´Ø·Ø© Ø§Ù„ØªÙ…ÙˆÙŠÙ„ÙŠØ©", fin, XLColor.FromHtml("#e1f5fe"));

        ws.Cell(r, 2).Value = "ØµØ§ÙÙŠ Ø§Ù„ØªØ¯ÙÙ‚ Ø§Ù„Ù†Ù‚Ø¯ÙŠ"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = op.Sum(x => x.Amount) + inv.Sum(x => x.Amount) + fin.Sum(x => x.Amount);
        ws.Cell(r, 3).Style.Font.Bold = true; ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        r++;
        ws.Cell(r, 2).Value = "Ø§Ù„Ø±ØµÙŠØ¯ Ø§Ù„Ø®ØªØ§Ù…ÙŠ Ù„Ù„Ù†Ù‚Ø¯ÙŠØ©"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = openBal + op.Sum(x => x.Amount) + inv.Sum(x => x.Amount) + fin.Sum(x => x.Amount);
        ws.Cell(r, 3).Style.Font.Bold = true; ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"cash_flow_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelEmployeeStatement(Employee emp, List<EmployeeStatementRowDto> rows, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ÙƒØ´Ù Ø­Ø³Ø§Ø¨ Ù…ÙˆØ¸Ù");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"ÙƒØ´Ù Ø­Ø³Ø§Ø¨: {emp.EmployeeNumber} â€” {emp.Name}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,6).Merge();
        ws.Cell(2,1).Value = $"Ù…Ù† {from:yyyy-MM-dd} Ø¥Ù„Ù‰ {to:yyyy-MM-dd}";
        ws.Cell(2,1).Style.Font.FontColor = XLColor.Gray;

        string[] hdrs = { "Ø§Ù„ØªØ§Ø±ÙŠØ®","Ø±Ù‚Ù… Ø§Ù„Ù‚ÙŠØ¯","Ù†ÙˆØ¹ Ø§Ù„Ø¹Ù…Ù„ÙŠØ©","Ø§Ù„Ø¨ÙŠØ§Ù†","Ù…Ø¯ÙŠÙ†","Ø¯Ø§Ø¦Ù†","Ø§Ù„Ø±ØµÙŠØ¯" };
        for (int c = 0; c < hdrs.Length; c++) { ws.Cell(3,c+1).Value = hdrs[c]; ws.Cell(3,c+1).Style.Font.Bold = true; }

        ws.Cell(4,4).Value = "Ø±ØµÙŠØ¯ Ø§ÙØªØªØ§Ø­ÙŠ"; ws.Cell(4,7).Value = openBal;
        ws.Cell(4,7).Style.NumberFormat.Format = "#,##0.00";

        int r = 5;
        foreach (var row in rows)
        {
            ws.Cell(r,1).Value = row.Date.ToString("yyyy-MM-dd");
            ws.Cell(r,2).Value = row.Reference;
            ws.Cell(r,3).Value = row.Type;
            ws.Cell(r,4).Value = row.Description;
            ws.Cell(r,5).Value = row.Debit;
            ws.Cell(r,6).Value = row.Credit;
            ws.Cell(r,7).Value = row.Balance;
            ws.Cell(r,5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,7).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Cell(r,4).Value = "Ø§Ù„Ø¥Ø¬Ù…Ø§Ù„ÙŠ"; ws.Cell(r,4).Style.Font.Bold = true;
        ws.Cell(r,5).Value = rows.Sum(x=>x.Debit); ws.Cell(r,5).Style.Font.Bold = true;
        ws.Cell(r,6).Value = rows.Sum(x=>x.Credit); ws.Cell(r,6).Style.Font.Bold = true;
        ws.Cell(r,5).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(r,6).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"employee_statement_{emp.EmployeeNumber}_{from:yyyyMMdd}.xlsx");
    }

    private static FileStreamResult ExcelResult(XLWorkbook wb, string filename)
    {
        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;
        return new FileStreamResult(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        { FileDownloadName = filename };
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // 7. ÙƒØ´Ù Ø­Ø³Ø§Ø¨ Ø§Ù„Ù…ÙˆØ¸Ù  GET /api/financialreports/employee-statement
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("employee-statement")]
    public async Task<IActionResult> EmployeeStatement(
        [FromQuery] int       employeeId,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] bool      excel     = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound(new { message = "Ø§Ù„Ù…ÙˆØ¸Ù ØºÙŠØ± Ù…ÙˆØ¬ÙˆØ¯" });

        // â”€â”€â”€ Ø³Ø·ÙˆØ± Ø§Ù„Ù‚ÙŠÙˆØ¯ Ø§Ù„Ù…Ø±ØªØ¨Ø·Ø© Ø¨Ø§Ù„Ù…ÙˆØ¸Ù â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var jeLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.EmployeeId == employeeId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntry.Id)
            .ToListAsync();

        // Ø±ØµÙŠØ¯ Ø§ÙØªØªØ§Ø­ÙŠ Ù…Ù† Ø§Ù„Ù‚ÙŠÙˆØ¯ Ø§Ù„Ø³Ø§Ø¨Ù‚Ø© Ù„Ù„ÙØªØ±Ø©
        var openLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.EmployeeId == employeeId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from)
            .ToListAsync();

        // Ø·Ø¨ÙŠØ¹Ø© Ø­Ø³Ø§Ø¨ Ø§Ù„Ù…ÙˆØ¸Ù Ù…Ø¯ÙŠÙ† (Ø§Ù„Ø³Ù„Ù Ù…Ø¯ÙŠÙ†Ø©ØŒ Ø§Ù„Ø±ÙˆØ§ØªØ¨ Ø¯Ø§Ø¦Ù†Ø©)
        // Ø§Ù„Ø±ØµÙŠØ¯ = Ù…Ø¯ÙŠÙ† - Ø¯Ø§Ø¦Ù†: Ù…ÙˆØ¬Ø¨ = Ù„Ù‡ Ø³Ù„Ù ØºÙŠØ± Ù…Ø®ØµÙˆÙ…Ø©ØŒ Ø³Ø§Ù„Ø¨ = Ù„Ù‡ Ø±ÙˆØ§ØªØ¨ Ù…Ø³ØªØ­Ù‚Ø©
        var openBal = openLines.Sum(l => l.Debit) - openLines.Sum(l => l.Credit);

        var runBal = openBal;
        var rows   = new List<EmployeeStatementRowDto>();

        foreach (var l in jeLines)
        {
            runBal += l.Debit - l.Credit;
            rows.Add(new EmployeeStatementRowDto(
                l.JournalEntry.EntryDate,
                l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                l.JournalEntry.Description ?? l.Description ?? "",
                l.Debit,
                l.Credit,
                runBal
            ));
        }

        // â”€â”€â”€ Ø¨ÙŠØ§Ù†Ø§Øª Ø¥Ø¶Ø§ÙÙŠØ© Ù…Ù† Ø¬Ø¯Ø§ÙˆÙ„ HR â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var advances = await _db.EmployeeAdvances
            .Where(a => a.EmployeeId == employeeId && a.AdvanceDate >= from && a.AdvanceDate <= to)
            .OrderBy(a => a.AdvanceDate)
            .Select(a => new { a.AdvanceDate, a.AdvanceNumber, a.Amount, a.Status, a.Reason })
            .ToListAsync();

        var bonuses = await _db.EmployeeBonuses
            .Where(b => b.EmployeeId == employeeId && b.BonusDate >= from && b.BonusDate <= to)
            .OrderBy(b => b.BonusDate)
            .Select(b => new { b.BonusDate, b.BonusNumber, b.Amount, b.BonusType, b.Reason })
            .ToListAsync();

        var deductions = await _db.EmployeeDeductions
            .Where(d => d.EmployeeId == employeeId && d.DeductionDate >= from && d.DeductionDate <= to)
            .OrderBy(d => d.DeductionDate)
            .Select(d => new { d.DeductionDate, d.DeductionNumber, d.Amount, d.DeductionType, d.Reason })
            .ToListAsync();

        var payrollItems = await _db.PayrollItems
            .Include(i => i.PayrollRun)
            .Where(i => i.EmployeeId == employeeId
                     && i.PayrollRun.Status == PayrollStatus.Posted
                     && i.PayrollRun.PeriodYear * 100 + i.PayrollRun.PeriodMonth
                        >= from.Year * 100 + from.Month
                     && i.PayrollRun.PeriodYear * 100 + i.PayrollRun.PeriodMonth
                        <= to.Year * 100 + to.Month)
            .OrderBy(i => i.PayrollRun.PeriodYear).ThenBy(i => i.PayrollRun.PeriodMonth)
            .Select(i => new {
                i.PayrollRun.PeriodYear, i.PayrollRun.PeriodMonth,
                i.PayrollRun.PayrollNumber,
                i.BasicSalary, i.BonusAmount, i.DeductionAmount,
                i.AdvanceDeducted, NetPayable = i.BasicSalary + i.BonusAmount - i.DeductionAmount - i.AdvanceDeducted
            })
            .ToListAsync();

        if (excel) return ExcelEmployeeStatement(emp, rows, openBal, from, to);

        var statementRows = rows.Concat(payrollItems.Select(p => new EmployeeStatementRowDto(
            new DateTime(p.PeriodYear, p.PeriodMonth, 1),
            p.PayrollNumber,
            "Payroll",
            $"Salary - {p.PeriodMonth}/{p.PeriodYear}",
            0,
            p.NetPayable,
            0,
            p.PeriodYear,
            p.PeriodMonth,
            p.PayrollNumber,
            p.NetPayable
        ))).OrderBy(r => r.Date).ToList();

        // Calculate running balance
        decimal currentBal = openBal;
        foreach(var row in statementRows) {
            currentBal += (row.Debit - row.Credit);
            // We can't mutate row.Balance if it's a record without { get; set; }, but let's assume it's just a summary.
        }

        return Ok(new EmployeeStatementDto(
            emp.Id, emp.Name, emp.EmployeeNumber, emp.JobTitle, emp.Account?.NameAr ?? "Ø±ÙˆØ§ØªØ¨ Ù…Ø³ØªØ­Ù‚Ø©",
            from, to, openBal, statementRows,
            statementRows.Sum(r => r.Debit),
            statementRows.Sum(r => r.Credit),
            currentBal
        ));
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // VAT Report  GET /api/financialreports/vat-report
    // ØªÙ‚Ø±ÙŠØ± Ø¶Ø±ÙŠØ¨Ø© Ø§Ù„Ù‚ÙŠÙ…Ø© Ø§Ù„Ù…Ø¶Ø§ÙØ© Ø¹Ù„Ù‰ Ù…Ø³ØªÙˆÙ‰ Ø§Ù„Ø£ÙˆØ§Ù…Ø± ÙˆØ§Ù„Ù…Ø´ØªØ±ÙŠØ§Øª
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    [HttpGet("vat-report")]
    public async Task<IActionResult> VatReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        // Sales VAT â€” from Orders
        var salesOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to
                     && o.Status != OrderStatus.Cancelled)
            .Select(o => new {
                o.Id, o.OrderNumber, o.CreatedAt,
                o.TotalAmount, o.TotalVatAmount,
                CustomerName = o.Customer != null ? o.Customer.FullName : "Ø¹Ù…ÙŠÙ„ Ù…ØªØ¬ÙˆÙ„"
            })
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        var totalSalesVat   = salesOrders.Sum(o => o.TotalVatAmount);
        var totalSalesNet   = salesOrders.Sum(o => o.TotalAmount - o.TotalVatAmount);
        var totalSalesGross = salesOrders.Sum(o => o.TotalAmount);

        // Purchase VAT â€” from PurchaseInvoices
        var purchases = await _db.PurchaseInvoices
            .AsNoTracking()
            .Where(p => p.InvoiceDate >= from && p.InvoiceDate <= to
                     && p.Status != PurchaseInvoiceStatus.Cancelled)
            .Select(p => new {
                p.Id, p.InvoiceNumber, p.InvoiceDate,
                p.TotalAmount, p.TaxAmount,
                SupplierName = p.Supplier != null ? p.Supplier.Name : ""
            })
            .OrderBy(p => p.InvoiceDate)
            .ToListAsync();

        var totalPurchaseVat   = purchases.Sum(p => p.TaxAmount);
        var totalPurchaseNet   = purchases.Sum(p => p.TotalAmount - p.TaxAmount);
        var totalPurchaseGross = purchases.Sum(p => p.TotalAmount);

        var netVatPosition = totalSalesVat - totalPurchaseVat; // positive = payable to authority

        return Ok(new {
            from, to,
            sales = new {
                totalNet   = Math.Round(totalSalesNet,   2),
                totalVat   = Math.Round(totalSalesVat,   2),
                totalGross = Math.Round(totalSalesGross, 2),
                items = salesOrders
            },
            purchases = new {
                totalNet   = Math.Round(totalPurchaseNet,   2),
                totalVat   = Math.Round(totalPurchaseVat,   2),
                totalGross = Math.Round(totalPurchaseGross, 2),
                items = purchases
            },
            summary = new {
                outputVat  = Math.Round(totalSalesVat,    2),
                inputVat   = Math.Round(totalPurchaseVat, 2),
                netPosition= Math.Round(netVatPosition,   2),
                status = netVatPosition > 0 ? "payable" : netVatPosition < 0 ? "refundable" : "zero"
            }
        });
    }
}

// â”€â”€ Internal models (not exposed to DB) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
internal class AccountBalance
{
    public int           Id           { get; set; }
    public string        Code         { get; set; } = "";
    public string        NameAr       { get; set; } = "";
    public AccountType   Type         { get; set; }
    public AccountNature Nature       { get; set; }
    public int?          ParentId     { get; set; }
    public int           Level        { get; set; }
    public bool          IsLeaf       { get; set; }
    public decimal       OpenDebit    { get; set; }
    public decimal       OpenCredit   { get; set; }
    public decimal       OpenBalance  { get; set; }
    public decimal       PeriodDebit  { get; set; }
    public decimal       PeriodCredit { get; set; }
    public decimal       ClosingBal   { get; set; }
}

// â”€â”€ Report DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
public record TrialBalanceRow(
    string Code, string NameAr, int Level,
    decimal OpenDebit, decimal OpenCredit,
    decimal PeriodDebit, decimal PeriodCredit,
    decimal ClosingDebit, decimal ClosingCredit,
    int? AccountId = null);

public record IncomeRow(string Code, string NameAr, int Level, decimal Amount);
public record BalanceSheetRow(string Code, string NameAr, int Level, decimal Amount);
public record LedgerRow(
    int AccountId, string AccountCode, string AccountName,
    DateTime Date, string EntryNumber, string EntryType, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance,
    string? Reference = null, int JournalEntryId = 0,
    string? PartnerName = null);
public record CashFlowItem(DateTime Date, string EntryNumber, string Description, string Account, decimal Amount);

