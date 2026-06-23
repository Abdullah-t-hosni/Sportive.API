using Hangfire.Client;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Sportive.API.Data;
using Sportive.API.Services;

namespace Sportive.API.Filters;

public class TenantJobFilter : IClientFilter, IServerFilter
{
    private const string TenantSlugKey = "TenantSlug";
    private readonly IServiceProvider _rootServiceProvider;

    public TenantJobFilter(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider;
    }

    public void OnCreating(CreatingContext filterContext)
    {
        // Capture the tenant from the enqueuing thread (e.g., HTTP request)
        var currentTenant = new TenantContext().CurrentTenant;
        if (currentTenant != null)
        {
            filterContext.SetJobParameter(TenantSlugKey, currentTenant.Slug);
        }
    }

    public void OnCreated(CreatedContext filterContext) { }

    public void OnPerforming(PerformingContext filterContext)
    {
        // Restore the tenant on the Hangfire background worker thread
        var tenantSlug = filterContext.GetJobParameter<string>(TenantSlugKey);
        
        // Always clear the AsyncLocal at the start of a background job to prevent bleed from thread pool reuse
        new TenantContext().SetTenant(null!);

        if (!string.IsNullOrEmpty(tenantSlug))
        {
            // We must create a scope from the root provider to safely resolve MasterDbContext
            using var scope = _rootServiceProvider.CreateScope();
            var masterDb = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            
            // Look up the tenant
            var tenant = masterDb.Tenants.FirstOrDefault(t => t.Slug == tenantSlug);
            if (tenant != null)
            {
                // Inject the tenant into the AsyncLocal, so any services resolved in this job inherit it
                new TenantContext().SetTenant(tenant);
            }
        }
    }

    public void OnPerformed(PerformedContext filterContext) 
    {
        // Clean up
        new TenantContext().SetTenant(null!);
    }
}
