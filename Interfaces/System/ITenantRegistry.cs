using System.Threading.Tasks;
using Sportive.API.Models;

namespace Sportive.API.Interfaces;

public interface ITenantRegistry
{
    Task<Tenant?> GetTenantBySlugAsync(string slug);
    Task<Tenant?> GetTenantBySubdomainAsync(string subdomain);
}
