using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Services.Loyalty;

public interface ILoyaltyService
{
    Task EnsureTablesAsync();
    Task<LoyaltyProgramSettingsDto> GetSettingsAsync();
    Task<LoyaltyProgramSettingsDto> UpdateSettingsAsync(UpdateLoyaltySettingsDto dto, string? updatedBy);
    Task<CustomerLoyaltyInfoDto> GetCustomerLoyaltyInfoAsync(int customerId);
    Task<List<LoyaltyPointTransactionDto>> GetCustomerTransactionsAsync(int customerId, int take = 50);
    Task<LoyaltyPointTransactionDto> AdjustCustomerPointsAsync(int customerId, decimal points, string note, string? createdBy);
    Task<(decimal pointsEarned, CustomerLoyaltyTier newTier)> ProcessOrderEarnedPointsAsync(int orderId, int customerId, decimal eligibleAmount, string orderNumber);
    Task<bool> ProcessOrderRedemptionAsync(int orderId, int customerId, decimal pointsToRedeem, decimal discountAmount, string orderNumber);
    Task ProcessOrderReturnReversalAsync(int orderId, int customerId, decimal returnedRatio, string orderNumber);
    Task ProcessOrderDeletionAsync(int orderId, int? customerId, string orderNumber);
    Task ProcessOrderCancellationAsync(int orderId, int customerId, string orderNumber);
    Task ProcessOrderReactivateAsync(int orderId, int customerId, string orderNumber);
}
