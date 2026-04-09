namespace Sportive.API.Utils;

public static class AppConstants
{
    public const int MaxPageSize        = 100;
    /// <summary>
    /// Larger cap used only for internal precache / offline-sync requests.
    /// The SW sends pageSize=500 to fetch all products/customers in one shot.
    /// </summary>
    public const int MaxPrecacheSize    = 500;
    public const int DefaultPageSize    = 20;

    public static int ClampPageSize(int requested) =>
        Math.Clamp(requested, 1, MaxPageSize);

    /// <summary>Use for endpoints that the Service Worker precaches (products, customers).</summary>
    public static int ClampPrecacheSize(int requested) =>
        Math.Clamp(requested, 1, MaxPrecacheSize);
}
