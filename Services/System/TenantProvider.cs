using System.Security.Claims;

namespace Sportive.API.Services
{
    public interface ITenantProvider
    {
        int GetTenantId();
    }

    public class TenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public int GetTenantId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return 1; // Default/System tenant

            var tenantClaim = user.FindFirst("TenantId")?.Value;
            if (int.TryParse(tenantClaim, out var tenantId))
            {
                return tenantId;
            }

            return 1; // Fallback
        }
    }
}
