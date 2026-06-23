using System.Threading.Tasks;
using Sportive.API.DTOs.System;

namespace Sportive.API.Interfaces;

public interface IAnalyticsService
{
    Task<SuperAdminDashboardStatsDto> GetDashboardStatsAsync();
}
