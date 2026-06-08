using Sportive.API.DTOs;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IOrderService
{
    Task<PaginatedResult<OrderSummaryDto>> GetOrdersAsync(int page, int pageSize, OrderStatus? status = null, string? search = null, int? customerId = null, DateTime? fromDate = null, DateTime? toDate = null, string? salesPersonId = null, OrderSource? source = null, PaymentMethod? paymentMethod = null, string? orderBy = null, bool descending = false);
    Task<OrderDetailDto?> GetOrderByIdAsync(int id);
    Task<PaginatedResult<OrderSummaryDto>> GetCustomerOrdersAsync(int customerId, int page, int pageSize);
    Task<OrderDetailDto> CreateOrderAsync(int? customerId, CreateOrderDto dto);
    Task<OrderDetailDto> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusDto dto, string updatedByUserId);
    Task<OrderDetailDto> ProcessPartialReturnAsync(int orderId, PartialReturnDto dto, string updatedByUserId);
    Task<string> GenerateOrderNumberAsync(OrderSource source = OrderSource.Website);
    Task<string> ProcessDirectReturnAsync(DirectReturnDto dto, string updatedByUserId);
    Task<OrderDetailDto> UpdateOrderAsync(int orderId, UpdateOrderDto dto, string updatedByUserId);
    Task<OrderDetailDto> ConvertToCostAsync(int orderId, string refundMethod, string updatedByUserId);
    Task SyncAllOrderAccountingAsync(int? daysLimit = null);
    Task UpdateSalesReturnAsync(string reference, UpdateSalesReturnDto dto, string updatedByUserId);
}
