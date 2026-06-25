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

        [HttpPost("punch")]
        public async Task<IActionResult> SelfPunch()
        {
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.AppUserId == UserId);
            if (employee == null)
                return BadRequest(new { message = "لا يوجد ملف موظف مرتبط بحسابك." });

            if (string.IsNullOrEmpty(employee.EmployeeNumber))
                return BadRequest(new { message = "رقم الموظف غير مسجل. يرجى مراجعة الموارد البشرية." });

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
            var attendance = await _db.EmployeeAttendances
                .Where(a => a.EmployeeId == employee.Id && a.Date == now.Date)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            bool isCheckedIn = attendance != null && attendance.CheckIn.HasValue;
            bool isCheckedOut = attendance != null && attendance.CheckOut.HasValue;
            
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
