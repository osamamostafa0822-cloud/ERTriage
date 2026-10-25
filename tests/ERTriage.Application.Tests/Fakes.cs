using ERTriage.Domain.Patients;
using ERTriage.Application.Patients.Notifications;
using ERTriage.Domain.Common;
using ERTriage.Domain.Triage;

namespace ERTriage.Application.Tests;

/// <summary>Hand-written in-memory repository: records what the services asked for (no mocking library).</summary>
internal sealed class FakePatientRepository : IPatientRepository
{
    private readonly Dictionary<Guid, Patient> _patients = [];

    public List<Patient> Added { get; } = [];

    public int TrackedLoads { get; private set; }

    public int ReadOnlyLoads { get; private set; }

    public CancellationToken LastToken { get; private set; }

    public void Seed(Patient patient) => _patients[patient.Id] = patient;

    public void Add(Patient patient)
    {
        Added.Add(patient);
        _patients[patient.Id] = patient;
    }

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        TrackedLoads++;
        LastToken = cancellationToken;

        return Task.FromResult(_patients.GetValueOrDefault(id));
    }

    public Task<Patient?> GetByIdReadOnlyAsync(Guid id, CancellationToken cancellationToken)
    {
        ReadOnlyLoads++;
        LastToken = cancellationToken;

        return Task.FromResult(_patients.GetValueOrDefault(id));
    }
}

/// <summary>Hand-written board read port: records the query and returns a preset page (no mocking library).</summary>
internal sealed class FakeTriageBoardReadRepository : ITriageBoardReadRepository
{
    public int Calls { get; private set; }

    public TriageBoardQuery? LastQuery { get; private set; }

    public CancellationToken LastToken { get; private set; }

    /// <summary>What <see cref="GetPageAsync"/> returns.</summary>
    public TriageBoardPage Result { get; set; } = new([], 0);

    public Task<TriageBoardPage> GetPageAsync(TriageBoardQuery query, CancellationToken cancellationToken)
    {
        Calls++;
        LastQuery = query;
        LastToken = cancellationToken;

        return Task.FromResult(Result);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public CancellationToken LastToken { get; private set; }

    /// <summary>When set, <see cref="SaveChangesAsync"/> throws it (for example a concurrency conflict).</summary>
    public Exception? ToThrow { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;

        if (ToThrow is not null)
        {
            throw ToThrow;
        }

        SaveCalls++;

        return Task.FromResult(1);
    }
}

/// <summary>Records every notification the services publish (no mocking library).</summary>
internal sealed class RecordingPatientChangeNotifier : IPatientChangeNotifier
{
    public List<PatientChangedNotification> Notifications { get; } = [];

    public Task NotifyAsync(PatientChangedNotification notification, CancellationToken cancellationToken)
    {
        Notifications.Add(notification);

        return Task.CompletedTask;
    }
}
