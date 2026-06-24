using System;
using System.Collections.Generic;
using Sportive.API.Models;

namespace Sportive.API.DTOs.System;

public class TenantQueryDto
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? PlanId { get; set; }
    public bool? IsTrial { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PagedResponseDto<T>
{
    public IEnumerable<T> Items { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

public class TenantListDto
{
    public Guid TenantGuid { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public bool IsLocked { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PlanName { get; set; }
    public bool IsTrial { get; set; }
}

public class TenantDetailsDto
{
    public Guid TenantGuid { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? CustomDomain { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string? PlanName { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public bool IsTrial { get; set; }
}

public class UpdateTenantDto
{
    public string? Name { get; set; }
    public string? Subdomain { get; set; }
    public string? CustomDomain { get; set; }
    public TenantStatus? Status { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
}

public class TenantUsageDto
{
    public int UsersCount { get; set; }
    public int BranchesCount { get; set; }
    public long StorageUsedBytes { get; set; }
    public int PlanLimitUsers { get; set; }
    public int PlanLimitBranches { get; set; }
    public long PlanLimitStorageBytes { get; set; }
}
