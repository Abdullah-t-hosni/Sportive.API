using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Data;
using Sportive.API.Services.HR;
using Sportive.API.Utils;
using System.Security.Claims;

namespace Sportive.API.Controllers.HR
{
    [ApiController]
    [Route("api/hr/self-service")]
    [Authorize]
    public class SelfServiceController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ZKDeviceService _zkService;
        private readonly ILogger<SelfServiceController> _logger;

        public SelfServiceController(AppDbContext db, ZKDeviceService zkService, ILogger<SelfServiceController> logger)
        {
            _db = db;
            _zkService = zkService;
            _logger = logger;
        }

        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        public class PunchRequestDto
        {
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
        }

        private static double GetDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            var r = 6371e3; // metres
            var φ1 = lat1 * Math.PI / 180;
            var φ2 = lat2 * Math.PI / 180;
            var Δφ = (lat2 - lat1) * Math.PI / 180;
            var Δλ = (lon2 - lon1) * Math.PI / 180;

            var a = Math.Sin(Δφ / 2) * Math.Sin(Δφ / 2) +
                    Math.Cos(φ1) * Math.Cos(φ2) *
                    Math.Sin(Δλ / 2) * Math.Sin(Δλ / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return r * c;
        }

        [HttpPost("punch")]
        public async Task<IActionResult> SelfPunch([FromBody] PunchRequestDto? request)
        {
            var employee = await _db.Employees.Include(e => e.Branch).FirstOrDefaultAsync(e => e.AppUserId == UserId);
            if (employee == null)
                return BadRequest(new { message = "لا يوجد ملف موظف مرتبط بحسابك." });

            if (string.IsNullOrEmpty(employee.EmployeeNumber))
                return BadRequest(new { message = "رقم الموظف غير مسجل. يرجى مراجعة الموارد البشرية." });

            // ── Validate Location and IP ──
            if (!employee.AllowRemotePunch && employee.Branch != null)
            {
                var branch = employee.Branch;

                // 1. IP Whitelisting Validation
                if (!string.IsNullOrWhiteSpace(branch.AllowedIpAddress))
                {
                    var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var allowedIps = branch.AllowedIpAddress.Split(',').Select(i => i.Trim());
                    if (clientIp == null || !allowedIps.Contains(clientIp))
                    {
                        return BadRequest(new { message = $"غير مسموح بتسجيل البصمة من هذه الشبكة. IP الخاص بك هو ({clientIp ?? "غير معروف"})." });
                    }
                }

                // 2. GPS Geolocation Validation
                if (branch.Latitude.HasValue && branch.Longitude.HasValue)
                {
                    if (request == null || !request.Latitude.HasValue || !request.Longitude.HasValue)
                        return BadRequest(new { message = "يجب تفعيل خدمة الموقع (GPS) في المتصفح لتسجيل البصمة." });

                    var distance = GetDistanceMeters(
                        request.Latitude.Value, request.Longitude.Value,
                        branch.Latitude.Value, branch.Longitude.Value);

                    if (distance > branch.AllowedPunchRadiusMeters)
                    {
                        return BadRequest(new { message = $"أنت بعيد عن مقر الفرع. مسموح بتسجيل البصمة داخل نطاق {branch.AllowedPunchRadiusMeters} متراً، ولكنك تبعد حالياً {Math.Round(distance)} متراً." });
                    }
                }
            }

            var now = TimeHelper.GetEgyptTime();
            
            try
            {
                await _zkService.ProcessPunchAsync(employee.EmployeeNumber, now, "بوابة الموظف (Web)");
                
                // Check their current status after punch
                var attendance = await _db.EmployeeAttendances
                    .Where(a => a.EmployeeId == employee.Id && a.Date == now.Date)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync();

                string statusMsg = "تم تسجيل بصمة بنجاح.";
                if (attendance != null)
                {
                    if (attendance.CheckOut.HasValue && attendance.CheckOut.Value == now)
                        statusMsg = "تم تسجيل الانصراف بنجاح.";
                    else if (attendance.CheckIn.HasValue && attendance.CheckIn.Value == now)
                        statusMsg = "تم تسجيل الحضور بنجاح.";
                }

                return Ok(new { success = true, message = statusMsg });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing self-punch for employee {Emp}", employee.EmployeeNumber);
                return StatusCode(500, new { message = "حدث خطأ أثناء تسجيل البصمة." });
            }
        }
        
        [HttpGet("punch-status")]
        public async Task<IActionResult> GetPunchStatus()
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == UserId);
            if (employee == null) return Ok(new { canPunch = false });

            var now = TimeHelper.GetEgyptTime();
            var date = now.Hour < 2 ? now.Date.AddDays(-1) : now.Date;

            var attendance = await _db.EmployeeAttendances
                .Where(a => a.EmployeeId == employee.Id && a.Date == date)
                .FirstOrDefaultAsync();

            bool isCheckedIn = false;
            bool isCheckedOut = false;

            if (attendance != null && !string.IsNullOrEmpty(attendance.PunchesJson))
            {
                var punches = System.Text.Json.JsonSerializer.Deserialize<List<Sportive.API.Services.HR.PunchLog>>(attendance.PunchesJson);
                var lastPunch = punches?.OrderBy(p => p.Time).LastOrDefault();
                
                if (lastPunch != null)
                {
                    if (lastPunch.Type == "In")
                    {
                        isCheckedIn = true;
                        isCheckedOut = false;
                    }
                    else
                    {
                        // Last was Out, so they are checked out right now, but CAN check in again
                        isCheckedIn = false;
                        isCheckedOut = false; 
                        // Returning false for both allows the frontend to show "Check In" again
                    }
                }
            }

            return Ok(new { 
                canPunch = !string.IsNullOrEmpty(employee.EmployeeNumber), 
                isCheckedIn, 
                isCheckedOut,
                checkInTime = attendance?.CheckIn,
                checkOutTime = attendance?.CheckOut
            });
        }
    }
}
