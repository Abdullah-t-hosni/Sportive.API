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
    public DbSet<OrderPayment> OrderPayments     => Set<OrderPayment>();
    public DbSet<Coupon> Coupons                 => Set<Coupon>();
    public DbSet<WishlistItem> WishlistItems     => Set<WishlistItem>();
    public DbSet<Notification> Notifications     => Set<Notification>();
    public DbSet<BackupRecord> BackupRecords     => Set<BackupRecord>();
    public DbSet<UserModulePermission> UserModulePermissions => Set<UserModulePermission>();
    public DbSet<StoreInfo> StoreInfo           => Set<StoreInfo>();
    public DbSet<ShippingZone> ShippingZones    => Set<ShippingZone>();

    // ✅ جديد — سجل التدقيق للعمليات الحساسة
    public DbSet<AuditLog> AuditLogs            => Set<AuditLog>();

    public DbSet<Supplier>             Suppliers            { get; set; }
    public DbSet<PurchaseInvoice>      PurchaseInvoices     { get; set; }
    public DbSet<PurchaseInvoiceItem>  PurchaseInvoiceItems { get; set; }
    public DbSet<SupplierPayment>      SupplierPayments     { get; set; }
    public DbSet<PurchaseReturn>       PurchaseReturns      { get; set; }
    public DbSet<PurchaseReturnItem>   PurchaseReturnItems { get; set; }
    public DbSet<ProductUnit>          ProductUnits         { get; set; }

    public DbSet<Account>        Accounts        { get; set; }
    public DbSet<AccountSystemMapping> AccountSystemMappings { get; set; }
    public DbSet<JournalEntry>   JournalEntries  { get; set; }
    public DbSet<JournalLine>    JournalLines    { get; set; }
    public DbSet<ReceiptVoucher> ReceiptVouchers { get; set; }
    public DbSet<PaymentVoucher> PaymentVouchers { get; set; }

    public DbSet<InventoryAudit>     InventoryAudits     { get; set; }
    public DbSet<InventoryAuditItem> InventoryAuditItems => Set<InventoryAuditItem>();
    public DbSet<InventoryMovement>  InventoryMovements  => Set<InventoryMovement>();
    public DbSet<InventoryOpeningBalance> InventoryOpeningBalances { get; set; }
    public DbSet<InventoryOpeningBalanceItem> InventoryOpeningBalanceItems { get; set; }

    public DbSet<CustomerInstallment>  CustomerInstallments  { get; set; }
    public DbSet<InstallmentPayment>   InstallmentPayments   { get; set; }

    public DbSet<ProductDiscount>      ProductDiscounts      { get; set; }

    public DbSet<Department>           Departments           { get; set; }

    // ── الموارد البشرية والرواتب ──────────────────────────
    public DbSet<Employee>           Employees           { get; set; }
    public DbSet<PayrollRun>         PayrollRuns         { get; set; }
    public DbSet<PayrollItem>        PayrollItems        { get; set; }
    public DbSet<EmployeeAdvance>    EmployeeAdvances    { get; set; }
    public DbSet<EmployeeBonus>      EmployeeBonuses     { get; set; }
    public DbSet<EmployeeDeduction>  EmployeeDeductions  { get; set; }

    // ── الأصول الثابتة ────────────────────────────────────
    public DbSet<FixedAssetCategory> FixedAssetCategories { get; set; }
    public DbSet<FixedAsset>         FixedAssets          { get; set; }
    public DbSet<AssetDepreciation>  AssetDepreciations   { get; set; }
    public DbSet<AssetDisposal>      AssetDisposals       { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(e => {
            e.HasIndex(u => u.PhoneNumber);
        });

        builder.Entity<Customer>(e => {
            e.HasIndex(c => c.Phone);
        });

        builder.Entity<Category>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
            // Self-referencing hierarchy — قسم رئيسي → أقسام فرعية (أي عمق)
            e.HasOne(x => x.Parent)
             .WithMany(x => x.Children)
             .HasForeignKey(x => x.ParentId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Brand>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
            // Self-referencing hierarchy
            e.HasOne(x => x.Parent)
             .WithMany(x => x.SubBrands)
             .HasForeignKey(x => x.ParentId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Product>(e => {
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.DiscountPrice).HasPrecision(18, 2);
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
            e.Property(x => x.VatRate).HasPrecision(18, 2);
            e.Property(x => x.SKU).HasMaxLength(50);
            e.HasIndex(x => x.SKU).IsUnique();
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Brand).WithMany(b => b.Products)
             .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Status).HasConversion<string>();
        });

        builder.Entity<ProductVariant>(e => {
            e.Property(x => x.PriceAdjustment).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany(p => p.Variants)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductImage>(e => {
            e.HasOne(x => x.Product).WithMany(p => p.Images)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Review>(e => {
            e.HasOne(x => x.Product).WithMany(p => p.Reviews)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Customer>(e => {
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.TotalSales).HasPrecision(18, 2);
            e.Property(x => x.TotalPaid).HasPrecision(18, 2);
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
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.OrderNumber).IsUnique();
            e.HasOne(x => x.Customer).WithMany(c => c.Orders)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeliveryAddress).WithMany()
             .HasForeignKey(x => x.DeliveryAddressId).OnDelete(DeleteBehavior.SetNull);
            // String representation for enums in the database
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.FulfillmentType).HasConversion<string>();
            e.Property(x => x.PaymentMethod).HasConversion<string>();
            e.Property(x => x.PaymentStatus).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.HasIndex(x => x.CreatedAt); // For reports
        });

        builder.Entity<OrderPayment>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Method).HasConversion<string>();
            e.HasOne(x => x.Order).WithMany(o => o.Payments)
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<OrderItem>(e => {
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);
            e.Property(x => x.VatRateApplied).HasPrecision(18, 2);
            e.Property(x => x.ItemVatAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany(o => o.Items)
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(p => p.OrderItems)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<OrderStatusHistory>(e => {
            e.Property(x => x.Status).HasConversion<string>();
        });

        builder.Entity<CartItem>(e => {
            e.HasOne(x => x.Customer).WithMany(c => c.CartItems)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(p => p.CartItems)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

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
            e.HasIndex(x => x.InvoiceDate); // For reports
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
        
        builder.Entity<PurchaseReturn>(e => {
            e.Property(r => r.SubTotal).HasPrecision(18, 2);
            e.Property(r => r.TaxAmount).HasPrecision(18, 2);
            e.Property(r => r.DiscountAmount).HasPrecision(18, 2);
            e.Property(r => r.TotalAmount).HasPrecision(18, 2);
            e.HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.PurchaseInvoiceId).IsRequired(false);
            e.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).IsRequired();
        });

        builder.Entity<PurchaseReturnItem>(e => {
            e.Property(ri => ri.UnitCost).HasPrecision(18, 2);
            e.Property(ri => ri.TotalCost).HasPrecision(18, 2);
            e.HasOne(ri => ri.PurchaseReturn).WithMany(r => r.Items).HasForeignKey(ri => ri.PurchaseReturnId).IsRequired();
            e.HasOne(ri => ri.InvoiceItem).WithMany().HasForeignKey(ri => ri.PurchaseInvoiceItemId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
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
            e.HasIndex(x => x.EntryDate); // For reports
        });

        builder.Entity<StoreInfo>(e => {
            e.Property(x => x.VatRatePercent).HasPrecision(18, 2);
            e.Property(x => x.FixedDeliveryFee).HasPrecision(18, 2);
            e.Property(x => x.FreeDeliveryAt).HasPrecision(18, 2);
            e.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
        });

        builder.Entity<ShippingZone>(e => {
            e.Property(x => x.Fee).HasPrecision(18, 2);
            e.Property(x => x.FreeThreshold).HasPrecision(18, 2);
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

        builder.Entity<InventoryMovement>(e => {
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Type).HasConversion<string>();
        });

        builder.Entity<InventoryAudit>(e => {
            e.Property(x => x.TotalExpectedValue).HasPrecision(18, 2);
            e.Property(x => x.TotalActualValue).HasPrecision(18, 2);
            // Default enum mapping to int is fine here as it matches the migration designer
            e.HasMany(a => a.Items).WithOne(i => i.InventoryAudit).HasForeignKey(i => i.InventoryAuditId);
        });

        builder.Entity<InventoryAuditItem>(e => {
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InventoryOpeningBalance>(e => {
            e.Property(x => x.TotalValue).HasPrecision(18, 2);
            e.HasMany(x => x.Items).WithOne(x => x.InventoryOpeningBalance).HasForeignKey(x => x.InventoryOpeningBalanceId);
        });

        builder.Entity<InventoryOpeningBalanceItem>(e => {
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
        });

        // ── JournalLine — EmployeeId ──────────────────────────
        builder.Entity<JournalLine>(e => {
            e.HasOne(l => l.Employee).WithMany()
             .HasForeignKey(l => l.EmployeeId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── الموارد البشرية والرواتب ──────────────────────────
        builder.Entity<Employee>(e => {
            e.Property(x => x.BaseSalary).HasPrecision(18, 2);
            e.Property(x => x.FixedAllowance).HasPrecision(18, 2);
            e.Property(x => x.FixedDeduction).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasOne(x => x.Account).WithMany()
             .HasForeignKey(x => x.AccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Department).WithMany(d => d.Employees)
             .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            // ربط اختياري بحساب النظام — SetNull عند حذف المستخدم لحفظ سجل الـ HR
            e.HasOne(x => x.AppUser).WithMany()
             .HasForeignKey(x => x.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.AppUserId).IsUnique();
        });

        builder.Entity<PayrollRun>(e => {
            e.Property(x => x.TotalBasicSalary).HasPrecision(18, 2);
            e.Property(x => x.TotalBonuses).HasPrecision(18, 2);
            e.Property(x => x.TotalDeductions).HasPrecision(18, 2);
            e.Property(x => x.TotalAdvancesDeducted).HasPrecision(18, 2);
            e.Property(x => x.TotalNetPayable).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.PayrollNumber).IsUnique();
            e.HasOne(x => x.JournalEntry).WithMany()
             .HasForeignKey(x => x.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WagesExpenseAccount).WithMany()
             .HasForeignKey(x => x.WagesExpenseAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AccruedSalariesAccount).WithMany()
             .HasForeignKey(x => x.AccruedSalariesAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DeductionRevenueAccount).WithMany()
             .HasForeignKey(x => x.DeductionRevenueAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AdvancesAccount).WithMany()
             .HasForeignKey(x => x.AdvancesAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PayrollItem>(e => {
            e.Property(x => x.BasicSalary).HasPrecision(18, 2);
            e.Property(x => x.BonusAmount).HasPrecision(18, 2);
            e.Property(x => x.DeductionAmount).HasPrecision(18, 2);
            e.Property(x => x.AdvanceDeducted).HasPrecision(18, 2);
            e.HasOne(x => x.PayrollRun).WithMany(p => p.Items)
             .HasForeignKey(x => x.PayrollRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Employee).WithMany(emp => emp.PayrollItems)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmployeeAdvance>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.DeductedAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.AdvanceNumber).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Advances)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CashAccount).WithMany()
             .HasForeignKey(x => x.CashAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.JournalEntry).WithMany()
             .HasForeignKey(x => x.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<EmployeeBonus>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BonusType).HasConversion<string>();
            e.HasIndex(x => x.BonusNumber).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Bonuses)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PayrollRun).WithMany()
             .HasForeignKey(x => x.PayrollRunId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CashAccount).WithMany()
             .HasForeignKey(x => x.CashAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.JournalEntry).WithMany()
             .HasForeignKey(x => x.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<EmployeeDeduction>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.DeductionType).HasConversion<string>();
            e.HasIndex(x => x.DeductionNumber).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Deductions)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PayrollRun).WithMany()
             .HasForeignKey(x => x.PayrollRunId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CashAccount).WithMany()
             .HasForeignKey(x => x.CashAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.JournalEntry).WithMany()
             .HasForeignKey(x => x.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── الأصول الثابتة ────────────────────────────────────
        builder.Entity<FixedAssetCategory>(e => {
            e.HasMany(c => c.Assets).WithOne(a => a.Category)
             .HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.AssetAccount).WithMany()
             .HasForeignKey(c => c.AssetAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.AccumDepreciationAccount).WithMany()
             .HasForeignKey(c => c.AccumDepreciationAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.DepreciationExpenseAccount).WithMany()
             .HasForeignKey(c => c.DepreciationExpenseAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<FixedAsset>(e => {
            e.Property(a => a.PurchaseCost).HasPrecision(18, 2);
            e.Property(a => a.SalvageValue).HasPrecision(18, 2);
            e.Property(a => a.AccumulatedDepreciation).HasPrecision(18, 2);
            e.Property(a => a.DepreciationMethod).HasConversion<string>();
            e.Property(a => a.Status).HasConversion<string>();
            e.HasIndex(a => a.AssetNumber).IsUnique();
            e.HasOne(a => a.PurchaseInvoice).WithMany()
             .HasForeignKey(a => a.PurchaseInvoiceId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.AssetAccount).WithMany()
             .HasForeignKey(a => a.AssetAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.AccumDepreciationAccount).WithMany()
             .HasForeignKey(a => a.AccumDepreciationAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(a => a.DepreciationExpenseAccount).WithMany()
             .HasForeignKey(a => a.DepreciationExpenseAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AssetDepreciation>(e => {
            e.Property(d => d.DepreciationAmount).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedBefore).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedAfter).HasPrecision(18, 2);
            e.Property(d => d.BookValueAfter).HasPrecision(18, 2);
            e.HasOne(d => d.FixedAsset).WithMany(a => a.Depreciations)
             .HasForeignKey(d => d.FixedAssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.JournalEntry).WithMany()
             .HasForeignKey(d => d.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => d.DepreciationNumber).IsUnique();
        });

        builder.Entity<AssetDisposal>(e => {
            e.Property(d => d.BookValueAtDisposal).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedAtDisposal).HasPrecision(18, 2);
            e.Property(d => d.SaleProceeds).HasPrecision(18, 2);
            e.Property(d => d.DisposalType).HasConversion<string>();
            e.HasOne(d => d.FixedAsset).WithMany(a => a.Disposals)
             .HasForeignKey(d => d.FixedAssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.JournalEntry).WithMany()
             .HasForeignKey(d => d.JournalEntryId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.ProceedsAccount).WithMany()
             .HasForeignKey(d => d.ProceedsAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.GainAccount).WithMany()
             .HasForeignKey(d => d.GainAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.LossAccount).WithMany()
             .HasForeignKey(d => d.LossAccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => d.DisposalNumber).IsUnique();
        });

        builder.Entity<StoreInfo>().HasData(
            new StoreInfo { 
                StoreConfigId = 1, 
                StoreBrandName = "Sportive", 
                StoreEmailAddr = "contact@sportive-sportwear.com",
                LastUpdateDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) 
            }
        );
    }
}
