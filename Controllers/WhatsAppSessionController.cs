using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sportive.API.Data;

namespace Sportive.API.Controllers;

[ApiController]
[Route("api/whatsapp/session")]
[AllowAnonymous]
public class WhatsAppSessionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<WhatsAppSessionController> _logger;

    public WhatsAppSessionController(AppDbContext db, ILogger<WhatsAppSessionController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public class SaveSessionRequest
    {
        public string? Data { get; set; }
        public string? LidMap { get; set; }
    }

    /// <summary>
    /// GET /api/whatsapp/session/{key}
    /// Retrieves saved Baileys session data and LID map from MySQL database
    /// </summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> GetSession(string key)
    {
        try
        {
            var cleanKey = (key ?? "pos").Trim().ToLower();
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SessionKey, Data, LidMap, UpdatedAt FROM WhatsAppSessions WHERE SessionKey = @key LIMIT 1";

            var param = cmd.CreateParameter();
            param.ParameterName = "@key";
            param.Value = cleanKey;
            cmd.Parameters.Add(param);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var data = reader.IsDBNull(1) ? null : reader.GetString(1);
                var lidMap = reader.IsDBNull(2) ? null : reader.GetString(2);
                var updatedAt = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);

                return Ok(new
                {
                    success = true,
                    key = cleanKey,
                    data,
                    lidMap,
                    updatedAt
                });
            }

            return Ok(new
            {
                success = false,
                key = cleanKey,
                message = "No saved session found for this key"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve WhatsApp session for key {Key}", key);
            return StatusCode(500, new { error = "Database error retrieving session", details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/whatsapp/session/{key}
    /// Saves or updates Baileys session data and LID map into MySQL database
    /// </summary>
    [HttpPost("{key}")]
    public async Task<IActionResult> SaveSession(string key, [FromBody] SaveSessionRequest request)
    {
        try
        {
            var cleanKey = (key ?? "pos").Trim().ToLower();
            if (string.IsNullOrWhiteSpace(request?.Data) && string.IsNullOrWhiteSpace(request?.LidMap))
            {
                return BadRequest(new { error = "Payload cannot be empty" });
            }

            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO WhatsAppSessions (SessionKey, Data, LidMap, UpdatedAt)
                VALUES (@key, @data, @lidMap, UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE
                    Data = COALESCE(@data, Data),
                    LidMap = COALESCE(@lidMap, LidMap),
                    UpdatedAt = UTC_TIMESTAMP();";

            var pKey = cmd.CreateParameter();
            pKey.ParameterName = "@key";
            pKey.Value = cleanKey;
            cmd.Parameters.Add(pKey);

            var pData = cmd.CreateParameter();
            pData.ParameterName = "@data";
            pData.Value = (object?)request.Data ?? DBNull.Value;
            cmd.Parameters.Add(pData);

            var pLid = cmd.CreateParameter();
            pLid.ParameterName = "@lidMap";
            pLid.Value = (object?)request.LidMap ?? DBNull.Value;
            cmd.Parameters.Add(pLid);

            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("WhatsApp session saved successfully for key {Key}", cleanKey);
            return Ok(new { success = true, key = cleanKey, message = "Session saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save WhatsApp session for key {Key}", key);
            return StatusCode(500, new { error = "Database error saving session", details = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/whatsapp/session/{key}
    /// Removes session from database on explicit reset / logout
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> DeleteSession(string key)
    {
        try
        {
            var cleanKey = (key ?? "pos").Trim().ToLower();
            var conn = _db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM WhatsAppSessions WHERE SessionKey = @key";

            var param = cmd.CreateParameter();
            param.ParameterName = "@key";
            param.Value = cleanKey;
            cmd.Parameters.Add(param);

            await cmd.ExecuteNonQueryAsync();

            _logger.LogInformation("WhatsApp session deleted for key {Key}", cleanKey);
            return Ok(new { success = true, key = cleanKey, message = "Session deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete WhatsApp session for key {Key}", key);
            return StatusCode(500, new { error = "Database error deleting session", details = ex.Message });
        }
    }
}
