using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.DTOs;
using Sportive.API.Utils;
using Sportive.API.Services;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Accountant")]
public class FinancialReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public FinancialReportsController(AppDbContext db) => _db = db;

    // ══════════════════════════════════════════════════════
    // SHARED: حساب أرصدة كل الحسابات في فترة زمنية
    // ══════════════════════════════════════════════════════
    private async Task<List<AccountBalance>> GetBalances(DateTime from, DateTime to)
    {
        var accounts = await _db.Accounts
            .Where(a => a.IsActive)
            .OrderBy(a => a.Code)
            .ToListAsync();

        // 1. Get transaction movements for the period
        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .ToListAsync();

        // 2. Get opening movements (before the period)
        var openingLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from)
            .ToListAsync();

        // 3. Create dictionaries for fast lookup of direct postings
        var periodDrMap = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit));
        var periodCrMap = lines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Credit));
        var openDrMap = openingLines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Debit));
        var openCrMap = openingLines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.Credit));

        // 4. Initialize balance objects with direct postings only
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
            // Opening balance from initial setup
            OpenDebit    = (a.Nature == AccountNature.Debit  ? a.OpeningBalance : 0) + openDrMap.GetValueOrDefault(a.Id, 0),
            OpenCredit   = (a.Nature == AccountNature.Credit ? a.OpeningBalance : 0) + openCrMap.GetValueOrDefault(a.Id, 0),
            PeriodDebit  = periodDrMap.GetValueOrDefault(a.Id, 0),
            PeriodCredit = periodCrMap.GetValueOrDefault(a.Id, 0)
        }).ToList();

        // 5. 🔥 HIERARCHICAL ROLL-UP 🔥
        // Process accounts from bottom-up (longest codes to shortest) to aggregate children into parents
        var balancesByCode = balanceList.OrderByDescending(b => b.Code.Length).ToList();
        foreach (var b in balancesByCode)
        {
            if (b.ParentId.HasValue)
            {
                var parent = balanceList.FirstOrDefault(p => p.Id == b.ParentId.Value);
                if (parent != null)
                {
                    parent.OpenDebit    += b.OpenDebit;
                    parent.OpenCredit   += b.OpenCredit;
                    parent.PeriodDebit  += b.PeriodDebit;
                    parent.PeriodCredit += b.PeriodCredit;
                }
            }
        }

        // 6. Calculate net balances for each account after roll-up
        foreach (var b in balanceList)
        {
            // Opening Balance Calculation
            b.OpenBalance = b.Nature == AccountNature.Debit ? b.OpenDebit - b.OpenCredit : b.OpenCredit - b.OpenDebit;
            
            // Closing Balance Calculation: (Opening Dr + Period Dr) - (Opening Cr + Period Cr)
            var closingDr = b.OpenDebit + b.PeriodDebit;
            var closingCr = b.OpenCredit + b.PeriodCredit;
            b.ClosingBal = b.Nature == AccountNature.Debit ? closingDr - closingCr : closingCr - closingDr;
        }

        return balanceList;
    }

    // helper: مجموع نوع حسابات معين
    private static decimal SumType(List<AccountBalance> balances, AccountType type)
        => balances.Where(b => b.Type == type && b.IsLeaf).Sum(b => b.ClosingBal);

    // ══════════════════════════════════════════════════════
    // 1. ميزان المراجعة  GET /api/financialreports/trial-balance
    // ══════════════════════════════════════════════════════
    [HttpGet("trial-balance")]
    public async Task<IActionResult> TrialBalance(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to);
        var rows = balances
            .OrderBy(b => b.Code)
            .Select(b => new TrialBalanceRow(
                b.Code, b.NameAr, b.Level,
                b.OpenBalance > 0 ? b.OpenBalance : 0,
                b.OpenBalance < 0 ? -b.OpenBalance : 0,
                b.PeriodDebit, b.PeriodCredit,
                b.ClosingBal > 0 ? b.ClosingBal : 0,
                b.ClosingBal < 0 ? -b.ClosingBal : 0
            )).ToList();

        if (excel) return ExcelTrialBalance(rows, from, to);

        return Ok(new {
            from, to,
            rows,
            totalOpenDebit    = rows.Where(r => r.Level == 1).Sum(r => r.OpenDebit),
            totalOpenCredit   = rows.Where(r => r.Level == 1).Sum(r => r.OpenCredit),
            totalPeriodDebit  = rows.Where(r => r.Level == 1).Sum(r => r.PeriodDebit),
            totalPeriodCredit = rows.Where(r => r.Level == 1).Sum(r => r.PeriodCredit),
            totalClosingDebit = rows.Where(r => r.Level == 1).Sum(r => r.ClosingDebit),
            totalClosingCredit= rows.Where(r => r.Level == 1).Sum(r => r.ClosingCredit),
        });
    }

    // ══════════════════════════════════════════════════════
    // 2. قائمة الدخل   GET /api/financialreports/income-statement
    // ══════════════════════════════════════════════════════
    [HttpGet("income-statement")]
    public async Task<IActionResult> IncomeStatement(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to);

        // الإيرادات (طبيعتها دائن → الرصيد موجب = إيراد)
        // ✅ تصحيح: ضم أي حساب يبدأ بكود 4 للإيرادات (مثل إيراد التوصيل) حتى لو كان مصنفاً خطأ في الداتابيز
        var revenues = balances
            .Where(b => (b.Type == AccountType.Revenue || b.Code.StartsWith("4")) && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new IncomeRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // المصاريف (طبيعتها مدين → الرصيد موجب = مصروف)
        // ✅ تصحيح: استثناء أي حساب يبدأ بكود 4 من المصاريف
        var expenses = balances
            .Where(b => b.Type == AccountType.Expense && !b.Code.StartsWith("4") && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new IncomeRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        var totalRevenues = revenues.Sum(r => r.Amount);
        var totalExpenses = expenses.Sum(r => r.Amount);
        var netProfit     = totalRevenues - totalExpenses;

        if (excel) return ExcelIncomeStatement(revenues, expenses, totalRevenues, totalExpenses, netProfit, from, to);

        return Ok(new {
            from, to,
            revenues,
            expenses,
            totalRevenues,
            totalExpenses,
            netProfit,
            isProfit = netProfit >= 0
        });
    }

    // ══════════════════════════════════════════════════════
    // 3. الميزانية العمومية  GET /api/financialreports/balance-sheet
    // ══════════════════════════════════════════════════════
    [HttpGet("balance-sheet")]
    public async Task<IActionResult> BalanceSheet(
        [FromQuery] DateTime? toDate = null,
        [FromQuery] bool      excel  = false)
    {
        var from = new DateTime(2000, 1, 1).Date;
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var balances = await GetBalances(from, to);

        // الأصول — طبيعة مدين (closingBal = Dr - Cr)
        var assets = balances
            .Where(b => b.Type == AccountType.Asset && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // الالتزامات — طبيعة دائن (closingBal = Cr - Dr)
        var liabilities = balances
            .Where(b => b.Type == AccountType.Liability && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // حقوق الملكية — طبيعة دائن
        var equity = balances
            .Where(b => b.Type == AccountType.Equity && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // صافي الربح للسنة يضاف لحقوق الملكية ونظهره في القائمة للشفافية
        var incomeFrom = new DateTime(to.Year, 1, 1);
        var incomeBals = await GetBalances(incomeFrom, to);
        var netProfit  = incomeBals.Where(b => b.Type == AccountType.Revenue && b.IsLeaf).Sum(b => b.ClosingBal)
                       - incomeBals.Where(b => b.Type == AccountType.Expense && b.IsLeaf).Sum(b => b.ClosingBal);

        if (netProfit != 0)
        {
            equity.Add(new BalanceSheetRow("N/P", "صافي ربح / (خسارة) العام", 1, netProfit));
        }

        var totalAssets      = assets.Sum(a => a.Amount);
        var totalLiabilities = liabilities.Sum(l => l.Amount);
        var totalEquity      = equity.Sum(e => e.Amount);
        var totalLiabEquity  = totalLiabilities + totalEquity;

        if (excel) return ExcelBalanceSheet(assets, liabilities, equity, netProfit,
            totalAssets, totalLiabilities, totalEquity, to);

        return Ok(new {
            to, assets, liabilities, equity, netProfit,
            totalAssets, totalLiabilities, totalEquity, totalLiabEquity,
            isBalanced = Math.Round(totalAssets, 2) == Math.Round(totalLiabEquity, 2)
        });
    }

    // ══════════════════════════════════════════════════════
    // 4. دفتر الأستاذ  GET /api/financialreports/ledger
    // ══════════════════════════════════════════════════════
    [HttpGet("ledger")]
    public async Task<IActionResult> Ledger(
        [FromQuery] int?      accountId  = null,
        [FromQuery] int?      customerId = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var q = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to);

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

        var lines = await q.OrderBy(l => l.JournalEntry.EntryDate)
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
                line.JournalEntry.Reference, line.JournalEntry.Id
            ));
        }

        if (excel) return ExcelLedger(ledgerRows, openingMap, from, to);

        // تجميع حسب الحساب — الحسابات مرتبة بالكود، والصفوف داخل كل حساب مرتبة بالتاريخ ثم رقم القيد
        // تحسين: تجميع العملاء والموردين تحت حسابات رقابة إذا لم يكن هناك فلترة محددة
        var grouped = ledgerRows
            .GroupBy(r => {
                if (r.AccountCode.StartsWith("1103") && !customerId.HasValue)
                    return new { Id = 0, Code = "1103", Name = "إجمالي العملاء" };
                if (r.AccountCode.StartsWith("2101") && !supplierId.HasValue)
                    return new { Id = 0, Code = "2101", Name = "إجمالي الموردين" };
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
                    rows = g.OrderBy(r => r.Date).ThenBy(r => r.EntryNumber).ToList(),
                    closingBalance = g.OrderBy(r => r.Date).ThenBy(r => r.EntryNumber).LastOrDefault()?.RunningBalance ?? 0
                };
            }).ToList();
        return Ok(new { from, to, accounts = grouped });
    }

    // ══════════════════════════════════════════════════════
    // 5. تشخيص الصحة المحاسبية (Accounting Health Check)
    // ══════════════════════════════════════════════════════
    [HttpGet("health-check")]
    public async Task<IActionResult> AccountingHealthCheck()
    {
        // 1. البحث عن القيود غير المتوازنة
        var unbalancedEntries = await _db.JournalEntries
            .Include(e => e.Lines)
            .OrderByDescending(e => e.EntryDate)
            .Select(e => new {
                e.Id,
                e.EntryNumber,
                e.EntryDate,
                e.Description,
                e.Type,
                TotalDebit = e.Lines.Sum(l => l.Debit),
                TotalCredit = e.Lines.Sum(l => l.Credit),
                Difference = Math.Abs(e.Lines.Sum(l => l.Debit) - e.Lines.Sum(l => l.Credit))
            })
            .Where(e => e.Difference > 0.009m) // سماحية 1 قرش
            .Take(100)
            .ToListAsync();

        // 2. البحث عن حركات تشير لحسابات غير نشطة
        var inactiveAccountLines = await _db.JournalLines
            .Include(l => l.Account)
            .Include(l => l.JournalEntry)
            .Where(l => l.Account != null && !l.Account.IsActive)
            .Select(l => new {
                l.Id,
                l.JournalEntryId,
                EntryNumber = l.JournalEntry.EntryNumber,
                AccountCode = l.Account.Code,
                AccountName = l.Account.NameAr,
                l.Debit,
                l.Credit
            })
            .Take(100)
            .ToListAsync();

        // 3. البحث عن حركات بدون حسابات (Orphans)
        var orphanLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == 0 || l.Account == null)
            .Select(l => new {
                l.Id,
                l.JournalEntryId,
                EntryNumber = l.JournalEntry != null ? l.JournalEntry.EntryNumber : "N/A",
                l.Debit,
                l.Credit
            })
            .ToListAsync();

        return Ok(new {
            isHealthy = !unbalancedEntries.Any() && !inactiveAccountLines.Any() && !orphanLines.Any(),
            unbalancedEntries,
            inactiveAccountLines,
            orphanLines
        });
    }

    // ══════════════════════════════════════════════════════
    // 6. كشف حساب  GET /api/financialreports/account-statement
    // ══════════════════════════════════════════════════════
    [HttpGet("account-statement")]
    public async Task<IActionResult> AccountStatement(
        [FromQuery] int       accountId,
        [FromQuery] int?      customerId = null,
        [FromQuery] int?      supplierId = null,
        [FromQuery] DateTime? fromDate   = null,
        [FromQuery] DateTime? toDate     = null,
        [FromQuery] string?   search     = null,
        [FromQuery] bool      excel      = false)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId);
        if (acct == null) return NotFound(new { message = "الحساب غير موجود" });

        // الرصيد الافتتاحي — يجب الفلترة بالمورد/العميل أيضاً إذا وجدوا
        var openQ = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from);

        if (customerId.HasValue) openQ = openQ.Where(l => l.CustomerId == customerId);
        if (supplierId.HasValue) openQ = openQ.Where(l => l.SupplierId == supplierId);

        var openLines = await openQ.ToListAsync();

        var openDr  = openLines.Sum(l => l.Debit);
        var openCr  = openLines.Sum(l => l.Credit);
        
        // الأرصدة الافتتاحية من شجرة الحسابات (فقط إذا لم نكن نفلتر بعميل/مورد محدد، أو إذا أردنا تضمينها)
        // محاسبياً: إذا اخترنا عميلاً، الرصيد الافتتاحي هو فقط الحركات السابقة له.
        if (!customerId.HasValue && !supplierId.HasValue)
        {
            openDr += (acct.Nature == AccountNature.Debit  ? acct.OpeningBalance : 0);
            openCr += (acct.Nature == AccountNature.Credit ? acct.OpeningBalance : 0);
        }

        var openBal = acct.Nature == AccountNature.Debit ? openDr - openCr : openCr - openDr;

        // حركات الفترة
        var q = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to);

        if (customerId.HasValue) q = q.Where(l => l.CustomerId == customerId);
        if (supplierId.HasValue) q = q.Where(l => l.SupplierId == supplierId);

        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(l => (l.Description != null && l.Description.Contains(search))
                          || (l.JournalEntry.Description != null && l.JournalEntry.Description.Contains(search))
                          || (l.JournalEntry.Reference != null && l.JournalEntry.Reference.Contains(search)));
        }

        var periodLines = await q.OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntry.Id)
            .ToListAsync();

        var runBal = openBal;
        var rows = periodLines.Select(l => {
            if (acct.Nature == AccountNature.Debit)
                runBal += l.Debit - l.Credit;
            else
                runBal += l.Credit - l.Debit;

            return new LedgerRow(
                accountId, acct.Code, acct.NameAr,
                l.JournalEntry.EntryDate, l.JournalEntry.EntryNumber,
                l.JournalEntry.Type.ToString(),
                l.JournalEntry.Description ?? l.Description ?? "",
                l.Debit, l.Credit, runBal,
                l.JournalEntry.Reference, l.JournalEntry.Id
            );
        }).ToList();

        if (excel) return ExcelAccountStatement(acct, rows, openBal, from, to);

        return Ok(new {
            from, to,
            account = new { acct.Id, acct.Code, acct.NameAr, Nature = acct.Nature.ToString() },
            openingBalance  = openBal,
            rows,
            totalDebit      = rows.Sum(r => r.Debit),
            totalCredit     = rows.Sum(r => r.Credit),
            closingBalance  = rows.LastOrDefault()?.RunningBalance ?? openBal
        });
    }

    // ══════════════════════════════════════════════════════
    // 6. قائمة التدفقات النقدية  GET /api/financialreports/cash-flow
    // ══════════════════════════════════════════════════════
    [HttpGet("cash-flow")]
    public async Task<IActionResult> CashFlow(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
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

        // جلب كل الأسطر التي تخص حسابات النقدية في تلك الفترة
        var cashLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to
                     && cashIds.Contains(l.AccountId))
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

                // تصنيف آلي
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
            from, to,
            openingCashBalance = openCash,
            operatingActivities = new { items = opItems, total = operating },
            investingActivities = new { items = invItems, total = investing },
            financingActivities = new { items = finItems, total = financing },
            netCashFlow,
            closingCashBalance = actualClosingBalance // نستخدم الرصيد الفعلي لضمان الدقة
        });
    }

    // ══════════════════════════════════════════════════════
    // EXCEL EXPORTS
    // ══════════════════════════════════════════════════════
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
        var ws = wb.Worksheets.Add("قائمة الدخل");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"قائمة الدخل — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
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
            ws.Cell(r, 2).Value = "الإجمالي";
            ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = total;
            ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        }

        WriteSection("الإيرادات", revenues, totalRev, 2, XLColor.FromHtml("#e8f5e9"));
        int expStart = revenues.Count + 4;
        WriteSection("المصاريف", expenses, totalExp, expStart, XLColor.FromHtml("#fce4ec"));

        int netRow = expStart + expenses.Count + 3;
        ws.Cell(netRow, 2).Value = netProfit >= 0 ? "صافي الربح" : "صافي الخسارة";
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
        var ws = wb.Worksheets.Add("الميزانية العمومية");
        ws.RightToLeft = true;

        ws.Cell(1, 1).Value = $"الميزانية العمومية — في {to:yyyy-MM-dd}";
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
            ws.Cell(r, 2).Value = $"إجمالي {title}"; ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = total; ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            r += 2;
        }

        Section("الأصول",       assets,      totalAssets,      XLColor.FromHtml("#e3f2fd"));
        Section("الالتزامات",   liabilities, totalLiabilities, XLColor.FromHtml("#fce4ec"));
        Section("حقوق الملكية", equity,      totalEquity,      XLColor.FromHtml("#f3e5f5"));

        ws.Cell(r, 2).Value = "صافي الربح / الخسارة"; ws.Cell(r, 3).Value = netProfit;
        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00"; r++;
        ws.Cell(r, 2).Value = "إجمالي الالتزامات وحقوق الملكية"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = totalLiabilities + totalEquity; ws.Cell(r, 3).Style.Font.Bold = true;
        ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"balance_sheet_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelLedger(List<LedgerRow> rows, Dictionary<int, decimal> openMap, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("دفتر الأستاذ");
        ws.RightToLeft = true;

        string[] hdrs = { "الكود","اسم الحساب","التاريخ","القيد","البيان","مدين","دائن","الرصيد" };
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
            ws.Cell(r,5).Value = row.Description;
            ws.Cell(r,6).Value = row.Debit;
            ws.Cell(r,7).Value = row.Credit;
            ws.Cell(r,8).Value = row.RunningBalance;
            ws.Cell(r,6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,7).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,8).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"ledger_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelAccountStatement(Account acct, List<LedgerRow> rows, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف حساب");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"كشف حساب: {acct.Code} — {acct.NameAr}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,5).Merge();
        ws.Cell(2,1).Value = $"من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(2,1).Style.Font.FontColor = XLColor.Gray;

        string[] hdrs = { "التاريخ","القيد","البيان","مدين","دائن","الرصيد" };
        for (int c = 0; c < hdrs.Length; c++) { ws.Cell(3,c+1).Value = hdrs[c]; ws.Cell(3,c+1).Style.Font.Bold = true; }

        ws.Cell(4,3).Value = "رصيد افتتاحي"; ws.Cell(4,6).Value = openBal;
        ws.Cell(4,6).Style.NumberFormat.Format = "#,##0.00";

        int r = 5;
        foreach (var row in rows)
        {
            ws.Cell(r,1).Value = row.Date.ToString("yyyy-MM-dd");
            ws.Cell(r,2).Value = row.EntryNumber;
            ws.Cell(r,3).Value = row.Description;
            ws.Cell(r,4).Value = row.Debit;
            ws.Cell(r,5).Value = row.Credit;
            ws.Cell(r,6).Value = row.RunningBalance;
            ws.Cell(r,4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r,6).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        ws.Cell(r,3).Value = "الإجمالي"; ws.Cell(r,3).Style.Font.Bold = true;
        ws.Cell(r,4).Value = rows.Sum(x=>x.Debit); ws.Cell(r,4).Style.Font.Bold = true;
        ws.Cell(r,5).Value = rows.Sum(x=>x.Credit); ws.Cell(r,5).Style.Font.Bold = true;
        ws.Cell(r,4).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(r,5).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"account_statement_{acct.Code}_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelCashFlow(List<CashFlowItem> op, List<CashFlowItem> inv, List<CashFlowItem> fin, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("التدفقات النقدية");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"قائمة التدفقات النقدية — من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

        int r = 3;
        ws.Cell(r, 1).Value = "الرصيد الافتتاحي للنقدية"; 
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
            ws.Cell(r, 2).Value = $"إجمالي {title}"; ws.Cell(r, 2).Style.Font.Bold = true;
            ws.Cell(r, 3).Value = items.Sum(x => x.Amount); ws.Cell(r, 3).Style.Font.Bold = true;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            r += 2;
        }

        AddSection("الأنشطة التشغيلية", op,  XLColor.FromHtml("#e8f5e9"));
        AddSection("الأنشطة الاستثمارية", inv, XLColor.FromHtml("#fff3e0"));
        AddSection("الأنشطة التمويلية", fin, XLColor.FromHtml("#e1f5fe"));

        ws.Cell(r, 2).Value = "صافي التدفق النقدي"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = op.Sum(x => x.Amount) + inv.Sum(x => x.Amount) + fin.Sum(x => x.Amount);
        ws.Cell(r, 3).Style.Font.Bold = true; ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        r++;
        ws.Cell(r, 2).Value = "الرصيد الختامي للنقدية"; ws.Cell(r, 2).Style.Font.Bold = true;
        ws.Cell(r, 3).Value = openBal + op.Sum(x => x.Amount) + inv.Sum(x => x.Amount) + fin.Sum(x => x.Amount);
        ws.Cell(r, 3).Style.Font.Bold = true; ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();
        return ExcelResult(wb, $"cash_flow_{from:yyyyMMdd}.xlsx");
    }

    private IActionResult ExcelEmployeeStatement(Employee emp, List<EmployeeStatementRowDto> rows, decimal openBal, DateTime from, DateTime to)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("كشف حساب موظف");
        ws.RightToLeft = true;

        ws.Cell(1,1).Value = $"كشف حساب: {emp.EmployeeNumber} — {emp.Name}";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;
        ws.Range(1,1,1,6).Merge();
        ws.Cell(2,1).Value = $"من {from:yyyy-MM-dd} إلى {to:yyyy-MM-dd}";
        ws.Cell(2,1).Style.Font.FontColor = XLColor.Gray;

        string[] hdrs = { "التاريخ","رقم القيد","نوع العملية","البيان","مدين","دائن","الرصيد" };
        for (int c = 0; c < hdrs.Length; c++) { ws.Cell(3,c+1).Value = hdrs[c]; ws.Cell(3,c+1).Style.Font.Bold = true; }

        ws.Cell(4,4).Value = "رصيد افتتاحي"; ws.Cell(4,7).Value = openBal;
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
        ws.Cell(r,4).Value = "الإجمالي"; ws.Cell(r,4).Style.Font.Bold = true;
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

    // ══════════════════════════════════════════════════════
    // 7. كشف حساب الموظف  GET /api/financialreports/employee-statement
    // ══════════════════════════════════════════════════════
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
        if (emp == null) return NotFound(new { message = "الموظف غير موجود" });

        // ─── سطور القيود المرتبطة بالموظف ────────────────────
        var jeLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => l.EmployeeId == employeeId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntry.Id)
            .ToListAsync();

        // رصيد افتتاحي من القيود السابقة للفترة
        var openLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.EmployeeId == employeeId
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from)
            .ToListAsync();

        // طبيعة حساب الموظف مدين (السلف مدينة، الرواتب دائنة)
        // الرصيد = مدين - دائن: موجب = له سلف غير مخصومة، سالب = له رواتب مستحقة
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

        // ─── بيانات إضافية من جداول HR ───────────────────────
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

        return Ok(new EmployeeStatementDto(
            emp.Id, emp.Name, emp.EmployeeNumber, emp.JobTitle,
            from, to, openBal, rows,
            rows.Sum(r => r.Debit),
            rows.Sum(r => r.Credit),
            rows.LastOrDefault()?.Balance ?? openBal
        ) with { });
    }

    // ══════════════════════════════════════════════════════
    // VAT Report  GET /api/financialreports/vat-report
    // تقرير ضريبة القيمة المضافة على مستوى الأوامر والمشتريات
    // ══════════════════════════════════════════════════════
    [HttpGet("vat-report")]
    public async Task<IActionResult> VatReport(
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null)
    {
        var from = fromDate?.Date ?? new DateTime(TimeHelper.GetEgyptTime().Year, 1, 1);
        var to   = toDate?.Date.AddDays(1).AddTicks(-1) ?? TimeHelper.GetEgyptTime();

        // Sales VAT — from Orders
        var salesOrders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to
                     && o.Status != OrderStatus.Cancelled)
            .Select(o => new {
                o.Id, o.OrderNumber, o.CreatedAt,
                o.TotalAmount, o.TotalVatAmount,
                CustomerName = o.Customer != null ? o.Customer.FullName : "عميل متجول"
            })
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        var totalSalesVat   = salesOrders.Sum(o => o.TotalVatAmount);
        var totalSalesNet   = salesOrders.Sum(o => o.TotalAmount - o.TotalVatAmount);
        var totalSalesGross = salesOrders.Sum(o => o.TotalAmount);

        // Purchase VAT — from PurchaseInvoices
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

// ── Internal models (not exposed to DB) ──────────────────
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

// ── Report DTOs ──────────────────────────────────────────
public record TrialBalanceRow(
    string Code, string NameAr, int Level,
    decimal OpenDebit, decimal OpenCredit,
    decimal PeriodDebit, decimal PeriodCredit,
    decimal ClosingDebit, decimal ClosingCredit);

public record IncomeRow(string Code, string NameAr, int Level, decimal Amount);
public record BalanceSheetRow(string Code, string NameAr, int Level, decimal Amount);
public record LedgerRow(
    int AccountId, string AccountCode, string AccountName,
    DateTime Date, string EntryNumber, string EntryType, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance,
    string? Reference = null, int JournalEntryId = 0);
public record CashFlowItem(DateTime Date, string EntryNumber, string Description, string Account, decimal Amount);
