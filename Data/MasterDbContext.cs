using Microsoft.EntityFrameworkCore;
using Sportive.API.Models;

namespace Sportive.API.Data;

public class MasterDbContext : DbContext
{
    public MasterDbContext(DbContextOptions<MasterDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasIndex(t => t.Slug).IsUnique();
            entity.HasIndex(t => t.Subdomain).IsUnique();
            entity.HasIndex(t => t.DatabaseName).IsUnique();
        });
    }
}
