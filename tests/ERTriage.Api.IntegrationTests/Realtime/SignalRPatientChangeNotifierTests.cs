using System.Text.Json;
using ERTriage.Api.Realtime;
using ERTriage.Application;
using ERTriage.Application.Patients;
using ERTriage.Domain.Patients;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERTriage.Api.IntegrationTests.Realtime;

/// <summary>
/// The SignalR side of D-79 without a database or a socket: hand-written hub fakes record what would be sent, so the
/// tests prove the target (everyone vs everyone except the originating connection), the method name, the payload and
/// the best-effort failure policy.
/// </summary>
public class SignalRPatientChangeNotifierTests
{
    private static readonly PatientChangedNotification Sample = new(
        PatientChangeType.StatusChanged,
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        2,
        PatientStatus.InTreatment,
        new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero));

    private readonly FakeHubContext _hub = new();

    private SignalRPatientChangeNotifier CreateNotifier(string? originConnectionId = null)
    {
        var httpContext = new DefaultHttpContext();
        if (originConnectionId is not null)
        {
            httpContext.Request.Headers[TriageHub.OriginConnectionHeader] = originConnectionId;
        }

        return new SignalRPatientChangeNotifier(
            _hub,
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<SignalRPatientChangeNotifier>.Instance);
    }

    [Fact]
    public async Task NotifyAsync_WithoutOriginHeader_SendsToEveryone()
    {
        await CreateNotifier().NotifyAsync(Sample, CancellationToken.None);

        var sent = Assert.Single(_hub.FakeClients.All.Sent);
        Assert.Equal(TriageHub.PatientChangedMethod, sent.Method);
        Assert.Same(Sample, Assert.Single(sent.Arguments));
        Assert.Null(_hub.FakeClients.Excluded);
    }

    [Fact]
    public async Task NotifyAsync_WithOriginHeader_ExcludesThatConnection()
    {
        await CreateNotifier("connection-123").NotifyAsync(Sample, CancellationToken.None);

        Assert.Equal(["connection-123"], _hub.FakeClients.Excluded);
        Assert.Empty(_hub.FakeClients.All.Sent);
        var sent = Assert.Single(_hub.FakeClients.AllExceptProxy.Sent);
        Assert.Equal(TriageHub.PatientChangedMethod, sent.Method);
    }

    [Fact]
    public async Task NotifyAsync_WithOversizedOriginHeader_IgnoresItAndSendsToEveryone()
    {
        await CreateNotifier(new string('x', 500)).NotifyAsync(Sample, CancellationToken.None);

        Assert.Single(_hub.FakeClients.All.Sent);
        Assert.Null(_hub.FakeClients.Excluded);
    }

    [Fact]
    public async Task NotifyAsync_WhenSendingFails_DoesNotThrow()
    {
        _hub.FakeClients.All.ToThrow = new InvalidOperationException("socket closed");

        await CreateNotifier().NotifyAsync(Sample, CancellationToken.None);

        Assert.Empty(_hub.FakeClients.All.Sent);
    }

    [Fact]
    public async Task NotifyAsync_WhenTheRequestIsCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _hub.FakeClients.All.ToThrow = new OperationCanceledException(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateNotifier().NotifyAsync(Sample, cancellation.Token));
    }

    // ---- wiring ----

    private static ServiceProvider BuildApiProvider(bool applicationFirst)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://localhost:4200" })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = "Development" };
        var services = new ServiceCollection().AddLogging();

        if (applicationFirst)
        {
            services.AddApplication().AddApi(configuration, environment);
        }
        else
        {
            services.AddApi(configuration, environment).AddApplication();
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddApi_RegistersTheSignalRNotifier_RegardlessOfRegistrationOrder(bool applicationFirst)
    {
        using var provider = BuildApiProvider(applicationFirst);
        using var scope = provider.CreateScope();

        Assert.IsType<SignalRPatientChangeNotifier>(scope.ServiceProvider.GetRequiredService<IPatientChangeNotifier>());
    }

    [Fact]
    public void AddApi_SerializesNotificationEnumsAsStrings()
    {
        using var provider = BuildApiProvider(applicationFirst: false);
        var options = provider.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Sample, options));

        Assert.Equal("StatusChanged", json.RootElement.GetProperty("changeType").GetString());
        Assert.Equal("InTreatment", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("priority").GetInt32());
        // The payload is an id-and-state signal only: nothing identifying the patient travels over the socket.
        Assert.Equal(
            ["changeType", "occurredAt", "patientId", "priority", "status"],
            json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    // ---- hand-written fakes (no mocking library) ----

    private sealed record SentMessage(string Method, object?[] Arguments);

    private sealed class FakeClientProxy : IClientProxy
    {
        public List<SentMessage> Sent { get; } = [];

        public Exception? ToThrow { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (ToThrow is not null)
            {
                throw ToThrow;
            }

            Sent.Add(new SentMessage(method, args));

            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubClients : IHubClients
    {
        public FakeClientProxy All { get; } = new();

        public FakeClientProxy AllExceptProxy { get; } = new();

        public IReadOnlyList<string>? Excluded { get; private set; }

        IClientProxy IHubClients<IClientProxy>.All => All;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds)
        {
            Excluded = excludedConnectionIds;

            return AllExceptProxy;
        }

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Group(string groupName) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeHubContext : IHubContext<TriageHub>
    {
        public FakeHubClients FakeClients { get; } = new();

        public IHubClients Clients => FakeClients;

        public IGroupManager Groups => throw new NotSupportedException();
    }
}
