using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;
using Sportive.API.Models;
using Sportive.API.Utils;

namespace Sportive.API.Services.HR
{
    public class ZKDeviceService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<ZKDeviceService> _logger;

        public ZKDeviceService(AppDbContext db, ILogger<ZKDeviceService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Processes a raw punch log from a fingerprint machine.
        /// </summary>
        public async Task ProcessPunchAsync(string pin, DateTime timestamp, string? serialNumber)
        {
            if (string.IsNullOrWhiteSpace(pin))
            {
                _logger.LogWarning("Received empty PIN from device SN: {SN}", serialNumber);
                return;
            }

            var normalizedPin = pin.TrimStart('0');
            
            // Match employee by absolute match, normalized numeric code, or suffixes (e.g., EMP-0012 matching 12)
            var emp = await _db.Employees.FirstOrDefaultAsync(e => 
                e.EmployeeNumber.ToLower() == pin.ToLower() ||
                (!string.IsNullOrEmpty(normalizedPin) && e.EmployeeNumber.ToLower() == normalizedPin.ToLower()) ||
                e.EmployeeNumber.EndsWith("-" + pin) ||
                (!string.IsNullOrEmpty(normalizedPin) && e.EmployeeNumber.EndsWith("-" + normalizedPin))
            );

            if (emp == null)
            {
                _logger.LogWarning("No employee matches PIN: {PIN} (normalized: {Normalized}) from device SN: {SN}", pin, normalizedPin, serialNumber);
                return;
            }

            // Business Date logic: Punches before 2:00 AM belong to the previous day
            var date = timestamp.Hour < 2 ? timestamp.Date.AddDays(-1) : timestamp.Date;

            var attendance = await _db.EmployeeAttendances
                .Where(a => a.EmployeeId == emp.Id && a.Date == date)
                .FirstOrDefaultAsync();

            var punches = new System.Collections.Generic.List<PunchLog>();
            if (attendance != null && !string.IsNullOrEmpty(attendance.PunchesJson))
            {
                punches = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PunchLog>>(attendance.PunchesJson) ?? new System.Collections.Generic.List<PunchLog>();
            }

            var lastPunch = punches.OrderBy(p => p.Time).LastOrDefault();
            var newPunch = new PunchLog 
            { 
                Time = timestamp, 
                Source = serialNumber ?? "Web",
                Type = (lastPunch == null || lastPunch.Type == "Out") ? "In" : "Out"
            };
            
            punches.Add(newPunch);
            punches = punches.OrderBy(p => p.Time).ToList();

            if (attendance != null)
            {
                attendance.PunchesJson = System.Text.Json.JsonSerializer.Serialize(punches);
                RecalculateAttendanceMetrics(attendance, emp);
                attendance.UpdatedAt = TimeHelper.GetEgyptTime();
                _logger.LogInformation("Updated attendance record ID {Id} for Employee {Emp} on {Date} (Punches: {Count})", 
                    attendance.Id, emp.Name, attendance.Date.ToShortDateString(), punches.Count);
            }
            else
            {
                // Create new record
                var newAttendance = new EmployeeAttendance
                {
                    EmployeeId = emp.Id,
                    Date = date,
                    PunchesJson = System.Text.Json.JsonSerializer.Serialize(punches),
                    IsAbsent = false,
                    Notes = $"تم التسجيل بواسطة النظام التلقائي",
                    CreatedAt = TimeHelper.GetEgyptTime()
                };

                RecalculateAttendanceMetrics(newAttendance, emp);
                _db.EmployeeAttendances.Add(newAttendance);
                _logger.LogInformation("Created new attendance record for Employee {Emp} on {Date}", 
                    emp.Name, date.ToShortDateString());
            }

            await _db.SaveChangesAsync();
        }

        private void RecalculateAttendanceMetrics(EmployeeAttendance attendance, Employee emp)
        {
            if (attendance.IsAbsent)
            {
                attendance.WorkHours = 0;
                attendance.OvertimeHours = 0;
                attendance.DelayMinutes = 0;
                return;
            }

            var punches = new System.Collections.Generic.List<PunchLog>();
            if (!string.IsNullOrEmpty(attendance.PunchesJson))
            {
                punches = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<PunchLog>>(attendance.PunchesJson) ?? new System.Collections.Generic.List<PunchLog>();
                punches = punches.OrderBy(p => p.Time).ToList();
            }

            if (punches.Any())
            {
                attendance.CheckIn = punches.First().Time;
                var lastPunch = punches.Last();
                attendance.CheckOut = lastPunch.Type == "Out" ? lastPunch.Time : (DateTime?)null;

                decimal totalHours = 0;
                DateTime? currentIn = null;
                
                foreach (var p in punches)
                {
                    if (p.Type == "In")
                    {
                        currentIn = p.Time;
                    }
                    else if (p.Type == "Out" && currentIn.HasValue)
                    {
                        totalHours += (decimal)(p.Time - currentIn.Value).TotalHours;
                        currentIn = null;
                    }
                }
                
                attendance.WorkHours = totalHours;
                
                // Overtime
                var stdHours = emp.WorkHoursPerDay;
                if (attendance.WorkHours > stdHours)
                {
                    attendance.OvertimeHours = attendance.WorkHours - stdHours;
                }
                else
                {
                    attendance.OvertimeHours = 0;
                }
            }
            else
            {
                attendance.CheckIn = null;
                attendance.CheckOut = null;
                attendance.WorkHours = 0;
                attendance.OvertimeHours = 0;
            }

            // Delay minutes calculation (only in Fixed Shift mode, comparing CheckIn to shift start)
            if (attendance.CheckIn.HasValue)
            {
                // Verify if it is weekend day - weekends should have 0 delay
                var weekendDays = (emp.WeeklyDaysOff ?? "Friday")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => d.Trim().ToLower())
                    .ToList();
                
                var dayNameAr = attendance.Date.ToString("dddd", new System.Globalization.CultureInfo("ar-EG")).ToLower();
                var dayNameEn = attendance.Date.ToString("dddd", new System.Globalization.CultureInfo("en-US")).ToLower();
                var isWeekend = weekendDays.Contains(dayNameEn) || weekendDays.Contains(dayNameAr);

                if (isWeekend)
                {
                    attendance.DelayMinutes = 0;
                }
                else if (emp.AttendanceMode == AttendanceMode.Fixed && !string.IsNullOrEmpty(emp.ShiftStartTime))
                {
                    if (TimeSpan.TryParse(emp.ShiftStartTime, out var shiftStart))
                    {
                        var stdCheckIn = attendance.Date.Add(shiftStart);
                        if (attendance.CheckIn.Value > stdCheckIn)
                        {
                            var diff = (attendance.CheckIn.Value - stdCheckIn).TotalMinutes;
                            attendance.DelayMinutes = diff > 0 ? (decimal)diff : 0;
                        }
                        else
                        {
                            attendance.DelayMinutes = 0;
                        }
                    }
                }
                else
                {
                    attendance.DelayMinutes = 0;
                }
            }
        }
    }

    public class PunchLog
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = "In";
        public string Source { get; set; } = "";
    }
}
