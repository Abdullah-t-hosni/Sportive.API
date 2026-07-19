// ============================================================
// Data/AppDbContext.cs — تم إضافة AuditLogs DbSet وتحديث التصنيفات والمقاسات
// ============================================================
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;
using Sportive.API.Services;
using Sportive.API.Interfaces;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Security.Claims;

namespace Sportive.API.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ITenantContext? _tenantContext;
    public bool BypassAuditLogging { get; set; } = false;

    public AppDbContext(
        DbContextOptions<AppDbContext> options, 
        IHttpContextAccessor? httpContextAccessor = null,
        IServiceScopeFactory? scopeFactory = null,
        ITenantContext? tenantContext = null) : base(options) 
    { 
        _httpContextAccessor = httpContextAccessor;
        _scopeFactory = scopeFactory;
        _tenantContext = tenantContext;
    }

    public DbSet<Category> Categories            => Set<Category>();
    public DbSet<Brand> Brands                  => Set<Brand>();
    public DbSet<Product> Products               => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages     => Set<ProductImage>();
    public DbSet<Review> Reviews                 => Set<Review>();
    public DbSet<ReviewToken> ReviewTokens       => Set<ReviewToken>();
    public DbSet<Customer> Customers             => Set<Customer>();
    public DbSet<CustomerCategory> CustomerCategories => Set<CustomerCategory>();
    public DbSet<Address> Addresses              => Set<Address>();
    public DbSet<Order> Orders                   => Set<Order>();
    public DbSet<OrderItem> OrderItems           => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<CartItem> CartItems             => Set<CartItem>();
    public DbSet<OrderPayment> OrderPayments     => Set<OrderPayment>();
    public DbSet<Coupon> Coupons                 => Set<Coupon>();
    public DbSet<WishlistItem> WishlistItems     => Set<WishlistItem>();
    public DbSet<Notification> Notifications     => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<BackupRecord> BackupRecords     => Set<BackupRecord>();
    public DbSet<UserModulePermission> UserModulePermissions => Set<UserModulePermission>();
    public DbSet<StoreInfo> StoreInfo           => Set<StoreInfo>();
    public DbSet<ShippingZone> ShippingZones    => Set<ShippingZone>();
    public DbSet<SizeGroup>    SizeGroups       => Set<SizeGroup>();
    public DbSet<SizeValue>    SizeValues       => Set<SizeValue>();
    public DbSet<ColorGroup>   ColorGroups      => Set<ColorGroup>();
    public DbSet<ColorValue>   ColorValues      => Set<ColorValue>();

    public DbSet<AuditLog> AuditLogs            => Set<AuditLog>();
    public DbSet<PosHeldCart> PosHeldCarts      => Set<PosHeldCart>();
    public DbSet<POSShiftClosure> POSShiftClosures => Set<POSShiftClosure>();
    public DbSet<UserSession> UserSessions      => Set<UserSession>();
    public DbSet<SecurityEvent> SecurityEvents    => Set<SecurityEvent>();
    public DbSet<EntityAttachment> EntityAttachments => Set<EntityAttachment>();

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
    public DbSet<SpecialOffer>         SpecialOffers         { get; set; }

    public DbSet<Department>           Departments           { get; set; }

    public DbSet<Employee>           Employees           { get; set; }

    public DbSet<Branch> Branches { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<ProductWarehouseStock> ProductWarehouseStocks { get; set; }
    public DbSet<StockTransfer> StockTransfers { get; set; }
    public DbSet<StockTransferItem> StockTransferItems { get; set; }
    public DbSet<PayrollRun>         PayrollRuns         { get; set; }
    public DbSet<PayrollItem>        PayrollItems        { get; set; }
    public DbSet<EmployeeAdvance>    EmployeeAdvances    { get; set; }
    public DbSet<EmployeeBonus>      EmployeeBonuses     { get; set; }
    public DbSet<EmployeeDeduction>  EmployeeDeductions  { get; set; }
    public DbSet<EmployeeAttendance> EmployeeAttendances  { get; set; }
    public DbSet<EmployeeShiftOverride> EmployeeShiftOverrides { get; set; }
    public DbSet<ZkDevice>           ZkDevices           { get; set; }
    
    // Employee Tasks and Responsibilities
    public DbSet<ResponsibilityType> ResponsibilityTypes  { get; set; }
    public DbSet<EmployeeTask> EmployeeTasks              { get; set; }
    public DbSet<EmployeeTaskItem> EmployeeTaskItems      { get; set; }
    public DbSet<TaskBlueprint> TaskBlueprints            { get; set; }

    public DbSet<EmployeeCommissionSetting> EmployeeCommissionSettings { get; set; }
    public DbSet<CommissionTier>     CommissionTiers     { get; set; }
    public DbSet<CommissionScheme>     CommissionSchemes     { get; set; }
    public DbSet<CommissionSchemeTier> CommissionSchemeTiers { get; set; }
    public DbSet<CommissionGroup>      CommissionGroups      { get; set; }
    public DbSet<CommissionGroupTier>  CommissionGroupTiers  { get; set; }

    public DbSet<FixedAssetCategory> FixedAssetCategories { get; set; }
    public DbSet<FixedAsset>         FixedAssets          { get; set; }
    public DbSet<AssetDepreciation>  AssetDepreciations   { get; set; }
    public DbSet<AssetDisposal>      AssetDisposals       { get; set; }
    public DbSet<DailyStat>          DailyStats           { get; set; }
    public DbSet<OutboxMessage>      OutboxMessages       { get; set; }
    public DbSet<DbSequence>        DbSequences          { get; set; }
    public DbSet<WelcomeMessage>     WelcomeMessages      { get; set; }
    public DbSet<WelcomeMessageSeen> WelcomeMessageSeens  { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DbSequence>(e => {
            e.HasIndex(x => new { x.Prefix, x.Stamp }).IsUnique();
        });

        builder.Entity<AppUser>(e => {
            e.HasIndex(u => u.PhoneNumber);
            e.HasOne(u => u.Branch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(u => u.Warehouse).WithMany().HasForeignKey(u => u.WarehouseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserSession>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.RefreshTokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SecurityEvent>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.IpAddress);
            e.HasIndex(x => x.CorrelationId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Customer>(e => {
            e.HasIndex(c => c.PhoneHash);
            e.HasIndex(c => c.EmailHash);
        });

        builder.Entity<Category>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
            e.HasOne(x => x.Parent)
             .WithMany(x => x.Children)
             .HasForeignKey(x => x.ParentId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SizeGroup).WithMany()
             .HasForeignKey(x => x.SizeGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ColorGroup).WithMany()
             .HasForeignKey(x => x.ColorGroupId).OnDelete(DeleteBehavior.SetNull);

        });

        builder.Entity<SizeGroup>(e => {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasMany(x => x.Values).WithOne(v => v.SizeGroup)
             .HasForeignKey(v => v.SizeGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SizeValue>(e => {
            e.Property(x => x.Value).HasMaxLength(50).IsRequired();
        });

        builder.Entity<ColorGroup>(e => {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasMany(x => x.Values).WithOne(v => v.ColorGroup)
             .HasForeignKey(v => v.ColorGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ColorValue>(e => {
            e.Property(x => x.Value).HasMaxLength(50).IsRequired();
        });

        builder.Entity<Brand>(e => {
            e.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
            e.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
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
            // ⚡ Performance: slug lookups on storefront (GetBySlug)
            e.HasIndex(x => x.Slug);
            // ⚡ Performance: status+stock filter used in dashboards
            e.HasIndex(x => new { x.Status, x.TotalStock });
            // ⚡ Performance: category and brand filters
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.BrandId);
            e.HasOne(x => x.Category).WithMany(c => c.Products)
             .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Brand).WithMany(b => b.Products)
             .HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.SizeGroup).WithMany()
             .HasForeignKey(x => x.SizeGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ColorGroup).WithMany()
             .HasForeignKey(x => x.ColorGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.LinkedProduct).WithMany()
             .HasForeignKey(x => x.LinkedProductId).OnDelete(DeleteBehavior.SetNull);

            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => x.CategoryId);
            e.HasIndex(x => x.BrandId);
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
            e.HasOne(x => x.Category).WithMany(c => c.Customers).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CustomerCategory>(e => {
            e.Property(x => x.DefaultDiscount).HasPrecision(18, 2);
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
            e.HasIndex(x => x.CustomerId);
            // ⚡ Performance: Accelerate reports filtering by branch and date
            e.HasIndex(x => new { x.BranchId, x.CreatedAt });
            e.HasOne(x => x.Customer).WithMany(c => c.Orders)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeliveryAddress).WithMany()
             .HasForeignKey(x => x.DeliveryAddressId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.FulfillmentType).HasConversion<string>();
            e.Property(x => x.PaymentMethod).HasConversion<string>();
            e.Property(x => x.PaymentStatus).HasConversion<string>();
            e.Property(x => x.Source).HasConversion<string>();
            e.HasIndex(x => x.CreatedAt);
            // ⚡ Performance: cover customer order history queries
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            // ⚡ Performance: covering index for dashboard/KPI (Status + Date + Amount)
            e.HasIndex(x => new { x.CreatedAt, x.Status, x.TotalAmount });
            e.HasIndex(x => x.SalesPersonId);
            e.HasIndex(x => x.IsArchived);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<OrderPayment>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Method).HasConversion<string>();
            e.HasOne(x => x.Order).WithMany(o => o.Payments)
             .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Reference);
            // ⚡ Performance: covering index for payment summaries
            e.HasIndex(x => new { x.CreatedAt, x.Method, x.Amount });
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
            
            // ⚡ Performance: critical for heavy reports (Sales by product/category)
            e.HasIndex(x => x.OrderId);
            e.HasIndex(x => x.ProductId);
        });
        builder.Entity<OrderStatusHistory>(e => {
            e.Property(x => x.Status).HasConversion<string>();
        });

        builder.Entity<CartItem>(e => {
            e.HasOne(x => x.Customer).WithMany(c => c.CartItems)
             .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany(p => p.CartItems)
             .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            // ⚡ Performance: every cart load filters by CustomerId
            e.HasIndex(x => x.CustomerId);
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

        builder.Entity<AuditLog>(e => {
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.EntityType);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            // 🔒 APPEND-ONLY: Audit logs must never be updated or deleted
            e.ToTable(tb => {
                tb.HasComment("Immutable audit trail — insert-only, never update/delete.");
            });
        });

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
            e.Property(i => i.CostCenter).HasConversion<string>();
            e.HasIndex(x => x.InvoiceDate);
            e.HasOne(i => i.Warehouse).WithMany().HasForeignKey(i => i.WarehouseId).OnDelete(DeleteBehavior.SetNull);
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
            e.Property(p => p.CostCenter).HasConversion<string>();
            e.HasIndex(p => p.ReferenceNumber);
        });
        
        builder.Entity<PurchaseReturn>(e => {
            e.Property(r => r.SubTotal).HasPrecision(18, 2);
            e.Property(r => r.TaxAmount).HasPrecision(18, 2);
            e.Property(r => r.DiscountAmount).HasPrecision(18, 2);
            e.Property(r => r.TotalAmount).HasPrecision(18, 2);
            e.HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.PurchaseInvoiceId).IsRequired(false);
            e.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).IsRequired();
            e.Property(r => r.CostCenter).HasConversion<string>();
            e.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasIndex(x => x.EntryDate);
            e.HasIndex(x => new { x.Status, x.EntryDate }); // ⚡ Performance: cover dashboard/report filters
            e.HasIndex(x => new { x.Reference, x.Type }).IsUnique();
        });

        builder.Entity<StoreInfo>(e => {
            e.Property(x => x.VatRatePercent).HasPrecision(18, 2);
            e.Property(x => x.FixedDeliveryFee).HasPrecision(18, 2);
            e.Property(x => x.FreeDeliveryAt).HasPrecision(18, 2);
            e.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
        });

        builder.Entity<SpecialOffer>(e => {
            e.Property(x => x.DiscountPercentage).HasPrecision(18, 2);
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
            e.HasOne(l => l.Employee).WithMany()
             .HasForeignKey(l => l.EmployeeId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            
            // ⚡ Performance: Critical for Statement and Aging reports
            e.HasIndex(l => l.CustomerId);
            e.HasIndex(l => l.SupplierId);
            e.HasIndex(l => l.AccountId);
            e.HasIndex(l => l.CostCenter);
            e.HasOne(l => l.Branch).WithMany().HasForeignKey(l => l.BranchId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ReceiptVoucher>(e => {
            e.Property(v => v.Amount).HasPrecision(18,2);
            e.HasOne(v => v.CashAccount).WithMany().HasForeignKey(v => v.CashAccountId);
            e.HasOne(v => v.FromAccount).WithMany().HasForeignKey(v => v.FromAccountId);
            e.HasOne(v => v.JournalEntry).WithMany().HasForeignKey(v => v.JournalEntryId).IsRequired(false);
            e.Property(v => v.PaymentMethod).HasConversion<string>();
            e.HasIndex(v => v.Reference);
            // ⚡ Performance: covering index for collection summaries
            e.HasIndex(v => new { v.VoucherDate, v.Amount });
            e.HasOne(v => v.Branch).WithMany().HasForeignKey(v => v.BranchId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PaymentVoucher>(e => {
            e.Property(v => v.Amount).HasPrecision(18,2);
            e.HasOne(v => v.CashAccount).WithMany().HasForeignKey(v => v.CashAccountId);
            e.HasOne(v => v.ToAccount).WithMany().HasForeignKey(v => v.ToAccountId);
            e.HasOne(v => v.JournalEntry).WithMany().HasForeignKey(v => v.JournalEntryId).IsRequired(false);
            e.Property(v => v.PaymentMethod).HasConversion<string>();
            e.HasIndex(v => v.Reference);
            // ⚡ Performance: covering index for expense summaries
            e.HasIndex(v => new { v.VoucherDate, v.Amount });
            e.HasOne(v => v.Branch).WithMany().HasForeignKey(v => v.BranchId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InventoryMovement>(e => {
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Type).HasConversion<string>();
            // ⚡ Performance: movement history queries filter by product + date
            e.HasIndex(x => new { x.ProductId, x.CreatedAt });
            e.HasIndex(x => x.ProductVariantId);
            // ⚡ Performance: reference-based lookup (order number)
            e.HasIndex(x => x.Reference);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InventoryAudit>(e => {
            e.Property(x => x.TotalExpectedValue).HasPrecision(18, 2);
            e.Property(x => x.TotalActualValue).HasPrecision(18, 2);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
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
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<InventoryOpeningBalanceItem>(e => {
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
        });

        builder.Entity<Employee>(e => {
            e.Property(x => x.BaseSalary).HasPrecision(18, 2);
            e.Property(x => x.FixedAllowance).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>();

            e.Property(x => x.AttendanceMode).HasConversion<string>();
            e.HasIndex(x => x.EmployeeNumber).IsUnique();
            e.HasOne(x => x.Account).WithMany()
             .HasForeignKey(x => x.AccountId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Department).WithMany(d => d.Employees)
             .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CommissionGroup).WithMany(g => g.Members)
             .HasForeignKey(x => x.CommissionGroupId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AppUser).WithMany()
             .HasForeignKey(x => x.AppUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.AppUserId).IsUnique();
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
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
        });

        builder.Entity<OutboxMessage>(e => {
            e.HasIndex(x => x.MessageId).IsUnique();
        });
        
        builder.Entity<DailyStat>(e => {
            e.HasKey(x => new { x.TenantId, x.Date, x.Source });
            e.Property(x => x.TotalSales).HasPrecision(18, 2);
            e.Property(x => x.TotalCollections).HasPrecision(18, 2);
            e.Property(x => x.TotalExpenses).HasPrecision(18, 2);
            e.Property(x => x.Profit).HasPrecision(18, 2);
        });
 
        builder.Entity<PayrollItem>(e => {
            e.Property(x => x.BasicSalary).HasPrecision(18, 2);
            e.Property(x => x.BonusAmount).HasPrecision(18, 2);
            e.Property(x => x.DeductionAmount).HasPrecision(18, 2);
            e.Property(x => x.AdvanceDeducted).HasPrecision(18, 2);
            e.Property(x => x.CommissionAmount).HasPrecision(18, 2);
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
        });

        builder.Entity<EmployeeBonus>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BonusType).HasConversion<string>();
            e.HasIndex(x => x.BonusNumber).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Bonuses)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmployeeDeduction>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.DeductionType).HasConversion<string>();
            e.HasIndex(x => x.DeductionNumber).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Deductions)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmployeeAttendance>(e => {
            e.Property(x => x.WorkHours).HasPrecision(18, 2);
            e.Property(x => x.OvertimeHours).HasPrecision(18, 2);
            e.Property(x => x.DelayMinutes).HasPrecision(18, 2);
            e.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
            e.HasOne(x => x.Employee).WithMany(emp => emp.Attendances)
             .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EmployeeCommissionSetting>(e => {
            e.Property(x => x.DefaultRate).HasPrecision(18, 2);
            e.Property(x => x.TargetAmount).HasPrecision(18, 2);
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Basis).HasConversion<string>();
            e.HasOne(x => x.Employee).WithOne(emp => emp.CommissionSetting)
             .HasForeignKey<EmployeeCommissionSetting>(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CommissionScheme).WithMany()
             .HasForeignKey(x => x.CommissionSchemeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CommissionTier>(e => {
            e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasOne(x => x.Setting).WithMany(s => s.Tiers)
             .HasForeignKey(x => x.SettingId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CommissionScheme>(e => {
            e.Property(x => x.DefaultRate).HasPrecision(18, 2);
            e.Property(x => x.TargetAmount).HasPrecision(18, 2);
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Basis).HasConversion<string>();
        });

        builder.Entity<CommissionSchemeTier>(e => {
            e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasOne(x => x.CommissionScheme).WithMany(s => s.Tiers)
             .HasForeignKey(x => x.CommissionSchemeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CommissionGroup>(e => {
            e.Property(x => x.DefaultRate).HasPrecision(18, 2);
            e.Property(x => x.TargetAmount).HasPrecision(18, 2);
            e.Property(x => x.Type).HasConversion<string>();
            e.Property(x => x.Basis).HasConversion<string>();
            e.HasOne(x => x.CommissionScheme).WithMany()
             .HasForeignKey(x => x.CommissionSchemeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<CommissionGroupTier>(e => {
            e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasOne(x => x.CommissionGroup).WithMany(s => s.Tiers)
             .HasForeignKey(x => x.CommissionGroupId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FixedAssetCategory>(e => {
            e.HasMany(c => c.Assets).WithOne(a => a.Category)
             .HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FixedAsset>(e => {
            e.Property(a => a.PurchaseCost).HasPrecision(18, 2);
            e.Property(a => a.SalvageValue).HasPrecision(18, 2);
            e.Property(a => a.AccumulatedDepreciation).HasPrecision(18, 2);
            e.Property(a => a.DepreciationMethod).HasConversion<string>();
            e.Property(a => a.Status).HasConversion<string>();
            e.HasIndex(a => a.AssetNumber).IsUnique();
        });

        builder.Entity<AssetDepreciation>(e => {
            e.Property(d => d.DepreciationAmount).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedBefore).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedAfter).HasPrecision(18, 2);
            e.Property(d => d.BookValueAfter).HasPrecision(18, 2);
            e.HasOne(d => d.FixedAsset).WithMany(a => a.Depreciations)
             .HasForeignKey(d => d.FixedAssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => d.DepreciationNumber).IsUnique();
        });

        builder.Entity<AssetDisposal>(e => {
            e.Property(d => d.BookValueAtDisposal).HasPrecision(18, 2);
            e.Property(d => d.AccumulatedAtDisposal).HasPrecision(18, 2);
            e.Property(d => d.SaleProceeds).HasPrecision(18, 2);
            e.Property(d => d.DisposalType).HasConversion<string>();
            e.HasOne(d => d.FixedAsset).WithMany(a => a.Disposals)
             .HasForeignKey(d => d.FixedAssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(d => d.DisposalNumber).IsUnique();
        });

        builder.Entity<Department>(e => {
            e.HasOne(d => d.ParentDepartment)
             .WithMany(d => d.SubDepartments)
             .HasForeignKey(d => d.ParentDepartmentId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(d => d.Manager)
             .WithMany()
             .HasForeignKey(d => d.ManagerEmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WelcomeMessage>(e => {
            e.Property(x => x.TargetType).HasConversion<string>();
            e.HasOne(x => x.TargetUser).WithMany()
             .HasForeignKey(x => x.TargetUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TargetDepartment).WithMany()
             .HasForeignKey(x => x.TargetDepartmentId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CreatedByUser).WithMany()
             .HasForeignKey(x => x.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<WelcomeMessageSeen>(e => {
            e.HasOne(x => x.WelcomeMessage).WithMany()
             .HasForeignKey(x => x.WelcomeMessageId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
             .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WelcomeMessageId, x.UserId }).IsUnique();
        });

        builder.Entity<ZkDevice>(e => {
            e.HasIndex(x => x.SerialNumber).IsUnique();
        });

        builder.Entity<Branch>(e => {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.PhoneNumber).HasMaxLength(50);
        });

        builder.Entity<Warehouse>(e => {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Location).HasMaxLength(500);
            e.HasOne(x => x.Branch).WithMany(b => b.Warehouses).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductWarehouseStock>(e => {
            e.HasIndex(x => new { x.ProductVariantId, x.WarehouseId }).IsUnique();
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StockTransfer>(e => {
            e.Property(x => x.TransferNumber).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.TransferNumber).IsUnique();
            e.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>();
        });

        builder.Entity<StockTransferItem>(e => {
            e.HasOne(x => x.StockTransfer).WithMany(t => t.Items).HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<POSShiftClosure>(e => {
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (BypassAuditLogging) return await base.SaveChangesAsync(cancellationToken);

        var auditEntries = OnBeforeSaveChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        OnAfterSaveChanges(auditEntries);
        return result;
    }

    public override int SaveChanges()
    {
        if (BypassAuditLogging) return base.SaveChanges();

        var auditEntries = OnBeforeSaveChanges();
        var result = base.SaveChanges();
        OnAfterSaveChanges(auditEntries);
        return result;
    }

    private List<AuditEntry> OnBeforeSaveChanges()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        var userId = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value;
        var ip = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();

        // Check if there is a manual action name passed in HttpContext items
        var contextAction = _httpContextAccessor?.HttpContext?.Items["AuditAction"]?.ToString();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.Entity is SecurityEvent || entry.Entity is UserSession || entry.Entity is DbSequence)
                continue; // Don't audit the audit logs or noisy tables

            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                continue;

            var entityType = entry.Entity.GetType().Name;
            var auditEntry = new AuditEntry(entry)
            {
                EntityType = entityType,
                Action = contextAction ?? entry.State.ToString(), // E.g. "Added", "Modified", "Deleted" or custom action
                UserId = userId,
                UserName = userName,
                IpAddress = ip
            };
            auditEntries.Add(auditEntry);

            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary)
                {
                    // value will be generated by the database, get the value after saving
                    auditEntry.TemporaryProperties.Add(property);
                    continue;
                }

                string propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey())
                {
                    auditEntry.EntityId = property.CurrentValue?.ToString();
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.NewValues[propertyName] = property.CurrentValue;
                        break;

                    case EntityState.Deleted:
                        auditEntry.OldValues[propertyName] = property.OriginalValue;
                        break;

                    case EntityState.Modified:
                        if (property.IsModified && (property.OriginalValue != null || property.CurrentValue != null) && !Equals(property.OriginalValue, property.CurrentValue))
                        {
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                        }
                        break;
                }
            }
        }

        // Keep only entries that actually have changes (for updates)
        return auditEntries.Where(a => a.HasTemporaryProperties || a.OldValues.Count > 0 || a.NewValues.Count > 0 || a.Entry.State == EntityState.Deleted || a.Entry.State == EntityState.Added).ToList();
    }

    private void OnAfterSaveChanges(List<AuditEntry> auditEntries)
    {
        if (auditEntries == null || auditEntries.Count == 0)
            return;

        var logs = new List<AuditLog>();
        foreach (var auditEntry in auditEntries)
        {
            // Get the final value of the temporary properties
            foreach (var prop in auditEntry.TemporaryProperties)
            {
                if (prop.Metadata.IsPrimaryKey())
                {
                    auditEntry.EntityId = prop.CurrentValue?.ToString();
                }
                else
                {
                    auditEntry.NewValues[prop.Metadata.Name] = prop.CurrentValue;
                }
            }

            var auditLog = auditEntry.ToDraftAuditLog();
            logs.Add(auditLog);
        }

        if (_scopeFactory != null && _tenantContext?.CurrentTenant != null)
        {
            AuditQueueProcessor.EnqueueAuditLogs(logs, _scopeFactory, _tenantContext.CurrentTenant);
        }
    }
}
