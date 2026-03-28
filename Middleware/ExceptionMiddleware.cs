using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

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

        var (statusCode, message) = ex switch
        {
            KeyNotFoundException    => (HttpStatusCode.NotFound,           ex.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized,   ex.Message),
            InvalidOperationException => (HttpStatusCode.BadRequest,       ex.Message),
            ArgumentException       => (HttpStatusCode.BadRequest,         ex.Message),
            BadHttpRequestException => (HttpStatusCode.BadRequest,         ex.Message),
            DbUpdateException       => (HttpStatusCode.BadRequest,
                _env.IsDevelopment() ? (ex.InnerException?.Message ?? ex.Message)
                    : "Could not save data (invalid reference or constraint)."),
            _                       => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            StatusCode = (int)statusCode,
            Message    = message,
            ExceptionType = _env.IsDevelopment() ? ex.GetType().Name : null,
            Details    = _env.IsDevelopment() ? ex.StackTrace : null
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
