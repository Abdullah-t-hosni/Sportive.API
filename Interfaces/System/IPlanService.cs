using System.Collections.Generic;
using System.Threading.Tasks;
using Sportive.API.DTOs.System;

namespace Sportive.API.Interfaces;

public interface IPlanService
{
    Task<IEnumerable<PlanDto>> GetAllPlansAsync(bool includeInactive = false);
    Task<PlanDto?> GetPlanByIdAsync(int id);
    Task<(bool Success, string Message, PlanDto? Data)> CreatePlanAsync(CreatePlanDto request);
    Task<(bool Success, string Message, PlanDto? Data)> UpdatePlanAsync(int id, UpdatePlanDto request);
    Task<(bool Success, string Message)> DeactivatePlanAsync(int id);
}
