using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Services.HR;
using Sportive.API.Utils;

namespace Sportive.API.Controllers.HR
{
    [ApiController]
    [Route("iclock")]
    [AllowAnonymous] // ZK devices do not send authentication headers
    public class IclockController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ZKDeviceService _zkService;
        private readonly ILogger<IclockController> _logger;

        public IclockController(AppDbContext db, ZKDeviceService zkService, ILogger<IclockController> logger)
        {
            _db = db;
            _zkService = zkService;
            _logger = logger;
        }

        /// <summary>
        /// Handshake endpoint for ZKTeco ADMS devices to query server specifications and configuration.
        /// </summary>
        [HttpGet("cdata")]
        public async Task<IActionResult> GetCData([FromQuery] string SN)
        {
            if (string.IsNullOrWhiteSpace(SN))
            {
                return BadRequest("Missing device Serial Number (SN).");
            }

            var device = await _db.ZkDevices.FirstOrDefaultAsync(d => d.SerialNumber == SN);
            if (device == null)
            {
                _logger.LogWarning("Unregistered ZK device handshake attempt with SN: {SN}", SN);
                return BadRequest("Device not registered.");
            }

            device.LastActive = TimeHelper.GetEgyptTime();
            await _db.SaveChangesAsync();

            // Return standard ZK ADMS specs
            var response = $"GET OPTION FROM: {SN}\n" +
                           "Stamp=9999\n" +
                           "OpStamp=0\n" +
                           "ErrorDelay=30\n" +
                           "Delay=10\n" +
                           "TransInterval=10\n" +
                           "Realtime=1\n";

            return Content(response, "text/plain");
        }

        /// <summary>
        /// Receives raw data logs from ZKTeco devices (e.g. check-ins, check-outs).
        /// </summary>
        [HttpPost("cdata")]
        public async Task<IActionResult> PostCData([FromQuery] string SN, [FromQuery] string? table)
        {
            if (string.IsNullOrWhiteSpace(SN))
            {
                return BadRequest("Missing Serial Number (SN).");
            }

            var device = await _db.ZkDevices.FirstOrDefaultAsync(d => d.SerialNumber == SN);
            if (device == null)
            {
                _logger.LogWarning("Unregistered ZK device post-data attempt with SN: {SN}", SN);
                return BadRequest("Device not registered.");
            }

            device.LastActive = TimeHelper.GetEgyptTime();
            await _db.SaveChangesAsync();

            // We only process ATTLOG (Attendance Logs)
            if (table != "ATTLOG")
            {
                return Content("OK\n", "text/plain");
            }

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            var lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                try
                {
                    // ZKTeco ATTLOG format: PIN \t Timestamp \t State \t VerifyMethod ...
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var pin = parts[0].Trim();
                        var timeStr = parts[1].Trim();

                        // If date and time are split by space (e.g. Parts[1] = "2026-05-31", parts[2] = "09:00:00")
                        if (parts.Length >= 3 && parts[1].Contains("-") && parts[2].Contains(":"))
                        {
                            timeStr = $"{parts[1]} {parts[2]}";
                        }

                        if (DateTime.TryParse(timeStr, out var timestamp))
                        {
                            await _zkService.ProcessPunchAsync(pin, timestamp, SN);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to parse time '{TimeStr}' in punch line: {Line} from SN: {SN}", timeStr, line, SN);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing punch line: {Line} from device SN: {SN}", line, SN);
                }
            }

            return Content("OK\n", "text/plain");
        }

        /// <summary>
        /// Command polling endpoint. Machines hit this periodically to check for commands.
        /// </summary>
        [HttpGet("getrequest")]
        public async Task<IActionResult> GetRequest([FromQuery] string SN)
        {
            if (string.IsNullOrWhiteSpace(SN)) return BadRequest();

            var device = await _db.ZkDevices.FirstOrDefaultAsync(d => d.SerialNumber == SN);
            if (device != null)
            {
                device.LastActive = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();
            }

            return Content("OK\n", "text/plain");
        }

        /// <summary>
        /// Command status update endpoint. Matches command feedback from the machine.
        /// </summary>
        [HttpPost("devicecmd")]
        public async Task<IActionResult> PostDeviceCmd([FromQuery] string SN)
        {
            if (string.IsNullOrWhiteSpace(SN)) return BadRequest();

            var device = await _db.ZkDevices.FirstOrDefaultAsync(d => d.SerialNumber == SN);
            if (device != null)
            {
                device.LastActive = TimeHelper.GetEgyptTime();
                await _db.SaveChangesAsync();
            }

            return Content("OK\n", "text/plain");
        }
    }
}
