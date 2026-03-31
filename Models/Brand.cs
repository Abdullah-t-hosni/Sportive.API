namespace Sportive.API.Models;

public class Brand : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public int? ParentId { get; set; }
    public Brand? Parent { get; set; }
    public ICollection<Brand> SubBrands { get; set; } = new List<Brand>();

    // Navigation
    public ICollection<Product> Products { get; set; } = new List<Product>();
}
