namespace Sportive.API.Utils;

public static class AppConstants
{
    public const int MaxPageSize     = 100;
    public const int DefaultPageSize = 20;

    public static int ClampPageSize(int requested) =>
        Math.Clamp(requested, 1, MaxPageSize);
}
