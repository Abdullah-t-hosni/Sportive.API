using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sportive.API.DTOs.System;

namespace Sportive.API.Interfaces;

public interface ISubscriptionService
{
    Task<IEnumerable<SubscriptionDto>> GetAllSubscriptionsAsync();
    Task<SubscriptionDto?> GetSubscriptionByIdAsync(int id);
    Task<(bool Success, string Message, SubscriptionDto? Data)> CreateSubscriptionAsync(CreateSubscriptionDto request);
    Task<(bool Success, string Message, SubscriptionDto? Data)> UpdateSubscriptionAsync(int id, UpdateSubscriptionDto request);
}
