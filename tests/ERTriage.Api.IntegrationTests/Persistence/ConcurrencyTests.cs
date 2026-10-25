using System.Text.Json;
using ERTriage.Api.ExceptionHandling;
using ERTriage.Application.Common;
using ERTriage.Domain.Patients;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ERTriage.Domain.Common;

namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>Optimistic concurrency with the real SQL Server rowversion, and how a conflict reaches the client (409).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ConcurrencyTests(SqlServerFixture fixture) : PersistenceTestBase(fixture)
{
    private async Task<byte[]> ReadRowVersionAsync(Guid id) =>
        (byte[])(await ScalarAsync("SELECT RowVersion FROM Patients WHERE Id = @id", IdParameter(id)))!;

    [SqlServerFact]
    public async Task RowVersion_ChangesOnEveryUpdate()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Version Change");
        await SeedAsync(patient);
        var before = await ReadRowVersionAsync(patient.Id);

        await using (var scope = Services.CreateAsyncScope())
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IPatientRepository>()
                .GetByIdAsync(patient.Id, CancellationToken.None);
            loaded!.ApplyPriorityOverride(1, "Escalated by nurse", "Nurse Test", Clock);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var after = await ReadRowVersionAsync(patient.Id);

        Assert.Equal(8, before.Length);
        Assert.NotEqual(before, after);
    }

    [SqlServerFact]
    public async Task StaleWrite_IsRejected_AndTheFirstWriteSurvives()
    {
        var conflict = await ProduceConflictAsync();

        Assert.IsType<DbUpdateConcurrencyException>(conflict.InnerException);
        Assert.Equal("InTreatment", await ScalarAsync("SELECT Status FROM Patients"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT OverridePriority FROM Patients"));
    }

    [SqlServerFact]
    public async Task SequentialUpdates_WithFreshLoads_DoNotConflict()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "No Conflict");
        await SeedAsync(patient);

        await using (var first = Services.CreateAsyncScope())
        {
            var loaded = await first.ServiceProvider.GetRequiredService<IPatientRepository>()
                .GetByIdAsync(patient.Id, CancellationToken.None);
            loaded!.ChangeStatus(PatientStatus.InTreatment, null, Clock);
            await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var second = Services.CreateAsyncScope();
        var reloaded = await second.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetByIdAsync(patient.Id, CancellationToken.None);
        reloaded!.ChangeStatus(PatientStatus.Discharged, "Went home", Clock);

        await second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        Assert.Equal("Discharged", await ScalarAsync("SELECT Status FROM Patients"));
    }

    [SqlServerFact]
    public async Task RealConflict_IsMappedToHttp409WithAClientSafeBody()
    {
        var conflict = await ProduceConflictAsync();

        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-409",
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, LocalizationTestServices.Translator)
            .TryHandleAsync(context, conflict, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("The record was modified by another user. Reload and retry.", body.RootElement.GetProperty("message").GetString());

        var raw = body.RootElement.GetRawText();
        Assert.DoesNotContain("DbUpdateConcurrencyException", raw);
        Assert.DoesNotContain("Patients", raw);
    }

    // Two scopes load the same patient; the first saves, so the second holds a stale rowversion.
    private async Task<ConcurrencyConflictException> ProduceConflictAsync()
    {
        var patient = PersistenceTestData.NewPatient(Clock, "Conflict");
        await SeedAsync(patient);

        await using var scopeA = Services.CreateAsyncScope();
        await using var scopeB = Services.CreateAsyncScope();

        var a = await scopeA.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetByIdAsync(patient.Id, CancellationToken.None);
        var b = await scopeB.ServiceProvider.GetRequiredService<IPatientRepository>()
            .GetByIdAsync(patient.Id, CancellationToken.None);

        a!.ChangeStatus(PatientStatus.InTreatment, null, Clock);
        await scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);

        b!.ApplyPriorityOverride(1, "Escalated by nurse", "Nurse Test", Clock);

        return await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => scopeB.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));
    }
}
