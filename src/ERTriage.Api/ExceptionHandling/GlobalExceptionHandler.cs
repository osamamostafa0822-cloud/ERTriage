using System.Diagnostics;
using ERTriage.Application.Common;
using ERTriage.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace ERTriage.Api.ExceptionHandling;

/// <summary>
/// Single place where exceptions become HTTP responses (exception-handling.md).
/// Known exceptions map to 4xx with a safe message; everything else is a generic 500 with a TraceId.
/// Each exception is logged exactly once, here.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>Non-standard status used by proxies for "client closed request".</summary>
    private const int ClientClosedRequest = 499;

    private const string UnexpectedErrorMessage = "An unexpected error occurred.";

    private static readonly IReadOnlyList<ApiError> NoErrors = Array.Empty<ApiError>();

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nobody is listening and this is not an error.
            _logger.LogDebug("Request was cancelled by the client. TraceId {TraceId}", traceId);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (statusCode, message, errors) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception. TraceId {TraceId}", traceId);
        }
        else
        {
            // Expected client error: log the type and status only (never messages that could carry data).
            _logger.LogWarning(
                "Request rejected with {StatusCode} ({ExceptionType}). TraceId {TraceId}",
                statusCode,
                exception.GetType().Name,
                traceId);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            GeneralResponse<object>.Fail(message, errors, traceId),
            cancellationToken);

        return true;
    }

    private static (int StatusCode, string Message, IReadOnlyList<ApiError> Errors) Map(Exception exception) =>
        exception switch
        {
            ValidationException ex => (StatusCodes.Status400BadRequest, ex.Message, ex.Errors),
            DomainValidationException ex => (StatusCodes.Status400BadRequest, ex.Message, NoErrors),
            NotFoundException ex => (StatusCodes.Status404NotFound, ex.Message, NoErrors),
            InvalidStatusTransitionException ex => (StatusCodes.Status409Conflict, ex.Message, NoErrors),
            ConcurrencyConflictException ex => (StatusCodes.Status409Conflict, ex.Message, NoErrors),
            _ => (StatusCodes.Status500InternalServerError, UnexpectedErrorMessage, NoErrors)
        };
}
