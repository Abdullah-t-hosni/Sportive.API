using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;

namespace Sportive.API.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories            => Set<Category>();
    public DbSet<Product> Products               => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages     => Set<ProductImage>();
    public DbSet<Review> Reviews                 => Set<Review>();
    public DbSet<Customer> Customers             => Set<Customer>();
    public DbSet<Address> Addresses              => Set<Address>();
    public DbSet<Order> Orders                   => Set<Order>();
    public DbSet<OrderItem> OrderItems           => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<CartItem> CartItems             => Set<CartItem>();
    public DbSet<Coupon> Coupons                 => Set<Coupon>();
    public DbSet<WishlistItem> WishlistItems     => Set<WishlistItem>();
    public DbSet<Notification> Notifications    => Set<Notification>();
    public DbSet<WishlistItem> WishlistItems      => Set<WishlistItem>();
    public DbSet<Notification> Notifications      => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Product>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<ProductVariant>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<ProductImage>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Review>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Customer>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Address>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Order>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<OrderItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<OrderStatusHistory>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<CartItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Coupon>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<WishlistItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Notification>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<WishlistItem>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Notification>().HasQueryFilter(x => !x.IsDeleted);
        builder.Entity<Notification>().HasQueryFilter(x => !x.IsDeleted);

        builder.Entity<Category>(e => {
            e.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        });

        builder.Entity<Product>(e => {
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.DiscountPrice).HasPrecision(18, 2);
            e.Property(x => x.SKU).HasMaxLength(50);
            e.HasIndex(x => x.SKU).IsUnique();
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ProductVariant>(e =>
            e.Property(x => x.PriceAdjustment).HasPrecision(18, 2));

        builder.Entity<Customer>(e => {
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
        });

        builder.Entity<Address>(e =>
            e.HasOne(x => x.Customer).WithMany(c => c.Addresses)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade)
             .IsRequired(false));

        builder.Entity<Order>(e => {
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.DeliveryFee).HasPrecision(18, 2);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasOne(x => x.Customer).WithMany(c => c.Orders)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeliveryAddress).WithMany()
             .HasForeignKey(x => x.DeliveryAddressId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<OrderItem>(e => {
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(o => o.Items)
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(p => p.OrderItems)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WishlistItem>(e =>
            e.HasOne(x => x.Customer).WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<Notification>(e =>
            e.HasOne(x => x.Customer).WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<CartItem>(e =>
            e.HasOne(x => x.Customer).WithMany(c => c.CartItems)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade));

        builder.Entity<Coupon>(e => {
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.DiscountValue).HasPrecision(18, 2);
            e.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxDiscountAmount).HasPrecision(18, 2);
        });

        builder.Entity<Notification>(e => {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.IsRead });
        });

        builder.Entity<WishlistItem>(e => {
            e.HasOne(x => x.Customer).WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany()
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        });

        builder.Entity<Category>().HasData(
            new Category { Id=1, NameAr="رجالي",         NameEn="Men",              Type=CategoryType.Men,       IsActive=true, CreatedAt=new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) },
            new Category { Id=2, NameAr="حريمي",         NameEn="Women",            Type=CategoryType.Women,     IsActive=true, CreatedAt=new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) },
            new Category { Id=3, NameAr="أطفال",         NameEn="Kids",             Type=CategoryType.Kids,      IsActive=true, CreatedAt=new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) },
            new Category { Id=4, NameAr="أدوات رياضية", NameEn="Sports Equipment", Type=CategoryType.Equipment, IsActive=true, CreatedAt=new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) }
        );
    }
}

// This is appended - copy the DbSets below into your AppDbContext manually
// public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
// public DbSet<Notification> Notifications => Set<Notification>();
