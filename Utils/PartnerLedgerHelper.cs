using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Utils;

/// <summary>
/// Shared ledger helpers for Customer (Receivables) and Supplier (Payables) balances
/// so reports, partner-position banners, settlement validation, and entity sync agree.
/// </summary>
public static class PartnerLedgerHelper
{
    public sealed record PartnerAccountSets(
        int? CustomerControlAccountId,
        int? SupplierControlAccountId,
        HashSet<int> CustomerAccountIds,
        HashSet<int> SupplierAccountIds,
        HashSet<int> CashAndWalletAccountIds
    );

    private static readonly string[] CashMappingKeysLower =
    [
        MappingKeys.Cash.ToLowerInvariant(),
        MappingKeys.PaymentVoucherCash.ToLowerInvariant(),
        MappingKeys.PosCash.ToLowerInvariant(),
        MappingKeys.PosBank.ToLowerInvariant(),
        MappingKeys.PosVodafone.ToLowerInvariant(),
        MappingKeys.PosInstaPay.ToLowerInvariant(),
        MappingKeys.WebCash.ToLowerInvariant(),
        MappingKeys.WebBank.ToLowerInvariant(),
        MappingKeys.WebVodafone.ToLowerInvariant(),
        MappingKeys.WebInstaPay.ToLowerInvariant(),
    ];

    public static async Task<PartnerAccountSets> ResolvePartnerAccountSetsAsync(AppDbContext db)
    {
        var mappings = await db.AccountSystemMappings
            .AsNoTracking()
            .Where(m => m.AccountId != null)
            .ToDictionaryAsync(m => m.Key.ToLower(), m => m.AccountId);

        int? custMappedId = mappings.TryGetValue(MappingKeys.Customer.ToLowerInvariant(), out var cm) ? cm : null;
        int? suppMappedId = mappings.TryGetValue(MappingKeys.Supplier.ToLowerInvariant(), out var sm) ? sm : null;

        var cashIds = new HashSet<int>();
        foreach (var k in CashMappingKeysLower)
        {
            if (mappings.TryGetValue(k, out var cid) && cid.HasValue)
                cashIds.Add(cid.Value);
        }

        var candidateAccounts = await db.Accounts
            .AsNoTracking()
            .Where(a =>
                (custMappedId.HasValue && a.Id == custMappedId.Value) ||
                (suppMappedId.HasValue && a.Id == suppMappedId.Value) ||
                (a.Code != null && (a.Code.StartsWith("1107") || a.Code.StartsWith("2101"))))
            .Select(a => new { a.Id, a.Code, a.CanReceivePayment })
            .ToListAsync();

        var customerIds = new HashSet<int>();
        var supplierIds = new HashSet<int>();

        foreach (var acc in candidateAccounts)
        {
            if (acc.Id == custMappedId ||
                (acc.Code != null && acc.Code.StartsWith("1107") && !acc.CanReceivePayment && !cashIds.Contains(acc.Id)))
            {
                customerIds.Add(acc.Id);
            }

            if (acc.Id == suppMappedId ||
                (acc.Code != null && acc.Code.StartsWith("2101") && !acc.CanReceivePayment && !cashIds.Contains(acc.Id)))
            {
                supplierIds.Add(acc.Id);
            }
        }

        return new PartnerAccountSets(custMappedId, suppMappedId, customerIds, supplierIds, cashIds);
    }

    public static List<int> GetCustomerAccountIdList(PartnerAccountSets sets, int? mainAccountId)
    {
        var result = new HashSet<int>(sets.CustomerAccountIds);
        if (mainAccountId.HasValue && !sets.CashAndWalletAccountIds.Contains(mainAccountId.Value))
        {
            result.Add(mainAccountId.Value);
        }
        return result.ToList();
    }

    public static List<int> GetSupplierAccountIdList(PartnerAccountSets sets, int? mainAccountId)
    {
        var result = new HashSet<int>(sets.SupplierAccountIds);
        if (mainAccountId.HasValue && !sets.CashAndWalletAccountIds.Contains(mainAccountId.Value))
        {
            result.Add(mainAccountId.Value);
        }
        return result.ToList();
    }

    public static decimal GetCustomerSubAccountOpeningBalance(Customer customer, PartnerAccountSets sets)
    {
        if (customer.MainAccount == null) return 0m;
        if (customer.MainAccount.Code == "1107") return 0m;
        if (sets.CustomerControlAccountId.HasValue && customer.MainAccountId == sets.CustomerControlAccountId.Value && (customer.MainAccount.Code?.Length ?? 0) <= 4)
            return 0m;
        return customer.MainAccount.OpeningBalance;
    }

    public static async Task<PartnerPositionDto> ComputePartnerPositionAsync(AppDbContext db, Customer? customer, Supplier? supplier)
    {
        var sets = await ResolvePartnerAccountSetsAsync(db);

        decimal custBal = 0m;
        if (customer != null)
        {
            var custAccIds = GetCustomerAccountIdList(sets, customer.MainAccountId);
            var custOpening = GetCustomerSubAccountOpeningBalance(customer, sets);
            var custJournalNet = custAccIds.Count > 0
                ? await db.JournalLines
                    .AsNoTracking()
                    .Where(l => l.CustomerId == customer.Id
                             && l.JournalEntry.Status != JournalEntryStatus.Draft
                             && custAccIds.Contains(l.AccountId))
                    .SumAsync(l => (decimal?)l.Debit - (decimal?)l.Credit) ?? 0m
                : 0m;
            custBal = custOpening + custJournalNet;
        }

        decimal suppBal = 0m;
        if (supplier != null)
        {
            var suppAccIds = GetSupplierAccountIdList(sets, supplier.MainAccountId);
            var suppOpening = supplier.OpeningBalance;
            var suppJournalNet = suppAccIds.Count > 0
                ? await db.JournalLines
                    .AsNoTracking()
                    .Where(l => l.SupplierId == supplier.Id
                             && l.JournalEntry.Status != JournalEntryStatus.Draft
                             && suppAccIds.Contains(l.AccountId))
                    .SumAsync(l => (decimal?)l.Credit - (decimal?)l.Debit) ?? 0m
                : 0m;
            suppBal = suppOpening + suppJournalNet;
        }

        var netBal = custBal - suppBal;
        var netStatus = netBal > 0.001m ? "CustomerOwes" : (netBal < -0.001m ? "SupplierOwes" : "Balanced");
        var maxSettlement = Math.Min(Math.Max(0m, custBal), Math.Max(0m, suppBal));

        return new PartnerPositionDto(
            customer?.Id,
            customer?.FullName,
            customer?.Phone,
            custBal,
            supplier?.Id,
            supplier?.Name,
            supplier?.Phone,
            suppBal,
            netBal,
            netStatus,
            maxSettlement,
            maxSettlement > 0m
        );
    }
}
