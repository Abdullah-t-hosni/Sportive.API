using Sportive.API.DTOs;

namespace Sportive.API.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryDto>> GetAllAsync();
    Task<List<CategoryDto>> GetTreeAsync();
    Task<CategoryDto?> GetByIdAsync(int id);
    Task<CategoryDto> CreateAsync(CreateCategoryDto dto);
    Task<CategoryDto> UpdateAsync(int id, CreateCategoryDto dto);
    Task DeleteAsync(int id);
}
