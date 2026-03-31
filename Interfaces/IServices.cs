using Sportive.API.DTOs;
using Sportive.API.Models;
using Sportive.API.Services;

namespace Sportive.API.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto, bool isCustomer = true);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    Task<bool> ChangePasswordAsync(string userId, ChangePasswordDto dto);
    Task<bool> AssignRoleAsync(string userId, string role);
}

public interface IProductService
{
    Task<PaginatedResult<ProductSummaryDto>> GetProductsAsync(ProductFilterDto filter);
    Task<ProductDetailDto?> GetProductByIdAsync(int id);
    Task<ProductDetailDto> CreateProductAsync(CreateProductDto dto);
    Task<ProductDetailDto> UpdateProductAsync(int id, UpdateProductDto dto);
    Task DeleteProductAsync(int id);
    Task<bool> UpdateStockAsync(int variantId, int quantity);
    Task<ProductVariantDto> AddVariantAsync(int productId, CreateVariantDto dto);
    Task<ProductVariantDto> UpdateVariantAsync(int variantId, CreateVariantDto dto);
    Task<bool> DeleteVariantAsync(int variantId);
    Task<bool> UpdateCostPriceAsync(int productId, decimal? costPrice);
    Task UpdateTotalStockAsync(int productId);
    Task<List<ProductSummaryDto>> GetFeaturedProductsAsync(int count = 8);
    Task<List<ProductSummaryDto>> GetRelatedProductsAsync(int productId, int count = 4);
}

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync();
    Task<List<CategoryDto>> GetTreeAsync();
    Task<CategoryDto?> GetByIdAsync(int id);
    Task<CategoryDto> CreateAsync(CreateCategoryDto dto);
    Task<CategoryDto> UpdateAsync(int id, CreateCategoryDto dto);
    Task DeleteAsync(int id);
}

public interface IBrandService
{
    Task<List<BrandDto>> GetAllAsync();
    Task<BrandDto?> GetByIdAsync(int id);
    Task<BrandDto> CreateAsync(CreateBrandDto dto);
    Task<BrandDto> UpdateAsync(int id, UpdateBrandDto dto);
    Task DeleteAsync(int id);
}

public interface IOrderService
{
    Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(int page, int pageSize, OrderStatus? status = null, string? search = null, int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, int? source = null);
    Task<OrderDetailDto?> GetOrderByIdAsync(int id);
    Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize);
    Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto);
    Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId);
    Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website);
}

public interface ICartService
{
    Task<CartSummaryDto> GetCartAsync(int customerId);
    Task<CartSummaryDto> AddToCartAsync(int customerId, AddToCartDto dto);
    Task<CartSummaryDto> UpdateCartItemAsync(int customerId, int cartItemId, UpdateCartItemDto dto);
    Task<CartSummaryDto> RemoveFromCartAsync(int customerId, int cartItemId);
    Task ClearCartAsync(int customerId);
}

public interface ICustomerService
{
    Task<PaginatedResult<CustomerDetailDto>> GetCustomersAsync(int page, int pageSize, string? search = null);
    Task<CustomerDetailDto?> GetCustomerByIdAsync(int id);
    Task<CustomerDetailDto?> GetCustomerByEmailAsync(string email);
    Task<CustomerDetailDto> CreateCustomerAsync(CreateCustomerDto dto);
    Task<bool> ToggleCustomerAsync(int id);
    Task<bool> DeleteCustomerAsync(int id);
    Task<AddressDto> AddAddressAsync(int customerId, CreateAddressDto dto);
    Task DeleteAddressAsync(int customerId, int addressId);
    Task SetDefaultAddressAsync(int customerId, int addressId);
}

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync();
    Task<List<SalesChartDto>> GetSalesChartAsync(string period);
    Task<List<TopProductDto>> GetTopProductsAsync(int count = 10);
    Task<List<OrderStatusStatsDto>> GetOrderStatusStatsAsync();
    Task<List<OrderSummaryDto>> GetRecentOrdersAsync(int count = 10);
    Task<AnalyticsSummaryDto> GetAnalyticsSummaryAsync();
    Task<byte[]> ExportSalesToCsvAsync(DateTime? from, DateTime? to);
    Task<AdvancedDashboardStatsDto> GetAdvancedStatsAsync();
    Task<StaffPerformanceDto> GetStaffStatsAsync(string staffId);
    Task<object> GetKpiAsync();
    Task TriggerLiveUpdateAsync(); // Pushes to all Admins via SignalR
}

public interface IPdfService
{
    Task<byte[]> GenerateOrderPdfAsync(OrderDetailDto order);
}

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}
