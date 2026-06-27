using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Hangfire;
using Sportive.API.Models;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
[Authorize(Roles = AppRoles.SuperAdmin)]
public class JobsController : ControllerBase
{
    /// <summary>GET /api/system/jobs/stats — إحصائيات المهام المجدولة (Hangfire)</summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var stats = monitoringApi.GetStatistics();

        return Ok(new
        {
            stats.Enqueued,
            stats.Failed,
            stats.Processing,
            stats.Scheduled,
            stats.Servers,
            stats.Succeeded,
            stats.Deleted,
            stats.Recurring
        });
    }

    /// <summary>GET /api/system/jobs/failed — المهام الفاشلة</summary>
    [HttpGet("failed")]
    public IActionResult GetFailedJobs([FromQuery] int count = 50)
    {
        var monitoringApi = JobStorage.Current.GetMonitoringApi();
        var failed = monitoringApi.FailedJobs(0, count);

        var result = failed.Select(f => new
        {
            Id = f.Key,
            f.Value.Job?.Type?.Name,
            f.Value.Job?.Method?.Name,
            f.Value.ExceptionType,
            f.Value.ExceptionMessage,
            f.Value.FailedAt
        }).ToList();

        return Ok(result);
    }
}
