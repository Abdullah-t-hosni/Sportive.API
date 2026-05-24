namespace Sportive.API.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// Returns true if the user can view at least one of the given modules.
    /// Bypasses check for Admin/Manager roles.
    /// </summary>
    Task<bool> CanViewAsync(string userId, IEnumerable<string> moduleKeys);

    /// <summary>
    /// Returns true if the user can edit at least one of the given modules.
    /// </summary>
    Task<bool> CanEditAsync(string userId, IEnumerable<string> moduleKeys);

    /// <summary>
    /// Returns true if the user has POS view access.
    /// Used by the [AllowPosAccess] bypass mechanism.
    /// </summary>
    Task<bool> HasPosAccessAsync(string userId);

    /// <summary>
    /// Invalidates the cached permissions for the given user.
    /// Call this whenever roles or module permissions change.
    /// </summary>
    Task InvalidateCacheAsync(string userId);
}
