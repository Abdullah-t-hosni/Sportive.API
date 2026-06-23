using System;
using System.ComponentModel.DataAnnotations;

namespace Sportive.API.Models;

public class OnboardTenantRequest
{
    [Required] 
    public string Name { get; set; } = string.Empty;
    
    [Required] 
    public string Slug { get; set; } = string.Empty;
    
    [Required] 
    public string Subdomain { get; set; } = string.Empty;
    
    [Required] 
    public string DatabaseName { get; set; } = string.Empty;
    
    [Required] 
    public string DatabaseUser { get; set; } = string.Empty;
    
    [Required] 
    public string DatabasePassword { get; set; } = string.Empty;
}

public class TenantOnboardingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid TenantGuid { get; set; }
    public string? AdminEmail { get; set; }
    public string? TemporaryPassword { get; set; }
}
