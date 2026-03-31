using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface IInventoryService
{
    /// <summary>
    /// يسجل حركة مخزنية ويحدث رصيد المنتج/الموديل
    /// </summary>
    Task LogMovementAsync(
        InventoryMovementType type,
        int quantity, // (+) للزيادة، (-) للعجز
        int? productId = null,
        int? variantId = null,
        string? reference = null,
        string? note = null,
        string? userId = null
    );

    /// <summary>
    /// يجلب تاريخ حركات صنف معين
    /// </summary>
    Task<List<InventoryMovement>> GetMovementsAsync(int? productId = null, int? variantId = null);
    
    /// <summary>
    /// يجلب رصيد المخزن الفعلي لصنف (للتأكد)
    /// </summary>
    Task<int> GetCurrentStockAsync(int? productId = null, int? variantId = null);
}
