using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sportive.API.Interfaces;

namespace Sportive.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next   = next;
        _logger = logger;
        _env    = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";
        
        var translator = context.RequestServices.GetRequiredService<ITranslator>();

        var (statusCode, message) = ex switch
        {
            KeyNotFoundException    => (HttpStatusCode.NotFound,           ex.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized,   translator.Get("General.Unauthorized")),
            InvalidOperationException => (HttpStatusCode.BadRequest,       ex.Message),
            ArgumentException       => (HttpStatusCode.BadRequest,         ex.Message),
            BadHttpRequestException => (HttpStatusCode.BadRequest,         ex.Message),
            DbUpdateException       => (HttpStatusCode.Conflict,           translator.Get("General.DbError")),
            _                       => (HttpStatusCode.InternalServerError, translator.Get("General.InternalServerError"))
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            Success    = false,
            StatusCode = (int)statusCode,
            Message    = _env.IsDevelopment() ? (ex.InnerException?.Message ?? ex.Message) : message,
            Errors     = _env.IsDevelopment() ? new { stack = ex.StackTrace } : null,
            TraceId    = context.TraceIdentifier
        };

        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
