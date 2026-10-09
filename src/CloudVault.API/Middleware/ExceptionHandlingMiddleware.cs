using System.Net;
using System.Text.Json;
using CloudVault.Application.Common.Models;
using CloudVault.Domain.Exceptions;

namespace CloudVault.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        context.Response.ContentType = "application/json";

        var response = exception switch
        {
            ValidationException valEx => new
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Body = ApiResponse.Fail(valEx.Message, valEx.Errors)
            },
            QuotaExceededException quotaEx => new
            {
                StatusCode = (int)HttpStatusCode.BadRequest,
                Body = ApiResponse.Fail(quotaEx.Message)
            },
            DuplicateResourceException dupEx => new
            {
                StatusCode = (int)HttpStatusCode.Conflict,
                Body = ApiResponse.Fail(dupEx.Message)
            },
            NotFoundException notFoundEx => new
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                Body = ApiResponse.Fail(notFoundEx.Message)
            },
            UnauthorizedException unauthEx => new
            {
                StatusCode = (int)HttpStatusCode.Unauthorized,
                Body = ApiResponse.Fail(unauthEx.Message)
            },
            KeyNotFoundException knfEx => new
            {
                StatusCode = (int)HttpStatusCode.NotFound,
                Body = ApiResponse.Fail(knfEx.Message)
            },
            _ => new
            {
                StatusCode = (int)HttpStatusCode.InternalServerError,
                Body = ApiResponse.Fail("An unexpected internal error occurred. Please try again later.")
            }
        };

        if (response.StatusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled Exception [CorrelationId: {CorrelationId}]: {Message}", correlationId, exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled Application Exception [Status: {StatusCode}]: {Message}", response.StatusCode, exception.Message);
        }

        context.Response.StatusCode = response.StatusCode;
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response.Body, jsonOptions));
    }
}
