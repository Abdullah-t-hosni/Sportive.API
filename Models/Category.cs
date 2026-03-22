namespace Sportive.API.Models;

public enum CategoryType
{
    Men = 1,      // رجالي
    Women = 2,    // حريمي
    Kids = 3,     // أطفال
    Equipment = 4 // أدوات رياضية
}

public class Category : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;  // الاسم بالعربي
    public string NameEn { get; set; } = string.Empty;  // الاسم بالإنجليزي
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public CategoryType Type { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
