using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Application.Triage;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Tests;

/// <summary>Hand-written in-memory repository: records what the services asked for (no mocking library).</summary>
internal sealed class FakePatientRepository : IPatientRepository
{
    private readonly Dictionary<Guid, Patient> _patients = [];

    public List<Patient> Added { get; } = [];

    public int TrackedLoads { get; private set; }

    public int ReadOnlyLoads { get; private set; }

    public int BoardCalls { get; private set; }

    public TriageBoardQuery? LastBoardQuery { get; private set; }

    public CancellationToken LastToken { get; private set; }

    /// <summary>What <see cref="GetBoardPageAsync"/> returns.</summary>
    public PagedResult<Patient> BoardResult { get; set; } = new([], 0, 1, 10);

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

    public Task<PagedResult<Patient>> GetBoardPageAsync(TriageBoardQuery query, CancellationToken cancellationToken)
    {
        BoardCalls++;
        LastBoardQuery = query;
        LastToken = cancellationToken;

        return Task.FromResult(BoardResult);
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
