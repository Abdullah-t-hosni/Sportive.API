using System;
using Sportive.API.Utils;

namespace Sportive.API.Models;

public enum WelcomeMessageTargetType
{
    User = 1,
    Department = 2,
    All = 3,
    Staff = 4,
    Customers = 5
}

public class WelcomeMessage : BaseEntity
{
    public string Message { get; set; } = string.Empty;
    public WelcomeMessageTargetType TargetType { get; set; }
    
    public string? TargetUserId { get; set; }
    public AppUser? TargetUser { get; set; }
    
    public int? TargetDepartmentId { get; set; }
    public Department? TargetDepartment { get; set; }
    
    public bool IsActive { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    
    public string? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
}

public class WelcomeMessageSeen : BaseEntity
{
    public int WelcomeMessageId { get; set; }
    public WelcomeMessage WelcomeMessage { get; set; } = null!;
    
    public string UserId { get; set; } = string.Empty;
    public AppUser User { get; set; } = null!;
    
    public DateTime SeenAt { get; set; } = TimeHelper.GetEgyptTime();
}
