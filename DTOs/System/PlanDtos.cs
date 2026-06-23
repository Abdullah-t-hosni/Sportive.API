using System.ComponentModel.DataAnnotations;

namespace Sportive.API.DTOs.System;

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MaxUsers { get; set; }
    public int MaxBranches { get; set; }
    public int MaxStorageGB { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsFeatured { get; set; }
}

public class CreatePlanDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    public int MaxUsers { get; set; }

    [Required]
    public int MaxBranches { get; set; }

    [Required]
    public int MaxStorageGB { get; set; }

    [Required]
    public decimal MonthlyPrice { get; set; }

    [Required]
    public decimal YearlyPrice { get; set; }

    public int DisplayOrder { get; set; } = 0;

    public bool IsFeatured { get; set; } = false;
}

public class UpdatePlanDto
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int? MaxUsers { get; set; }

    public int? MaxBranches { get; set; }

    public int? MaxStorageGB { get; set; }

    public decimal? MonthlyPrice { get; set; }

    public decimal? YearlyPrice { get; set; }

    public int? DisplayOrder { get; set; }

    public bool? IsFeatured { get; set; }
}
