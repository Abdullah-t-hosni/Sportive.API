using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.DTOs;

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
            .Where(a => !a.IsDeleted && a.IsActive)
            .OrderBy(a => a.Code)
            .ToListAsync();

        // كل القيود المرحّلة في الفترة
        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .ToListAsync();

        // القيود قبل الفترة (للرصيد الافتتاحي)
        var openingLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from)
            .ToListAsync();

        return accounts.Select(a =>
        {
            var periodDr = lines.Where(l => l.AccountId == a.Id).Sum(l => l.Debit);
            var periodCr = lines.Where(l => l.AccountId == a.Id).Sum(l => l.Credit);

            var openDr = openingLines.Where(l => l.AccountId == a.Id).Sum(l => l.Debit)
                       + (a.Nature == AccountNature.Debit  ? a.OpeningBalance : 0);
            var openCr = openingLines.Where(l => l.AccountId == a.Id).Sum(l => l.Credit)
                       + (a.Nature == AccountNature.Credit ? a.OpeningBalance : 0);

            var openBal = a.Nature == AccountNature.Debit
                ? openDr - openCr
                : openCr - openDr;

            var closingDr = openDr + periodDr;
            var closingCr = openCr + periodCr;
            var closingBal = a.Nature == AccountNature.Debit
                ? closingDr - closingCr
                : closingCr - closingDr;

            return new AccountBalance
            {
                Id          = a.Id,
                Code        = a.Code,
                NameAr      = a.NameAr,
                Type        = a.Type,
                Nature      = a.Nature,
                ParentId    = a.ParentId,
                Level       = a.Level,
                IsLeaf      = a.IsLeaf,
                OpenBalance = openBal,
                PeriodDebit = periodDr,
                PeriodCredit= periodCr,
                ClosingBal  = closingBal,
            };
        }).ToList();
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
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

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
            totalOpenDebit    = rows.Sum(r => r.OpenDebit),
            totalOpenCredit   = rows.Sum(r => r.OpenCredit),
            totalPeriodDebit  = rows.Sum(r => r.PeriodDebit),
            totalPeriodCredit = rows.Sum(r => r.PeriodCredit),
            totalClosingDebit = rows.Sum(r => r.ClosingDebit),
            totalClosingCredit= rows.Sum(r => r.ClosingCredit),
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
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        var balances = await GetBalances(from, to);

        // الإيرادات (طبيعتها دائن → الرصيد موجب = إيراد)
        var revenues = balances
            .Where(b => b.Type == AccountType.Revenue && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new IncomeRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // المصاريف (طبيعتها مدين → الرصيد موجب = مصروف)
        var expenses = balances
            .Where(b => b.Type == AccountType.Expense && b.IsLeaf && b.ClosingBal != 0)
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
        var from = new DateTime(2000, 1, 1);
        var to   = toDate ?? DateTime.UtcNow;

        var balances = await GetBalances(from, to);

        // الأصول — طبيعة مدين
        var assets = balances
            .Where(b => b.Type == AccountType.Asset && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // الالتزامات — طبيعة دائن
        var liabilities = balances
            .Where(b => b.Type == AccountType.Liability && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // حقوق الملكية
        var equity = balances
            .Where(b => b.Type == AccountType.Equity && b.IsLeaf && b.ClosingBal != 0)
            .OrderBy(b => b.Code)
            .Select(b => new BalanceSheetRow(b.Code, b.NameAr, b.Level, b.ClosingBal))
            .ToList();

        // صافي الربح للسنة يضاف لحقوق الملكية
        var incomeFrom = new DateTime(to.Year, 1, 1);
        var incomeBals = await GetBalances(incomeFrom, to);
        var netProfit  = incomeBals.Where(b => b.Type == AccountType.Revenue && b.IsLeaf).Sum(b => b.ClosingBal)
                       - incomeBals.Where(b => b.Type == AccountType.Expense && b.IsLeaf).Sum(b => b.ClosingBal);

        var totalAssets      = assets.Sum(a => a.Amount);
        var totalLiabilities = liabilities.Sum(l => l.Amount);
        var totalEquity      = equity.Sum(e => e.Amount) + netProfit;
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
        [FromQuery] int?      accountId = null,
        [FromQuery] DateTime? fromDate  = null,
        [FromQuery] DateTime? toDate    = null,
        [FromQuery] string?   search    = null,
        [FromQuery] bool      excel     = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        var q = _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to);

        if (accountId.HasValue)
            q = q.Where(l => l.AccountId == accountId.Value);

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
                         && !l.IsDeleted
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.EntryDate < from)
                .ToListAsync();

            var openDr = openLines.Sum(l => l.Debit)  + (acct.Nature == AccountNature.Debit  ? acct.OpeningBalance : 0);
            var openCr = openLines.Sum(l => l.Credit) + (acct.Nature == AccountNature.Credit ? acct.OpeningBalance : 0);
            openingMap[aId] = acct.Nature == AccountNature.Debit ? openDr - openCr : openCr - openDr;
        }

        // بناء السجلات مع الرصيد المتراكم
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
                line.JournalEntry.Description ?? line.Description ?? "",
                line.Debit, line.Credit, balanceMap[line.AccountId]
            ));
        }

        if (excel) return ExcelLedger(ledgerRows, openingMap, from, to);

        // Group by account
        var grouped = ledgerRows
            .GroupBy(r => new { r.AccountId, r.AccountCode, r.AccountName })
            .Select(g => new {
                g.Key.AccountId, g.Key.AccountCode, g.Key.AccountName,
                openingBalance = openingMap.GetValueOrDefault(g.Key.AccountId, 0),
                rows = g.ToList(),
                closingBalance = g.LastOrDefault()?.RunningBalance ?? 0
            }).ToList();

        return Ok(new { from, to, accounts = grouped });
    }

    // ══════════════════════════════════════════════════════
    // 5. كشف حساب  GET /api/financialreports/account-statement
    // ══════════════════════════════════════════════════════
    [HttpGet("account-statement")]
    public async Task<IActionResult> AccountStatement(
        [FromQuery] int       accountId,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate   = null,
        [FromQuery] bool      excel    = false)
    {
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        var acct = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted);
        if (acct == null) return NotFound(new { message = "الحساب غير موجود" });

        // الرصيد الافتتاحي
        var openLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId && !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate < from)
            .ToListAsync();

        var openDr  = openLines.Sum(l => l.Debit)  + (acct.Nature == AccountNature.Debit  ? acct.OpeningBalance : 0);
        var openCr  = openLines.Sum(l => l.Credit) + (acct.Nature == AccountNature.Credit ? acct.OpeningBalance : 0);
        var openBal = acct.Nature == AccountNature.Debit ? openDr - openCr : openCr - openDr;

        // حركات الفترة
        var periodLines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == accountId && !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to)
            .OrderBy(l => l.JournalEntry.EntryDate).ThenBy(l => l.JournalEntry.Id)
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
                l.JournalEntry.Description ?? l.Description ?? "",
                l.Debit, l.Credit, runBal
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
        var from = fromDate ?? new DateTime(DateTime.UtcNow.Year, 1, 1);
        var to   = toDate   ?? DateTime.UtcNow;

        // حسابات النقدية (كل ما يبدأ بـ 11)
        var cashAccounts = await _db.Accounts
            .Where(a => !a.IsDeleted && a.IsLeaf && a.Code.StartsWith("11"))
            .ToListAsync();

        var cashIds = cashAccounts.Select(a => a.Id).ToHashSet();

        var lines = await _db.JournalLines
            .Include(l => l.JournalEntry)
            .Include(l => l.Account)
            .Where(l => !l.IsDeleted
                     && l.JournalEntry.Status == JournalEntryStatus.Posted
                     && l.JournalEntry.EntryDate >= from
                     && l.JournalEntry.EntryDate <= to
                     && cashIds.Contains(l.AccountId))
            .OrderBy(l => l.JournalEntry.EntryDate)
            .ToListAsync();

        // تصنيف: تشغيلي (المبيعات/المشتريات) / استثماري / تمويلي
        decimal operating  = 0;
        decimal investing  = 0;
        decimal financing  = 0;
        var operatingItems = new List<CashFlowItem>();
        var investingItems = new List<CashFlowItem>();
        var financingItems = new List<CashFlowItem>();

        foreach (var line in lines)
        {
            var entryType = line.JournalEntry.Type;
            var amount    = line.Debit - line.Credit; // + = تدفق داخل، - = تدفق خارج

            var item = new CashFlowItem(
                line.JournalEntry.EntryDate,
                line.JournalEntry.EntryNumber,
                line.JournalEntry.Description ?? "",
                line.Account.NameAr,
                amount
            );

            if (entryType == JournalEntryType.SalesInvoice || entryType == JournalEntryType.SalesReturn ||
                entryType == JournalEntryType.PurchaseInvoice || entryType == JournalEntryType.PurchaseReturn ||
                entryType == JournalEntryType.ReceiptVoucher || entryType == JournalEntryType.PaymentVoucher ||
                entryType == JournalEntryType.Manual)
            {
                operating += amount;
                operatingItems.Add(item);
            }
            // يمكن تخصيص الاستثماري والتمويلي لاحقاً حسب طبيعة الحسابات
        }

        // الرصيد الافتتاحي للنقدية
        var openCash = 0m;
        foreach (var ca in cashAccounts)
        {
            var ol = await _db.JournalLines
                .Include(l => l.JournalEntry)
                .Where(l => l.AccountId == ca.Id && !l.IsDeleted
                         && l.JournalEntry.Status == JournalEntryStatus.Posted
                         && l.JournalEntry.EntryDate < from)
                .ToListAsync();
            openCash += ol.Sum(l => l.Debit) - ol.Sum(l => l.Credit) + ca.OpeningBalance;
        }

        var netCashFlow    = operating + investing + financing;
        var closingBalance = openCash + netCashFlow;

        return Ok(new {
            from, to,
            openingCashBalance = openCash,
            operatingActivities = new { items = operatingItems, total = operating },
            investingActivities = new { items = investingItems, total = investing },
            financingActivities = new { items = financingItems, total = financing },
            netCashFlow,
            closingCashBalance = closingBalance,
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

    private static FileStreamResult ExcelResult(XLWorkbook wb, string filename)
    {
        var stream = new MemoryStream();
        wb.SaveAs(stream); stream.Position = 0;
        return new FileStreamResult(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        { FileDownloadName = filename };
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
    DateTime Date, string EntryNumber, string Description,
    decimal Debit, decimal Credit, decimal RunningBalance);
public record CashFlowItem(DateTime Date, string EntryNumber, string Description, string Account, decimal Amount);
