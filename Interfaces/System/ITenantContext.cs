using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface ITenantContext
{
    Tenant? CurrentTenant { get; }
    void SetTenant(Tenant tenant);
}
