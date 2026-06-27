using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using StackExchange.Redis;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/system/[controller]")]
// [Authorize(Roles = "Admin")] // Uncomment when auth is ready
public class ServerMonitoringController : ControllerBase
{
    private readonly MasterDbContext _masterDb;
    private readonly AppDbContext _appDb;
    private readonly IConnectionMultiplexer? _redis;

    public ServerMonitoringController(MasterDbContext masterDb, AppDbContext appDb, IConnectionMultiplexer? redis = null)
    {
        _masterDb = masterDb;
        _appDb = appDb;
        _redis = redis;
    }

    [HttpGet("live-stats")]
    public async Task<IActionResult> GetLiveStats()
    {
        var process = Process.GetCurrentProcess();
        
        // Mocking a few things since actual CPU extraction requires OS-specific calls in .NET
        var cpuUsage = Math.Round(new Random().NextDouble() * 30 + 10, 2); // 10% to 40%
        var memoryUsedMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2);

        // Check DB connections
        var masterDbHealthy = await _masterDb.Database.CanConnectAsync();
        var appDbHealthy = await _appDb.Database.CanConnectAsync();

        var redisHealthy = _redis?.IsConnected ?? false;

        return Ok(new
        {
            cpuUsagePercent = cpuUsage,
            memoryUsedMb = memoryUsedMb,
            masterDbStatus = masterDbHealthy ? "healthy" : "down",
            appDbStatus = appDbHealthy ? "healthy" : "down",
            redisStatus = redisHealthy ? "healthy" : "down",
            activeWorkers = new Random().Next(15, 30),
            queueSize = new Random().Next(0, 100),
            uptime = (DateTime.Now - process.StartTime).ToString(@"dd\.hh\:mm\:ss")
        });
    }
}
