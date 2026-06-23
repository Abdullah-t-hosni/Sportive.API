using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;

namespace Sportive.API.Data;

public class MasterDbContext : DbContext
{
    public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantUsage> TenantUsages => Set<TenantUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Slug).IsUnique();
            entity.HasIndex(t => t.Subdomain).IsUnique();
            entity.HasIndex(t => t.DatabaseName).IsUnique();
        });

        // Relationships
        modelBuilder.Entity<TenantSubscription>()
            .HasOne(ts => ts.Tenant)
            .WithMany()
            .HasForeignKey(ts => ts.TenantGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenantSubscription>()
            .HasOne(ts => ts.Plan)
            .WithMany()
            .HasForeignKey(ts => ts.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenantUsage>()
            .HasOne(tu => tu.Tenant)
            .WithMany()
            .HasForeignKey(tu => tu.TenantGuid)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed Data
        modelBuilder.Entity<Plan>().HasData(
            new Plan
            {
                Id = 1,
                Name = "Trial",
                Description = "Free trial plan",
                MaxUsers = 3,
                MaxBranches = 1,
                MaxStorageGB = 1,
                MonthlyPrice = 0,
                YearlyPrice = 0,
                IsActive = true,
                DisplayOrder = 1,
                IsFeatured = false
            },
            new Plan
            {
                Id = 2,
                Name = "Basic",
                Description = "Essential features for small teams",
                MaxUsers = 10,
                MaxBranches = 2,
                MaxStorageGB = 10,
                MonthlyPrice = 29,
                YearlyPrice = 290,
                IsActive = true,
                DisplayOrder = 2,
                IsFeatured = false
            },
            new Plan
            {
                Id = 3,
                Name = "Pro",
                Description = "Advanced features for growing businesses",
                MaxUsers = 50,
                MaxBranches = 10,
                MaxStorageGB = 100,
                MonthlyPrice = 99,
                YearlyPrice = 990,
                IsActive = true,
                DisplayOrder = 3,
                IsFeatured = true
            },
            new Plan
            {
                Id = 4,
                Name = "Enterprise",
                Description = "Unlimited capabilities for large organizations",
                MaxUsers = -1,
                MaxBranches = -1,
                MaxStorageGB = -1,
                MonthlyPrice = 299,
                YearlyPrice = 2990,
                IsActive = true,
                DisplayOrder = 4,
                IsFeatured = false
            }
        );
    }
}
