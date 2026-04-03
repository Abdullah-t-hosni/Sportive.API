// ============================================================
// Data/AppDbContext.cs — تم إضافة AuditLogs DbSet
// ============================================================
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;

namespace Sportive.API.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Category> Categories            => Set<Category>();
    public DbSet<Brand> Brands                  => Set<Brand>();
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
    public DbSet<Notification> Notifications     => Set<Notification>();
    public DbSet<BackupRecord> BackupRecords     => Set<BackupRecord>();
    public DbSet<UserModulePermission> UserModulePermissions => Set<UserModulePermission>();
    public DbSet<StoreInfo> StoreInfo           => Set<StoreInfo>();

    // ✅ جديد — سجل التدقيق للعمليات الحساسة
    public DbSet<AuditLog> AuditLogs            => Set<AuditLog>();

    public DbSet<Supplier>             Suppliers            { get; set; }
    public DbSet<PurchaseInvoice>      PurchaseInvoices     { get; set; }
    public DbSet<PurchaseInvoiceItem>  PurchaseInvoiceItems { get; set; }
    public DbSet<SupplierPayment>      SupplierPayments     { get; set; }

    public DbSet<Account>        Accounts        { get; set; }
    public DbSet<AccountSystemMapping> AccountSystemMappings { get; set; }
    public DbSet<JournalEntry>   JournalEntries  { get; set; }
    public DbSet<JournalLine>    JournalLines    { get; set; }
    public DbSet<ReceiptVoucher> ReceiptVouchers { get; set; }
    public DbSet<PaymentVoucher> PaymentVouchers { get; set; }

    public DbSet<InventoryAudit>     InventoryAudits     { get; set; }
    public DbSet<InventoryAuditItem> InventoryAuditItems => Set<InventoryAuditItem>();
    public DbSet<InventoryMovement>  InventoryMovements  => Set<InventoryMovement>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ❌ تم إزالة فلاتر الحذف الناعم (Soft Delete) بناءً على طلب العميل
        // النظام الآن يستخدم المسح الفعلي (Hard Delete) لضمان نظافة قاعدة البيانات
        
        builder.Entity<AppUser>(e => {
            e.HasIndex(u => u.PhoneNumber);
        });

        builder.Entity<Customer>(e => {
            e.HasIndex(c => c.Phone);
        });

        builder.Entity<Category>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
        });

        builder.Entity<Brand>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
        });

        builder.Entity<Product>(e => {
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.DiscountPrice).HasPrecision(18, 2);
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
            e.Property(x => x.VatRate).HasPrecision(18, 2);
            e.Property(x => x.SKU).HasMaxLength(50);
            e.HasIndex(x => x.SKU).IsUnique();
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Brand).WithMany(b => b.Products)
             .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
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
            e.Property(x => x.TotalVatAmount).HasPrecision(18, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            e.Property(x => x.DeliveryFee).HasPrecision(18, 2);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasOne(x => x.Customer).WithMany(c => c.Orders)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeliveryAddress).WithMany()
             .HasForeignKey(x => x.DeliveryAddressId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.FulfillmentType).HasConversion<string>();
            e.Property(x => x.PaymentMethod).HasConversion<string>();
            e.Property(x => x.PaymentStatus).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
        });

        builder.Entity<OrderItem>(e => {
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.Property(x => x.VatRateApplied).HasPrecision(18, 2);
            e.Property(x => x.ItemVatAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(o => o.Items)
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(p => p.OrderItems)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderStatusHistory>(e => {
            e.Property(x => x.Status).HasConversion<string>();
        });

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

        builder.Entity<WishlistItem>(e => {
            e.HasOne(x => x.Customer).WithMany()
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany()
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
        });

        builder.Entity<Notification>(e => {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.IsRead });
        });

        // ── AUDIT LOG ─────────────────────────────────────────
        builder.Entity<AuditLog>(e => {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.EntityType);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        // ── PURCHASE & SUPPLIERS ──────────────────────────────
        builder.Entity<Supplier>(e => {
            e.Property(s => s.TotalPurchases).HasPrecision(18, 2);
            e.Property(s => s.TotalPaid).HasPrecision(18, 2);
        });

        builder.Entity<PurchaseInvoice>(e => {
            e.Property(i => i.SubTotal).HasPrecision(18, 2);
            e.Property(i => i.TaxAmount).HasPrecision(18, 2);
            e.Property(i => i.TaxPercent).HasPrecision(5, 2);
            e.Property(i => i.TotalAmount).HasPrecision(18, 2);
            e.Property(i => i.PaidAmount).HasPrecision(18, 2);
            e.HasOne(i => i.Supplier).WithMany(s => s.Invoices).HasForeignKey(i => i.SupplierId);
            e.Property(i => i.PaymentTerms).HasConversion<string>();
            e.Property(i => i.Status).HasConversion<string>();
        });

        builder.Entity<PurchaseInvoiceItem>(e => {
            e.Property(i => i.UnitCost).HasPrecision(18, 2);
            e.Property(i => i.TotalCost).HasPrecision(18, 2);
        });

        builder.Entity<SupplierPayment>(e => {
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.HasOne(p => p.Supplier).WithMany(s => s.Payments).HasForeignKey(p => p.SupplierId);
            e.HasOne(p => p.Invoice).WithMany(i => i.Payments)
                .HasForeignKey(p => p.PurchaseInvoiceId).IsRequired(false);
            e.Property(p => p.PaymentMethod).HasConversion<string>();
        });

        builder.Entity<Account>(e => {
            e.Property(a => a.OpeningBalance).HasPrecision(18,2);
            e.HasOne(a => a.Parent).WithMany(a => a.Children).HasForeignKey(a => a.ParentId).IsRequired(false);
            e.HasIndex(a => a.Code).IsUnique();
            e.Property(a => a.Type).HasConversion<string>();
            e.Property(a => a.Nature).HasConversion<string>();
        });

        builder.Entity<AccountSystemMapping>(e => {
            e.Property(x => x.Key).HasMaxLength(AccountSystemMapping.MaxKeyLength).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<JournalEntry>(e => {
            e.HasOne(j => j.ReversalOf).WithMany().HasForeignKey(j => j.ReversalOfId).IsRequired(false);
            e.Property(j => j.Type).HasConversion<string>();
            e.Property(j => j.Status).HasConversion<string>();
        });

        builder.Entity<StoreInfo>(e => {
            e.Property(x => x.VatRatePercent).HasPrecision(18, 2);
            e.Property(x => x.FixedDeliveryFee).HasPrecision(18, 2);
            e.Property(x => x.FreeDeliveryAt).HasPrecision(18, 2);
        });

        builder.Entity<JournalLine>(e => {
            e.Property(l => l.Debit).HasPrecision(18,2);
            e.Property(l => l.Credit).HasPrecision(18,2);
            e.HasOne(l => l.JournalEntry).WithMany(j => j.Lines).HasForeignKey(l => l.JournalEntryId);
            e.HasOne(l => l.Account).WithMany(a => a.Lines).HasForeignKey(l => l.AccountId);
        });

        builder.Entity<ReceiptVoucher>(e => {
            e.Property(v => v.Amount).HasPrecision(18,2);
            e.HasOne(v => v.CashAccount).WithMany().HasForeignKey(v => v.CashAccountId);
            e.HasOne(v => v.FromAccount).WithMany().HasForeignKey(v => v.FromAccountId);
            e.HasOne(v => v.JournalEntry).WithMany().HasForeignKey(v => v.JournalEntryId).IsRequired(false);
            e.Property(v => v.PaymentMethod).HasConversion<string>();
        });

        builder.Entity<PaymentVoucher>(e => {
            e.Property(v => v.Amount).HasPrecision(18,2);
            e.HasOne(v => v.CashAccount).WithMany().HasForeignKey(v => v.CashAccountId);
            e.HasOne(v => v.ToAccount).WithMany().HasForeignKey(v => v.ToAccountId);
            e.HasOne(v => v.JournalEntry).WithMany().HasForeignKey(v => v.JournalEntryId).IsRequired(false);
            e.Property(v => v.PaymentMethod).HasConversion<string>();
        });

        builder.Entity<StoreInfo>().HasData(
            new StoreInfo { StoreConfigId = 1, StoreBrandName = "Sportive", LastUpdateDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );
    }
}
