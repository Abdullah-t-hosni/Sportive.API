using Sportive.API.Interfaces;
using Sportive.API.Models;

namespace Sportive.API.Services;

public class TenantContext : ITenantContext
{
    public Tenant? CurrentTenant { get; private set; }

    public void SetTenant(Tenant tenant)
    {
        CurrentTenant = tenant;
    }
}
