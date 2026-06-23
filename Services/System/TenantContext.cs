using Sportive.API.Interfaces;
using Sportive.API.Models;
using System.Threading;

namespace Sportive.API.Services;

public class TenantContext : ITenantContext
{
    private static readonly AsyncLocal<Tenant?> _currentTenant = new();

    public Tenant? CurrentTenant 
    { 
        get => _currentTenant.Value; 
        private set => _currentTenant.Value = value; 
    }

    public void SetTenant(Tenant tenant)
    {
        CurrentTenant = tenant;
    }
}
