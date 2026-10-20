using System.Data.SqlTypes;
using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Application.Triage;
using ERTriage.Domain.Patients;
using ERTriage.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>The real repository + Unit of Work against real SQL Server (never EF Core InMemory).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class PatientRepositoryTests(SqlServerFixture fixture) : PersistenceTestBase(fixture)
{
    // ---- add / get ----

    [SqlServerFact]
    public async Task AddThenGetById_RoundTripsEveryPersistedField()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Round Trip", Now.AddMinutes(-42), overridePriority: 1);
        await SeedAsync(patient);

        await using var scope = Services.CreateAsyncScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetByIdAsync(patient.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.NotSame(patient, loaded);
        Assert.Equal(patient.Id, loaded.Id);
        Assert.Equal("Round Trip", loaded.FullName);
        Assert.Equal(patient.Age, loaded.Age);
        Assert.Equal(patient.ChiefComplaint, loaded.ChiefComplaint);
        Assert.Equal(patient.Vitals, loaded.Vitals);
        Assert.Equal(patient.ArrivalTime, loaded.ArrivalTime);
        Assert.Equal(DateTimeKind.Utc, loaded.ArrivalTime.Kind);
        Assert.Equal(patient.CreatedAt, loaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(patient.CalculatedPriority, loaded.CalculatedPriority);
        Assert.Equal(patient.PriorityReason, loaded.PriorityReason);
        Assert.Equal(1, loaded.OverridePriority);
        Assert.Equal("Test override", loaded.OverrideReason);
        Assert.Equal("Nurse Test", loaded.OverriddenBy);
        Assert.Equal(patient.OverriddenAt, loaded.OverriddenAt);
        Assert.Equal(1, loaded.CurrentPriority);
        Assert.Equal(PatientStatus.Waiting, loaded.Status);
        Assert.Null(loaded.TreatmentStartedAt);
        Assert.Null(loaded.CompletedAt);
        Assert.Null(loaded.DispositionNote);
        Assert.Equal(8, loaded.RowVersion.Length);
    }

    [SqlServerFact]
    public async Task Status_IsStoredAsItsName()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Status Name", status: PatientStatus.Admitted);
        await SeedAsync(patient);

        var stored = await ScalarAsync("SELECT Status FROM Patients WHERE Id = @id", IdParameter(patient.Id));

        Assert.Equal("Admitted", stored);
    }

    [SqlServerFact]
    public async Task GetById_UnknownId_ReturnsNull()
    {
        await using var scope = Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await repository.GetByIdReadOnlyAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [SqlServerFact]
    public async Task ReadOnlyQueries_DoNotTrack_AndTrackedQueriesDo()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Tracking");
        await SeedAsync(patient);

        await using var scope = Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        var context = scope.ServiceProvider.GetRequiredService<ErTriageDbContext>();

        await repository.GetByIdReadOnlyAsync(patient.Id, CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());

        await repository.GetBoardPageAsync(BoardQuery([], 1, 10), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());

        await repository.GetByIdAsync(patient.Id, CancellationToken.None);

        // The owned VitalSigns is tracked together with its Patient (two entries); only one Patient is tracked.
        Assert.Single(context.ChangeTracker.Entries<Patient>());
        Assert.NotEmpty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task UpdatingATrackedEntity_PersistsTheChange()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Update Me");
        await SeedAsync(patient);

        await using (var scope = Services.CreateAsyncScope())
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IPatientRepository>()
                .GetByIdAsync(patient.Id, CancellationToken.None);
            loaded!.ChangeStatus(PatientStatus.InTreatment, null, Clock);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var verify = Services.CreateAsyncScope();
        var reloaded = await verify.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetByIdReadOnlyAsync(patient.Id, CancellationToken.None);

        Assert.Equal(PatientStatus.InTreatment, reloaded!.Status);
        Assert.Equal(Now.UtcDateTime, reloaded.TreatmentStartedAt);
    }

    // ---- board: filtering and business order ----

    [SqlServerTheory]
    [InlineData("", "E,B,C,G,A,D,F")]
    [InlineData("Waiting", "B,C,A,D")]
    [InlineData("InTreatment", "E")]
    [InlineData("Waiting,InTreatment", "E,B,C,A,D")]
    [InlineData("Discharged,Admitted", "G,F")]
    public async Task Board_FiltersByStatus_AndOrdersByPriorityThenArrival(string statuses, string expectedLetters)
    {
        await SeedBoardAsync();
        var filter = statuses.Length == 0
            ? []
            : statuses.Split(',').Select(Enum.Parse<PatientStatus>).ToArray();

        var page = await GetBoardAsync(filter, 1, 10);

        Assert.Equal(expectedLetters, string.Join(",", page.Items.Select(p => p.FullName[^1])));
        Assert.Equal(expectedLetters.Split(',').Length, page.TotalCount);
    }

    // ---- board: deterministic tie-breaker and paging ----

    [SqlServerFact]
    public async Task Board_SamePriorityAndArrival_IsOrderedById_AndStableAcrossPages()
    {
        var patients = Enumerable.Range(1, 12)
            .Select(i => PersistenceTestData.NewPatient(Clock, $"Tie {i:00}", Now.AddMinutes(-30), overridePriority: 2))
            .ToArray();
        await SeedAsync(patients);

        // SQL Server orders uniqueidentifier differently from .NET's Guid, so the expectation uses SqlGuid.
        var expected = patients.Select(p => p.Id).OrderBy(id => new SqlGuid(id)).ToList();

        var pages = new[]
        {
            await GetBoardAsync([], 1, 5),
            await GetBoardAsync([], 2, 5),
            await GetBoardAsync([], 3, 5)
        };

        Assert.Equal([5, 5, 2], pages.Select(p => p.Items.Count));
        Assert.All(pages, p =>
        {
            Assert.Equal(12, p.TotalCount);
            Assert.Equal(3, p.TotalPages);
        });

        var actual = pages.SelectMany(p => p.Items).Select(p => p.Id).ToList();
        Assert.Equal(expected, actual);
        Assert.Equal(actual.Count, actual.Distinct().Count());

        var again = (await GetBoardAsync([], 1, 5)).Items.Select(p => p.Id);
        Assert.Equal(expected.Take(5), again);
    }

    [SqlServerFact]
    public async Task Board_PageBeyondTheEnd_ReturnsNoItemsButTheTotal()
    {
        await SeedBoardAsync();

        var page = await GetBoardAsync([], 10, 5);

        Assert.Empty(page.Items);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(10, page.PageNumber);
        Assert.Equal(5, page.PageSize);
    }

    [SqlServerTheory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Board_InvalidPagination_IsRejectedWithAValidationException(int pageNumber, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() => GetBoardAsync([], pageNumber, pageSize));
    }

    [SqlServerFact]
    public async Task Board_Paging_IsExecutedInTheDatabaseWithOffsetFetchAndTheBusinessOrder()
    {
        await SeedBoardAsync();
        Logs.Clear();

        var page = await GetBoardAsync([PatientStatus.Waiting, PatientStatus.InTreatment], 2, 2);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.TotalCount);

        var commands = Logs.Commands;
        var pageSql = Assert.Single(commands, c => c.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("FETCH NEXT", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(commands, c => c.Contains("COUNT(", StringComparison.OrdinalIgnoreCase));

        var orderBy = pageSql[pageSql.IndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase)..];
        var priority = orderBy.IndexOf("[CurrentPriority]", StringComparison.Ordinal);
        var arrival = orderBy.IndexOf("[ArrivalTime]", StringComparison.Ordinal);
        var id = orderBy.IndexOf("[Id]", StringComparison.Ordinal);
        Assert.True(priority >= 0 && priority < arrival && arrival < id, "ORDER BY must be priority, arrival time, id.");
    }

    // ---- helpers ----

    private static TriageBoardQuery BoardQuery(PatientStatus[] statuses, int pageNumber, int pageSize) =>
        new(statuses, new PaginationRequest(pageNumber, pageSize));

    private async Task<PagedResult<Patient>> GetBoardAsync(PatientStatus[] statuses, int pageNumber, int pageSize)
    {
        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetBoardPageAsync(BoardQuery(statuses, pageNumber, pageSize), CancellationToken.None);
    }

    // Expected business order: E, B, C, G, A, D, F (priority 1..4, then the longer-waiting arrival first).
    private async Task SeedBoardAsync() =>
        await SeedAsync(
            PersistenceTestData.NewPatient(Clock, "Patient A", Now.AddMinutes(-30), 2),
            PersistenceTestData.NewPatient(Clock, "Patient B", Now.AddMinutes(-5), 1),
            PersistenceTestData.NewPatient(Clock, "Patient C", Now.AddMinutes(-60), 2),
            PersistenceTestData.NewPatient(Clock, "Patient D", Now.AddMinutes(-90), 3),
            PersistenceTestData.NewPatient(Clock, "Patient E", Now.AddMinutes(-10), 1, PatientStatus.InTreatment),
            PersistenceTestData.NewPatient(Clock, "Patient F", Now.AddMinutes(-120), 4, PatientStatus.Discharged),
            PersistenceTestData.NewPatient(Clock, "Patient G", Now.AddMinutes(-45), 2, PatientStatus.Admitted));
}
