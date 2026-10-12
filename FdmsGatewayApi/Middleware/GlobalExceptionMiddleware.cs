using System.Net;
using System.Text.Json;
using FdmsGatewayApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FdmsGatewayApi.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
            _logger.LogError(ex, "Unhandled exception captured by GlobalExceptionMiddleware: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path,
            Detail = exception.Message
        };

        switch (exception)
        {
            case ZimraValidationException zimraEx:
                context.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity; // 422 Unprocessable Content per FDMS §8.1-8.2
                problemDetails.Status = 422;
                problemDetails.Title = "ZIMRA Rule Validation Error";
                problemDetails.Type = $"https://fdms.zimra.co.zw/errors/{zimraEx.ErrorCode}";
                problemDetails.Extensions["errorCode"] = zimraEx.ErrorCode;
                break;

            case InvalidOperationException when exception.Message.Contains("Fiscal day", StringComparison.OrdinalIgnoreCase):
                context.Response.StatusCode = (int)HttpStatusCode.Locked; // 423 Locked per FDMS §3.1
                problemDetails.Status = 423;
                problemDetails.Title = "Fiscal Day Locked";
                problemDetails.Type = "https://fdms.zimra.co.zw/errors/FISC01";
                break;

            case ArgumentException:
            case InvalidOperationException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Status = 400;
                problemDetails.Title = "Invalid Request Parameters";
                problemDetails.Type = "https://fdms.zimra.co.zw/errors/DEV01";
                break;

            case KeyNotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails.Status = 404;
                problemDetails.Title = "Resource Not Found";
                break;

            default:
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                problemDetails.Status = 500;
                problemDetails.Title = "Internal Server Error";
                break;
        }

        string json = JsonSerializer.Serialize(problemDetails);
        return context.Response.WriteAsync(json);
    }
}

