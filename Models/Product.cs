namespace Sportive.API.Models;

public enum ProductStatus { Active, OutOfStock, Discontinued, Draft, Hidden }

public class Product : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionAr { get; set; }
    public string? DescriptionEn { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountPrice { get; set; }
    public decimal? CostPrice { get; set; }        // تكلفة المنتج (للحسابات الداخلية)
    public string SKU { get; set; } = string.Empty;
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Active;
    public bool IsFeatured { get; set; } = false;
    public int TotalStock { get; set; } = 0;
    public int ReorderLevel { get; set; } = 0; // حد الطلب للمنتج الرئيسي
    public bool HasTax { get; set; } = true; // خيار وجود ضريبة على المنتج

    // Category
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // Navigation
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}

public class ProductVariant : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string? Size { get; set; }   // XS, S, M, L, XL, XXL
    public string? Color { get; set; }
    public string? ColorAr { get; set; }
    public int StockQuantity { get; set; } = 0;
    public int ReorderLevel { get; set; } = 0; // حد الطلب للموديل/المقاس الصغير
    public decimal? PriceAdjustment { get; set; } = 0; // extra price for this variant
    public string? ImageUrl { get; set; }
    public string? ImagePublicId { get; set; }
}

public class ProductImage : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string ImageUrl { get; set; } = string.Empty;
    public string? ImagePublicId { get; set; }
    public bool IsMain { get; set; } = false;
    public string? ColorAr { get; set; } // The color associated with this image
    public int SortOrder { get; set; } = 0;
}

public class Review : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int Rating { get; set; } // 1-5
    public string? Comment { get; set; }
    public bool IsApproved { get; set; } = false;
}
