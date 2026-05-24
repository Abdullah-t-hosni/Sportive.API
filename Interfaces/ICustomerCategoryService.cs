using Sportive.API.DTOs;

namespace Sportive.API.Interfaces;

public interface ICustomerCategoryService
{
    Task<List<CustomerCategoryDto>> GetAllAsync();
    Task<CustomerCategoryDto?> GetByIdAsync(int id);
    Task<CustomerCategoryDto> CreateAsync(CreateCustomerCategoryDto dto);
    Task<CustomerCategoryDto> UpdateAsync(int id, UpdateCustomerCategoryDto dto);
    Task DeleteAsync(int id);
}
