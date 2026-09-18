using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services.Loyalty;

public class LoyaltyService : ILoyaltyService
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoyaltyService> _logger;
    private static bool _tablesEnsured = false;

    public LoyaltyService(AppDbContext db, ILogger<LoyaltyService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureTablesAsync()
    {
        if (_tablesEnsured) return;

        try
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS `LoyaltyProgramSettings` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `IsEnabled` tinyint(1) NOT NULL DEFAULT 1,
                    `PointsPerCurrencyUnit` decimal(18,4) NOT NULL DEFAULT 0.1000,
                    `CurrencyUnitPerPointRedeemed` decimal(18,4) NOT NULL DEFAULT 0.1000,
                    `MinPointsToRedeem` int NOT NULL DEFAULT 50,
                    `MaxRedemptionPercentage` decimal(18,2) NOT NULL DEFAULT 50.00,
                    `PointsExpiryDays` int NOT NULL DEFAULT 365,
                    `SilverThreshold` decimal(18,2) NOT NULL DEFAULT 3000.00,
                    `SilverMultiplier` decimal(18,2) NOT NULL DEFAULT 1.25,
                    `GoldThreshold` decimal(18,2) NOT NULL DEFAULT 8000.00,
                    `GoldMultiplier` decimal(18,2) NOT NULL DEFAULT 1.50,
                    `PlatinumThreshold` decimal(18,2) NOT NULL DEFAULT 20000.00,
                    `PlatinumMultiplier` decimal(18,2) NOT NULL DEFAULT 2.00,
                    `UpdatedAt` datetime(6) NOT NULL,
                    PRIMARY KEY (`Id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            await _db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS `LoyaltyPointTransactions` (
                    `Id` int NOT NULL AUTO_INCREMENT,
                    `CustomerId` int NOT NULL,
                    `OrderId` int NULL,
                    `Points` decimal(18,2) NOT NULL,
                    `TransactionType` int NOT NULL,
                    `AmountEquivalent` decimal(18,2) NOT NULL DEFAULT 0.00,
                    `Note` varchar(500) NULL,
                    `CreatedBy` varchar(150) NULL,
                    `CreatedAt` datetime(6) NOT NULL,
                    `UpdatedAt` datetime(6) NOT NULL,
                    PRIMARY KEY (`Id`),
                    KEY `IX_LoyaltyPointTransactions_CustomerId` (`CustomerId`),
                    KEY `IX_LoyaltyPointTransactions_OrderId` (`OrderId`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            ");

            // Customer columns
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Customers` ADD COLUMN `LoyaltyPointsBalance` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `customers` ADD COLUMN `LoyaltyPointsBalance` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Customers` ADD COLUMN `LifetimePointsEarned` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `customers` ADD COLUMN `LifetimePointsEarned` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Customers` ADD COLUMN `CurrentTier` int NOT NULL DEFAULT 1;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `customers` ADD COLUMN `CurrentTier` int NOT NULL DEFAULT 1;"); } catch { }

            // Order columns
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Orders` ADD COLUMN `LoyaltyPointsRedeemed` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `orders` ADD COLUMN `LoyaltyPointsRedeemed` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Orders` ADD COLUMN `LoyaltyDiscountAmount` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `orders` ADD COLUMN `LoyaltyDiscountAmount` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `Orders` ADD COLUMN `LoyaltyPointsEarned` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }
            try { await _db.Database.ExecuteSqlRawAsync("ALTER TABLE `orders` ADD COLUMN `LoyaltyPointsEarned` decimal(18,2) NOT NULL DEFAULT 0.00;"); } catch { }

            _tablesEnsured = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure Loyalty tables/columns");
        }
    }

    private async Task<LoyaltyProgramSettings> GetOrCreateSettingsInternalAsync()
    {
        await EnsureTablesAsync();
        var settings = await _db.LoyaltyProgramSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new LoyaltyProgramSettings
            {
                Id = 1,
                IsEnabled = true,
                PointsPerCurrencyUnit = 0.1000m,
                CurrencyUnitPerPointRedeemed = 0.1000m,
                MinPointsToRedeem = 50,
                MaxRedemptionPercentage = 50.00m,
                PointsExpiryDays = 365,
                SilverThreshold = 3000.00m,
                SilverMultiplier = 1.25m,
                GoldThreshold = 8000.00m,
                GoldMultiplier = 1.50m,
                PlatinumThreshold = 20000.00m,
                PlatinumMultiplier = 2.00m,
                UpdatedAt = TimeHelper.GetEgyptTime()
            };
            _db.LoyaltyProgramSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }

    public async Task<LoyaltyProgramSettingsDto> GetSettingsAsync()
    {
        var s = await GetOrCreateSettingsInternalAsync();
        return MapToDto(s);
    }

    public async Task<LoyaltyProgramSettingsDto> UpdateSettingsAsync(UpdateLoyaltySettingsDto dto, string? updatedBy)
    {
        var s = await GetOrCreateSettingsInternalAsync();
        s.IsEnabled = dto.IsEnabled;
        s.PointsPerCurrencyUnit = dto.PointsPerCurrencyUnit > 0 ? dto.PointsPerCurrencyUnit : 0.1000m;
        s.CurrencyUnitPerPointRedeemed = dto.CurrencyUnitPerPointRedeemed > 0 ? dto.CurrencyUnitPerPointRedeemed : 0.1000m;
        s.MinPointsToRedeem = dto.MinPointsToRedeem >= 0 ? dto.MinPointsToRedeem : 50;
        s.MaxRedemptionPercentage = Math.Clamp(dto.MaxRedemptionPercentage, 1, 100);
        s.PointsExpiryDays = dto.PointsExpiryDays >= 0 ? dto.PointsExpiryDays : 365;
        s.SilverThreshold = dto.SilverThreshold;
        s.SilverMultiplier = dto.SilverMultiplier;
        s.GoldThreshold = dto.GoldThreshold;
        s.GoldMultiplier = dto.GoldMultiplier;
        s.PlatinumThreshold = dto.PlatinumThreshold;
        s.PlatinumMultiplier = dto.PlatinumMultiplier;
        s.UpdatedAt = TimeHelper.GetEgyptTime();

        await _db.SaveChangesAsync();
        return MapToDto(s);
    }

    public async Task<CustomerLoyaltyInfoDto> GetCustomerLoyaltyInfoAsync(int customerId)
    {
        await EnsureTablesAsync();
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) throw new KeyNotFoundException($"Customer with ID {customerId} not found");

        var settings = await GetOrCreateSettingsInternalAsync();
        var multiplier = GetTierMultiplier(customer.CurrentTier, settings);
        var cashValue = Math.Round(customer.LoyaltyPointsBalance * settings.CurrencyUnitPerPointRedeemed, 2);
        var isEligible = settings.IsEnabled && customer.LoyaltyPointsBalance >= settings.MinPointsToRedeem;

        return new CustomerLoyaltyInfoDto(
            CustomerId: customer.Id,
            CustomerName: customer.FullName,
            Balance: customer.LoyaltyPointsBalance,
            LifetimePoints: customer.LifetimePointsEarned,
            Tier: (int)customer.CurrentTier,
            TierName: customer.CurrentTier.ToString(),
            Multiplier: multiplier,
            CashValue: cashValue,
            IsEligibleToRedeem: isEligible,
            MinPointsToRedeem: settings.MinPointsToRedeem
        );
    }

    public async Task<List<LoyaltyPointTransactionDto>> GetCustomerTransactionsAsync(int customerId, int take = 50)
    {
        await EnsureTablesAsync();
        return await _db.LoyaltyPointTransactions
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .Select(t => new LoyaltyPointTransactionDto(
                t.Id,
                t.CustomerId,
                t.OrderId,
                t.Order != null ? t.Order.OrderNumber : null,
                t.Points,
                t.TransactionType.ToString(),
                t.AmountEquivalent,
                t.Note,
                t.CreatedBy,
                t.CreatedAt
            ))
            .ToListAsync();
    }

    public async Task<LoyaltyPointTransactionDto> AdjustCustomerPointsAsync(int customerId, decimal points, string note, string? createdBy)
    {
        await EnsureTablesAsync();
        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) throw new KeyNotFoundException($"Customer with ID {customerId} not found");

        var settings = await GetOrCreateSettingsInternalAsync();
        var amountEq = Math.Round(Math.Abs(points) * settings.CurrencyUnitPerPointRedeemed, 2);

        customer.LoyaltyPointsBalance = Math.Max(0, customer.LoyaltyPointsBalance + points);
        if (points > 0)
        {
            customer.LifetimePointsEarned += points;
            customer.CurrentTier = CalculateTier(customer.TotalSales, settings);
        }

        var tx = new LoyaltyPointTransaction
        {
            CustomerId = customerId,
            Points = points,
            TransactionType = LoyaltyTransactionType.Adjusted,
            AmountEquivalent = amountEq,
            Note = note,
            CreatedBy = createdBy,
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.LoyaltyPointTransactions.Add(tx);
        await _db.SaveChangesAsync();

        return new LoyaltyPointTransactionDto(
            tx.Id,
            tx.CustomerId,
            null,
            null,
            tx.Points,
            tx.TransactionType.ToString(),
            tx.AmountEquivalent,
            tx.Note,
            tx.CreatedBy,
            tx.CreatedAt
        );
    }

    public async Task<(decimal pointsEarned, CustomerLoyaltyTier newTier)> ProcessOrderEarnedPointsAsync(int orderId, int customerId, decimal eligibleAmount, string orderNumber)
    {
        await EnsureTablesAsync();
        var settings = await GetOrCreateSettingsInternalAsync();
        if (!settings.IsEnabled || eligibleAmount <= 0)
            return (0, CustomerLoyaltyTier.Bronze);

        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) return (0, CustomerLoyaltyTier.Bronze);

        // Recalculate tier based on customer's total spent
        customer.CurrentTier = CalculateTier(customer.TotalSales, settings);
        var multiplier = GetTierMultiplier(customer.CurrentTier, settings);

        // Calculate points
        var rawPoints = eligibleAmount * settings.PointsPerCurrencyUnit * multiplier;
        var pointsEarned = Math.Round(rawPoints, 2);

        if (pointsEarned > 0)
        {
            customer.LoyaltyPointsBalance += pointsEarned;
            customer.LifetimePointsEarned += pointsEarned;

            var amountEq = Math.Round(pointsEarned * settings.CurrencyUnitPerPointRedeemed, 2);

            var tx = new LoyaltyPointTransaction
            {
                CustomerId = customerId,
                OrderId = orderId,
                Points = pointsEarned,
                TransactionType = LoyaltyTransactionType.Earned,
                AmountEquivalent = amountEq,
                Note = $"نقاط مكتسبة عن فاتورة #{orderNumber}",
                CreatedAt = TimeHelper.GetEgyptTime()
            };

            _db.LoyaltyPointTransactions.Add(tx);

            // Update order record
            var order = await _db.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.LoyaltyPointsEarned = pointsEarned;
            }

            await _db.SaveChangesAsync();
        }

        return (pointsEarned, customer.CurrentTier);
    }

    public async Task<bool> ProcessOrderRedemptionAsync(int orderId, int customerId, decimal pointsToRedeem, decimal discountAmount, string orderNumber)
    {
        await EnsureTablesAsync();
        var settings = await GetOrCreateSettingsInternalAsync();
        if (!settings.IsEnabled || pointsToRedeem <= 0) return false;

        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) return false;

        // Verify balance
        if (customer.LoyaltyPointsBalance < pointsToRedeem)
        {
            _logger.LogWarning("Customer {CustomerId} has insufficient points balance: {Balance} < {ToRedeem}", customerId, customer.LoyaltyPointsBalance, pointsToRedeem);
            pointsToRedeem = customer.LoyaltyPointsBalance;
        }

        if (pointsToRedeem <= 0) return false;

        customer.LoyaltyPointsBalance = Math.Max(0, customer.LoyaltyPointsBalance - pointsToRedeem);

        var tx = new LoyaltyPointTransaction
        {
            CustomerId = customerId,
            OrderId = orderId,
            Points = -pointsToRedeem,
            TransactionType = LoyaltyTransactionType.Redeemed,
            AmountEquivalent = discountAmount,
            Note = $"استبدال نقاط بخصم {discountAmount:N2} ج في فاتورة #{orderNumber}",
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.LoyaltyPointTransactions.Add(tx);

        var order = await _db.Orders.FindAsync(orderId);
        if (order != null)
        {
            order.LoyaltyPointsRedeemed = pointsToRedeem;
            order.LoyaltyDiscountAmount = discountAmount;
        }

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ProcessOrderReturnReversalAsync(int orderId, int customerId, decimal returnedRatio, string orderNumber)
    {
        await EnsureTablesAsync();
        var order = await _db.Orders.FindAsync(orderId);
        if (order == null || order.LoyaltyPointsEarned <= 0) return;

        var customer = await _db.Customers.FindAsync(customerId);
        if (customer == null) return;

        var pointsToReverse = Math.Round(order.LoyaltyPointsEarned * returnedRatio, 2);
        if (pointsToReverse <= 0) return;

        customer.LoyaltyPointsBalance = Math.Max(0, customer.LoyaltyPointsBalance - pointsToReverse);

        var settings = await GetOrCreateSettingsInternalAsync();
        var amountEq = Math.Round(pointsToReverse * settings.CurrencyUnitPerPointRedeemed, 2);

        var tx = new LoyaltyPointTransaction
        {
            CustomerId = customerId,
            OrderId = orderId,
            Points = -pointsToReverse,
            TransactionType = LoyaltyTransactionType.Reversed,
            AmountEquivalent = amountEq,
            Note = $"إلغاء نقاط مكتسبة بسبب مرتجع فاتورة #{orderNumber}",
            CreatedAt = TimeHelper.GetEgyptTime()
        };

        _db.LoyaltyPointTransactions.Add(tx);
        await _db.SaveChangesAsync();
    }

    private static CustomerLoyaltyTier CalculateTier(decimal totalSales, LoyaltyProgramSettings s)
    {
        if (totalSales >= s.PlatinumThreshold) return CustomerLoyaltyTier.Platinum;
        if (totalSales >= s.GoldThreshold) return CustomerLoyaltyTier.Gold;
        if (totalSales >= s.SilverThreshold) return CustomerLoyaltyTier.Silver;
        return CustomerLoyaltyTier.Bronze;
    }

    private static decimal GetTierMultiplier(CustomerLoyaltyTier tier, LoyaltyProgramSettings s)
    {
        return tier switch
        {
            CustomerLoyaltyTier.Platinum => s.PlatinumMultiplier,
            CustomerLoyaltyTier.Gold     => s.GoldMultiplier,
            CustomerLoyaltyTier.Silver   => s.SilverMultiplier,
            _                            => 1.00m
        };
    }

    private static LoyaltyProgramSettingsDto MapToDto(LoyaltyProgramSettings s) => new(
        IsEnabled: s.IsEnabled,
        PointsPerCurrencyUnit: s.PointsPerCurrencyUnit,
        CurrencyUnitPerPointRedeemed: s.CurrencyUnitPerPointRedeemed,
        MinPointsToRedeem: s.MinPointsToRedeem,
        MaxRedemptionPercentage: s.MaxRedemptionPercentage,
        PointsExpiryDays: s.PointsExpiryDays,
        SilverThreshold: s.SilverThreshold,
        SilverMultiplier: s.SilverMultiplier,
        GoldThreshold: s.GoldThreshold,
        GoldMultiplier: s.GoldMultiplier,
        PlatinumThreshold: s.PlatinumThreshold,
        PlatinumMultiplier: s.PlatinumMultiplier,
        UpdatedAt: s.UpdatedAt
    );
}
