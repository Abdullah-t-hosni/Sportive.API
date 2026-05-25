using Sportive.API.DTOs;

namespace Sportive.API.Interfaces;

public interface ICustomerService
{
    Task<PaginatedResult<CustomerDetailDto>> GetCustomersAsync(
        int page, int pageSize, string? search = null,
        decimal? minSpent = null, int? minOrders = null,
        DateTime? joinStartDate = null, DateTime? joinEndDate = null,
        int? categoryId = null, bool? hasDebt = null,
        string? orderBy = null, bool isDescending = true,
        string? source = null);
    Task<CustomerDetailDto?> GetCustomerByIdAsync(int id);
    Task<CustomerDetailDto?> GetCustomerByEmailAsync(string email);
    Task<CustomerDetailDto?> GetCustomerByUserIdAsync(string userId);
    Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerDto dto);
    Task<bool> ToggleCustomerAsync(int id);
    Task<bool> DeleteCustomerAsync(int id);
    Task<AddressDto> AddAddressAsync(int customerId, CreateAddressDto dto);
    Task DeleteAddressAsync(int customerId, int addressId);
    Task SetDefaultAddressAsync(int customerId, int addressId);
    Task EnsureCustomerAccountAsync(int customerId, bool isEmployee = false, int? employeeId = null);
    Task SyncAllMissingAccountsAsync();
    Task<CustomerDetailDto> UpdateCustomerAsync(int id, UpdateCustomerDto dto);
    Task<List<CustomerRfmDto>> GetRfmDataAsync();
    Task<int> GetOrCreateCustomerIdByUserIdAsync(string userId);
    Task EvaluateCustomerCategoryAsync(int customerId);
    Task<CustomerInsightsDto> GetInsightsAsync();
}
