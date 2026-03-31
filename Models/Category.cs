namespace Sportive.API.Models;

public class Category : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;  // الاسم بالعربي
    public string NameEn { get; set; } = string.Empty;  // الاسم بالإنجليزي
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    // Subcategory support
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    // Navigation
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
