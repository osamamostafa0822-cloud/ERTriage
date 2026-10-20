using System.Collections.Concurrent;
using ERTriage.Api.Configuration;
using ERTriage.Application;
using ERTriage.Domain.Patients;
using ERTriage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>A clock the test controls (the Domain reads time only through <see cref="TimeProvider"/>).</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Collects log messages so tests can inspect the SQL that EF Core really sent (no sensitive data logging needed).</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly ConcurrentQueue<(string Category, string Message)> _entries = new();

    public IReadOnlyList<(string Category, string Message)> Entries => _entries.ToArray();

    /// <summary>Text of every executed database command (SQL; parameter values are not logged by default).</summary>
    public IReadOnlyList<string> Commands =>
        _entries.Where(e => e.Category == CommandCategory).Select(e => e.Message).ToArray();

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<(string Category, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, formatter(state, exception)));
    }
}

/// <summary>Builds the real Application + Infrastructure container against a given (isolated) database.</summary>
public static class PersistenceTestHost
{
    public static ServiceProvider Build(string connectionString, ILoggerProvider? loggerProvider = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ApiConfiguration.ConnectionStringKey] = connectionString
            })
            .Build();

        return new ServiceCollection()
            .AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.SetMinimumLevel(LogLevel.Information);

                if (loggerProvider is not null)
                {
                    builder.AddProvider(loggerProvider);
                }
            })
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}

/// <summary>Creates valid fictional patients through the Domain (never by hand-setting private state).</summary>
public static class PersistenceTestData
{
    public static VitalSigns Vitals() => VitalSigns.Create(80, 120, 80, 36.8m, 98);

    public static Patient NewPatient(
        TimeProvider clock,
        string name = "Test Patient",
        DateTimeOffset? arrival = null,
        int? overridePriority = null,
        PatientStatus status = PatientStatus.Waiting)
    {
        var patient = Patient.Register(name, 40, "Chest pain", Vitals(), arrival ?? clock.GetUtcNow(), clock);

        if (overridePriority is { } priority)
        {
            patient.ApplyPriorityOverride(priority, "Test override", "Nurse Test", clock);
        }

        if (status != PatientStatus.Waiting)
        {
            patient.ChangeStatus(PatientStatus.InTreatment, null, clock);

            if (status != PatientStatus.InTreatment)
            {
                patient.ChangeStatus(status, "Disposition note", clock);
            }
        }

        return patient;
    }
}
