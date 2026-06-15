using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Attributes;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;
using Sportive.API.Extensions;

namespace Sportive.API.Controllers;

/// <summary>
/// Dedicated server-side POS daily report endpoint.
/// Computes all KPIs directly from DB — no pagination, no client-side math.
/// This eliminates the "numbers change on every refresh" problem.
/// </summary>
[ApiController]
[Route("api/pos-report")]
[RequirePermission(ModuleKeys.Pos)]
public class POSReportController : ControllerBase
{
    private readonly AppDbContext _db;

    public POSReportController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/pos-report/summary?date=2024-06-01&stationId=DEFAULT
    /// Returns a fully computed POS daily summary — deterministic, no pagination gaps.
    /// Business day: 02:00 AM → next day 02:00 AM (Egypt time)
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetDailySummary(
        [FromQuery] string date,
        [FromQuery] string? stationId = null,
        [FromQuery] int? branchId = null)
    {
        if (!DateTime.TryParse(date, out var parsedDate))
            return BadRequest(new { message = "Invalid date format. Use yyyy-MM-dd." });

        int? resolvedBranchId = branchId;
        bool canViewAll = await User.HasViewAllBranchesAsync(HttpContext);
        if (!canViewAll)
        {
            resolvedBranchId = User.GetBranchId();
        }

        // Business day window: starts at 02:00 AM on 'date', ends at 02:00 AM next day
        var from = parsedDate.Date.AddHours(TimeHelper.GetBusinessDayEndHour());
        var to   = parsedDate.Date.AddDays(1).AddHours(TimeHelper.GetBusinessDayEndHour()).AddTicks(-1);

        // ── 1. Load account mappings ────────────────────────────────────────
        var mappings = await _db.AccountSystemMappings
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Key.ToLower(), m => m.AccountId);

        mappings.TryGetValue(MappingKeys.PosCash.ToLower(),     out var posCashId);
        mappings.TryGetValue(MappingKeys.PosBank.ToLower(),     out var posBankId);
        mappings.TryGetValue(MappingKeys.PosVodafone.ToLower(), out var posVodaId);
        mappings.TryGetValue(MappingKeys.PosInstaPay.ToLower(), out var posInstaId);
        mappings.TryGetValue(MappingKeys.Sales.ToLower(),        out var salesId);
        mappings.TryGetValue(MappingKeys.SalesDiscount.ToLower(), out var discountId);
        mappings.TryGetValue(MappingKeys.SalesReturn.ToLower(),   out var returnId);
        mappings.TryGetValue(MappingKeys.Cash.ToLower(),          out var mainCashId);
        mappings.TryGetValue(MappingKeys.Customer.ToLower(),      out var customerId);

        // Effective drawer = POS cash account (fallback to main cash)
        var effectiveDrawerId = posCashId != 0 ? posCashId : mainCashId;

        var posAccountIds = new HashSet<int>(
            new[] { effectiveDrawerId, posBankId, posVodaId, posInstaId }
                .Where(x => x != 0)
                .Select(x => x!.Value));

        // ── 2. Load ALL POS orders for the business day (no pageSize limit) ─
        var ordersQuery = _db.Orders
            .AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.Source == OrderSource.POS
                     && o.CreatedAt >= from
                     && o.CreatedAt <= to
                     && o.Status != OrderStatus.Cancelled);

        if (resolvedBranchId.HasValue)
        {
            ordersQuery = ordersQuery.Where(o => o.BranchId == resolvedBranchId.Value);
        }

        var orders = await ordersQuery
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();

        // ── 3. Load ALL journal entries for the business day ─────────────────
        var journalQuery = _db.JournalEntries
            .AsNoTracking()
            .Include(j => j.Lines).ThenInclude(l => l.Account)
            .Where(j => j.EntryDate >= from
                     && j.EntryDate <= to
                     && j.Status == JournalEntryStatus.Posted);

        if (resolvedBranchId.HasValue)
        {
            journalQuery = journalQuery.Where(j => j.Lines.Any(l => l.BranchId == resolvedBranchId.Value));
        }

        var journalEntries = await journalQuery
            .OrderBy(j => j.EntryDate).ThenBy(j => j.Id)
            .ToListAsync();

        // Filter: only entries that touch a POS account OR have POS cost center
        var posEntries = journalEntries.Where(j =>
            j.CostCenter == OrderSource.POS
            || (j.Reference != null && j.Reference.StartsWith("POS-"))
            || j.Lines.Any(l => posAccountIds.Contains(l.AccountId))
        ).ToList();

        // ── 4. Compute KPIs from Orders ───────────────────────────────────────
        decimal grossSales    = 0;
        decimal cashSales     = 0;
        decimal cardSales     = 0;
        decimal vodafoneSales = 0;
        decimal instapaySales = 0;
        decimal walletSales   = 0;
        decimal creditSales   = 0;
        decimal totalDiscounts = 0;

        foreach (var o in orders)
        {
            var oTotal = o.TotalAmount;
            grossSales += oTotal + o.DiscountAmount + o.TemporalDiscount;
            totalDiscounts += o.DiscountAmount + o.TemporalDiscount;

            var pm = o.PaymentMethod.ToString().ToLower();
            decimal paidAmt = o.PaymentStatus == PaymentStatus.Paid ? oTotal : o.PaidAmount;

            if (o.Payments.Any())
            {
                decimal paidThroughMethods = 0;
                foreach (var p in o.Payments)
                {
                    var m = p.Method.ToString().ToLower();
                    var amt = p.Amount;
                    if (m.Contains("customerbalance") || m == "9")
                    {
                        walletSales += amt;
                        paidThroughMethods += amt;
                    }
                    else if (m.Contains("cash") || m == "1")
                    {
                        cashSales += amt;
                        paidThroughMethods += amt;
                    }
                    else if (m.Contains("bank") || m.Contains("card") || m.Contains("visa") || m == "2")
                    {
                        cardSales += amt;
                        paidThroughMethods += amt;
                    }
                    else if (m.Contains("vodafone") || m == "3")
                    {
                        vodafoneSales += amt;
                        paidThroughMethods += amt;
                    }
                    else if (m.Contains("instapay") || m == "4")
                    {
                        instapaySales += amt;
                        paidThroughMethods += amt;
                    }
                    else if (m.Contains("credit") || m == "5")
                    {
                        // Credit is not cash/card, so it goes to creditSales remainder
                    }
                    else
                    {
                        cashSales += amt;
                        paidThroughMethods += amt;
                    }
                }
                decimal orderDebt = Math.Max(0, oTotal - paidThroughMethods);
                creditSales += orderDebt;
            }
            else
            {
                bool isCreditPm = pm.Contains("credit") || pm == "5" || pm.Contains("ajel") || pm.Contains("debt") || pm.Contains("آجل") || pm.Contains("مديونية");
                decimal paidThroughMethods = 0;
                if (!isCreditPm)
                {
                    paidThroughMethods = paidAmt;
                }

                decimal orderDebt = Math.Max(0, oTotal - paidThroughMethods);
                creditSales += orderDebt;

                if (pm.Contains("customerbalance") || pm == "9") { walletSales += paidAmt; }
                else if (pm.Contains("cash") || pm == "1") { cashSales += paidAmt; }
                else if (pm.Contains("bank") || pm.Contains("card") || pm.Contains("visa") || pm == "2") { cardSales += paidAmt; }
                else if (pm.Contains("vodafone") || pm == "3") { vodafoneSales += paidAmt; }
                else if (pm.Contains("instapay") || pm == "4") { instapaySales += paidAmt; }
                else if (isCreditPm) { /* Fully credit, handled by orderDebt */ }
                else { cashSales += paidAmt; }
            }
        }

        // ── 5. Compute Returns & Expenses from Journal Entries ────────────────
        decimal totalReturns  = 0;
        decimal expenses      = 0;
        decimal safeDrops     = 0;
        decimal cashReceipts  = 0; // debt collections
        decimal cashReturns   = 0;

        foreach (var j in posEntries)
        {
            var type = j.Type.ToString();
            var isReturn = type == "SalesReturn"
                        || (j.Reference != null && (j.Reference.Contains("RTN") || j.Reference.Contains("PRT")));
            var isExpense = type == "PaymentVoucher";
            var isReceipt = type == "ReceiptVoucher";
            var isManual  = type == "Manual";

            foreach (var l in j.Lines)
            {
                var aid = l.AccountId;
                var debit  = l.Debit;
                var credit = l.Credit;

                // Returns: credit to POS cash = cash refund
                if (isReturn && aid == returnId && debit > 0)
                    totalReturns += debit;

                // Expenses: debit from POS cash
                if (isExpense && aid == effectiveDrawerId && credit > 0)
                {
                    var debitedLine = j.Lines.FirstOrDefault(line => line.Debit > 0);
                    var isTransferToSafeOrBank = false;
                    if (debitedLine != null && debitedLine.Account != null)
                    {
                        var destAccountId = debitedLine.AccountId;
                        var destCode = debitedLine.Account.Code ?? "";
                        isTransferToSafeOrBank = destAccountId == mainCashId ||
                                                 destAccountId == posCashId ||
                                                 destAccountId == posBankId ||
                                                 destAccountId == posVodaId ||
                                                 destAccountId == posInstaId ||
                                                 destCode.StartsWith("1101") || // Cashier/Safes
                                                 destCode.StartsWith("1102") || // Banks
                                                 destCode.StartsWith("1103") || // POS drawers
                                                 destCode.StartsWith("1105") || // Vodafone cash
                                                 destCode.StartsWith("1107") || // Instapay
                                                 destCode.StartsWith("111");
                    }

                    if (isTransferToSafeOrBank)
                    {
                        safeDrops += credit;
                    }
                    else
                    {
                        expenses += credit;
                    }
                }

                // Safe drops: manual debit from POS cash → main safe (excluding shift closure entries)
                if (isManual && aid == effectiveDrawerId && credit > 0)
                {
                    if (string.IsNullOrEmpty(j.Reference) || !j.Reference.StartsWith("SHIFT-CLOSE-", StringComparison.OrdinalIgnoreCase))
                    {
                        safeDrops += credit;
                    }
                }

                // Cash returns: credit to POS cash drawer from a return entry
                if (isReturn && aid == effectiveDrawerId && credit > 0)
                    cashReturns += credit;

                // Debt collections: receipt voucher cash received
                if (isReceipt && aid == effectiveDrawerId && debit > 0)
                    cashReceipts += debit;
            }
        }

        // ── 6. Net Sales & Expected Cash ──────────────────────────────────────
        var netSales     = grossSales - totalDiscounts - totalReturns;
        var expectedCash = cashSales + cashReceipts - expenses - safeDrops - cashReturns;

        // ── 7. Build drawer movements list (for shift closure UI) ─────────────
        var drawerMovements = posEntries
            .Where(j => j.Type != JournalEntryType.SalesInvoice)
            .SelectMany(j => j.Lines
                .Where(l => posAccountIds.Contains(l.AccountId))
                .Select(l => new {
                    reference   = j.Reference,
                    description = j.Description ?? l.Description,
                    amount      = l.Debit - l.Credit,
                    date        = j.EntryDate,
                    type        = j.Type.ToString(),
                    entryId     = j.Id
                })
                .Where(m => m.amount != 0))
            .OrderBy(m => m.date)
            .ToList();

        // ── 8. Order count breakdown ───────────────────────────────────────────
        var ordersCount   = orders.Count(o => o.Status != OrderStatus.Returned);
        var returnsCount  = orders.Count(o => o.Status == OrderStatus.Returned);
        
        // ── 9. Respond ────────────────────────────────────────────────────────
        return Ok(new
        {
            date,
            businessDayFrom = from,
            businessDayTo   = to,
            stationId       = stationId ?? "DEFAULT",

            // Sales summary
            grossSales      = Math.Round(grossSales, 2),
            totalDiscounts  = Math.Round(totalDiscounts, 2),
            totalReturns    = Math.Round(totalReturns, 2),
            netSales        = Math.Round(netSales, 2),

            // Payment breakdown
            cashSales       = Math.Round(cashSales, 2),
            cardSales       = Math.Round(cardSales, 2),
            vodafoneSales   = Math.Round(vodafoneSales, 2),
            instapaySales   = Math.Round(instapaySales, 2),
            walletSales     = Math.Round(walletSales, 2),
            creditSales     = Math.Round(creditSales, 2),

            // Cash flow
            cashReceipts    = Math.Round(cashReceipts, 2),
            expenses        = Math.Round(expenses, 2),
            safeDrops       = Math.Round(safeDrops, 2),
            cashReturns     = Math.Round(cashReturns, 2),
            expectedCash    = Math.Round(expectedCash, 2),

            // Counts
            ordersCount,
            returnsCount,

            // Drawer movements (for shift closure table)
            drawerMovements,

            // Raw account IDs (for frontend validation)
            accountIds = new {
                posCash    = effectiveDrawerId,
                posBank    = posBankId,
                posVodafone = posVodaId,
                posInstapay = posInstaId,
                sales      = salesId,
                discount   = discountId,
                @return    = returnId,
                customer   = customerId
            }
        });
    }
}
