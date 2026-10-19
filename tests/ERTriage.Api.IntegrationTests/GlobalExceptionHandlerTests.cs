using System.Text.Json;
using ERTriage.Api.ExceptionHandling;
using ERTriage.Application.Common;
using ERTriage.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERTriage.Api.IntegrationTests;

public class GlobalExceptionHandlerTests
{
    private const string TraceId = "test-trace-id";

    private static async Task<(DefaultHttpContext Context, JsonElement? Body)> HandleAsync(
        Exception exception,
        CancellationToken requestAborted = default)
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = TraceId,
            RequestServices = new ServiceCollection().BuildServiceProvider(),
            RequestAborted = requestAborted
        };
        context.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);

        context.Response.Body.Position = 0;
        if (context.Response.Body.Length == 0)
        {
            return (context, null);
        }

        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task TryHandleAsync_ValidationException_Returns400WithFieldErrors()
    {
        var exception = new ValidationException("chiefComplaint", "Chief complaint is required.");

        var (context, body) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(body!.Value.GetProperty("success").GetBoolean());
        Assert.Equal("Validation failed.", body.Value.GetProperty("message").GetString());
        Assert.Equal(TraceId, body.Value.GetProperty("traceId").GetString());
        var error = body.Value.GetProperty("errors")[0];
        Assert.Equal("chiefComplaint", error.GetProperty("field").GetString());
        Assert.Equal("Chief complaint is required.", error.GetProperty("message").GetString());
    }

    [Theory]
    [MemberData(nameof(KnownExceptions))]
    public async Task TryHandleAsync_KnownException_MapsStatusAndKeepsClientSafeMessage(
        Exception exception,
        int expectedStatus,
        string expectedMessage)
    {
        var (context, body) = await HandleAsync(exception);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.False(body!.Value.GetProperty("success").GetBoolean());
        Assert.Equal(expectedMessage, body.Value.GetProperty("message").GetString());
        Assert.Equal(TraceId, body.Value.GetProperty("traceId").GetString());
    }

    public static TheoryData<Exception, int, string> KnownExceptions() => new()
    {
        { new DomainValidationException("Override reason is required."), StatusCodes.Status400BadRequest, "Override reason is required." },
        { new NotFoundException("Patient not found."), StatusCodes.Status404NotFound, "Patient not found." },
        { new InvalidStatusTransitionException("Waiting", "Discharged"), StatusCodes.Status409Conflict, "Cannot change status from Waiting to Discharged." },
        { new ConcurrencyConflictException(new Exception("inner secret")), StatusCodes.Status409Conflict, "The record was modified by another user. Reload and retry." }
    };

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Returns500WithoutLeakingDetails()
    {
        var exception = new InvalidOperationException("secret connection string Server=prod;Password=hunter2");

        var (context, body) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body!.Value.GetProperty("message").GetString());
        Assert.Equal(TraceId, body.Value.GetProperty("traceId").GetString());

        var raw = body.Value.GetRawText();
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryHandleAsync_ClientAbortedRequest_Returns499WithoutBody()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (context, body) = await HandleAsync(new OperationCanceledException(), cts.Token);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Null(body);
    }
}
