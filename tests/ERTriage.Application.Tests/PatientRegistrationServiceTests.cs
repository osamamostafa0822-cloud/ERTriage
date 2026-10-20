using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Application.Patients.Requests;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;

namespace ERTriage.Application.Tests;

public class PatientRegistrationServiceTests
{
    private readonly FakeTimeProvider _clock = TestData.Clock();
    private readonly CapturingLogger<PatientRegistrationService> _logger = new();
    private readonly FakePatientRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private PatientRegistrationService CreateService() => new(_repository, _unitOfWork, _clock, _logger);

    private RegisterPatientRequest ValidRequest() =>
        new("Test Patient Alpha", 54, "Shortness of breath", 112, 138, 86, 37.9m, 88, _clock.GetUtcNow());

    [Fact]
    public async Task RegisterAsync_ValidRequest_AddsOnePatientAndSavesOnce()
    {
        var dto = await CreateService().RegisterAsync(ValidRequest(), CancellationToken.None);

        var added = Assert.Single(_repository.Added);
        Assert.Equal(1, _unitOfWork.SaveCalls);
        Assert.Equal(added.Id, dto.Id);
        Assert.Equal("Test Patient Alpha", dto.FullName);
        Assert.Equal(54, dto.Age);
        Assert.Equal("Shortness of breath", dto.ChiefComplaint);
        Assert.Equal(112, dto.Vitals.HeartRate);
        Assert.Equal(37.9m, dto.Vitals.TemperatureC);
        Assert.Equal(PatientStatus.Waiting, dto.Status);
        Assert.False(dto.IsOverridden);
        Assert.Equal([PatientStatus.InTreatment], dto.AllowedTransitions);
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_PriorityIsCalculatedByTheDomain()
    {
        // SpO2 88 is in the most urgent band (docs/03-features/triage-priority.md).
        var dto = await CreateService().RegisterAsync(ValidRequest(), CancellationToken.None);

        Assert.Equal(1, dto.CalculatedPriority);
        Assert.Equal(1, dto.CurrentPriority);
        Assert.False(string.IsNullOrWhiteSpace(dto.PriorityReason));
    }

    [Fact]
    public async Task RegisterAsync_TrimsTextAndUsesTheInjectedClockForTimestamps()
    {
        var request = ValidRequest() with { FullName = "  Test Patient Alpha  " };

        var dto = await CreateService().RegisterAsync(request, CancellationToken.None);

        Assert.Equal("Test Patient Alpha", dto.FullName);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, dto.ArrivalTime);
    }

    [Fact]
    public async Task RegisterAsync_InvalidRequest_ThrowsValidationExceptionWithAllErrorsAndDoesNotPersist()
    {
        var request = new RegisterPatientRequest("A", 200, null, 5, 100, 100, 50.0m, 10, null);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().RegisterAsync(request, CancellationToken.None));

        Assert.Equal(
            ["age", "arrivalTime", "chiefComplaint", "diastolicBp", "fullName", "heartRate", "spO2", "temperatureC"],
            exception.Errors.Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Empty(_repository.Added);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task RegisterAsync_Success_LogsOneInformationWithIdAndPriorityOnly()
    {
        var dto = await CreateService().RegisterAsync(ValidRequest(), CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(dto.Id, entry.State["PatientId"]);
        Assert.Equal(1, entry.State["CalculatedPriority"]);
        Assert.Equal(
            ["CalculatedPriority", "PatientId"],
            entry.State.Keys.Where(k => k != "{OriginalFormat}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RegisterAsync_Success_LogsNoPatientDataAndNoDto()
    {
        await CreateService().RegisterAsync(ValidRequest(), CancellationToken.None);

        var text = _logger.AllText();
        Assert.DoesNotContain("Test Patient Alpha", text);
        Assert.DoesNotContain("Shortness of breath", text);
        Assert.DoesNotContain("37.9", text);
        Assert.DoesNotContain("PatientDto", text);
    }

    [Fact]
    public async Task RegisterAsync_Failure_LogsNothing_BecauseTheExceptionHandlerLogsOnce()
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().RegisterAsync(null, CancellationToken.None));

        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => CreateService().RegisterAsync(ValidRequest(), CancellationToken.None));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task RegisterAsync_NullRequest_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().RegisterAsync(null, CancellationToken.None));

        Assert.Empty(_repository.Added);
    }

    [Fact]
    public async Task RegisterAsync_ArrivalMoreThanFiveMinutesAhead_IsRejected()
    {
        var request = ValidRequest() with { ArrivalTime = _clock.GetUtcNow().AddMinutes(6) };

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().RegisterAsync(request, CancellationToken.None));

        Assert.Equal("arrivalTime", Assert.Single(exception.Errors).Field);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrencyConflictFromTheUnitOfWork_Propagates()
    {
        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => CreateService().RegisterAsync(ValidRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_PassesTheCancellationTokenToTheUnitOfWork()
    {
        using var source = new CancellationTokenSource();

        await CreateService().RegisterAsync(ValidRequest(), source.Token);

        Assert.Equal(source.Token, _unitOfWork.LastToken);
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ReturnsTheDtoWithoutTracking()
    {
        var patient = TestData.NewPatient(_clock, 5);
        _repository.Seed(patient);

        var dto = await CreateService().GetByIdAsync(patient.Id, CancellationToken.None);

        Assert.Equal(patient.Id, dto.Id);
        Assert.Equal(1, _repository.ReadOnlyLoads);
        Assert.Equal(0, _repository.TrackedLoads);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task GetByIdAsync_Unknown_ThrowsNotFoundWithoutEchoingTheId()
    {
        var id = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().GetByIdAsync(id, CancellationToken.None));

        Assert.DoesNotContain(id.ToString(), exception.Message);
    }
}
