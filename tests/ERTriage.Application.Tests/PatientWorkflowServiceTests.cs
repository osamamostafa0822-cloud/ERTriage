using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;
using ERTriage.Application.Patients.Notifications;
using ERTriage.Application.Patients.Services;

namespace ERTriage.Application.Tests;

public class PatientWorkflowServiceTests
{
    private readonly FakeTimeProvider _clock = TestData.Clock();
    private readonly CapturingLogger<PatientWorkflowService> _logger = new();
    private readonly FakePatientRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingPatientChangeNotifier _notifier = new();

    private PatientWorkflowService CreateService() => new(_repository, _unitOfWork, _notifier, _clock, _logger);

    private Patient SeedWaiting()
    {
        var patient = TestData.NewPatient(_clock, 10);
        _repository.Seed(patient);

        return patient;
    }

    private Patient SeedDischarged()
    {
        var patient = TestData.NewPatient(_clock, 10);
        patient.ChangeStatus(PatientStatus.InTreatment, null, _clock);
        patient.ChangeStatus(PatientStatus.Discharged, "Released with advice.", _clock);
        _repository.Seed(patient);

        return patient;
    }

    // ---- status ----

    [Fact]
    public async Task ChangeStatusAsync_WaitingToInTreatment_UpdatesAndSavesOnce()
    {
        var patient = SeedWaiting();

        var dto = await CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), CancellationToken.None);

        Assert.Equal(PatientStatus.InTreatment, dto.Status);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, dto.TreatmentStartedAt);
        Assert.Equal([PatientStatus.Discharged, PatientStatus.Admitted], dto.AllowedTransitions);
        Assert.Equal(1, _unitOfWork.SaveCalls);
        Assert.Equal(1, _repository.TrackedLoads);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public async Task ChangeStatusAsync_InTreatmentToDisposition_StoresTheNoteAndCompletionTime(PatientStatus target)
    {
        var patient = SeedWaiting();
        patient.ChangeStatus(PatientStatus.InTreatment, null, _clock);

        var dto = await CreateService().ChangeStatusAsync(patient.Id, new(target, "Treated and released."), CancellationToken.None);

        Assert.Equal(target, dto.Status);
        Assert.Equal("Treated and released.", dto.DispositionNote);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, dto.CompletedAt);
        Assert.Empty(dto.AllowedTransitions);
    }

    [Fact]
    public async Task ChangeStatusAsync_WaitingToDischarged_IsRejectedByTheDomainAndNotSaved()
    {
        var patient = SeedWaiting();

        await Assert.ThrowsAsync<InvalidStatusTransitionException>(
            () => CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.Discharged, "Went home."), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveCalls);
        Assert.Equal(PatientStatus.Waiting, patient.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_SameStatus_IsAnInvalidTransition()
    {
        var patient = SeedWaiting();

        await Assert.ThrowsAsync<InvalidStatusTransitionException>(
            () => CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.Waiting, null), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeStatusAsync_InvalidRequest_FailsValidationBeforeAnyLookup()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().ChangeStatusAsync(Guid.NewGuid(), new(PatientStatus.Discharged, null), CancellationToken.None));

        Assert.Equal("dispositionNote", Assert.Single(exception.Errors).Field);
        Assert.Equal(0, _repository.TrackedLoads);
        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownPatient_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().ChangeStatusAsync(Guid.NewGuid(), new(PatientStatus.InTreatment, null), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task ChangeStatusAsync_ConcurrencyConflict_Propagates()
    {
        var patient = SeedWaiting();
        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeStatusAsync_PassesTheCancellationToken()
    {
        var patient = SeedWaiting();
        using var source = new CancellationTokenSource();

        await CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), source.Token);

        Assert.Equal(source.Token, _repository.LastToken);
        Assert.Equal(source.Token, _unitOfWork.LastToken);
    }

    [Fact]
    public async Task ChangeStatusAsync_Success_LogsFromAndToWithoutTheNote()
    {
        var patient = SeedWaiting();
        patient.ChangeStatus(PatientStatus.InTreatment, null, _clock);

        await CreateService().ChangeStatusAsync(
            patient.Id, new(PatientStatus.Admitted, "Secret ward note."), CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(patient.Id, entry.State["PatientId"]);
        Assert.Equal(PatientStatus.InTreatment, entry.State["PreviousStatus"]);
        Assert.Equal(PatientStatus.Admitted, entry.State["Status"]);
        Assert.DoesNotContain("Secret ward note", _logger.AllText());
        Assert.DoesNotContain("Test Patient Alpha", _logger.AllText());
    }

    [Fact]
    public async Task ChangeStatusAsync_Failures_LogNothing()
    {
        var patient = SeedWaiting();
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(
            () => service.ChangeStatusAsync(patient.Id, new(PatientStatus.Discharged, null), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.ChangeStatusAsync(Guid.NewGuid(), new(PatientStatus.InTreatment, null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidStatusTransitionException>(
            () => service.ChangeStatusAsync(patient.Id, new(PatientStatus.Waiting, null), CancellationToken.None));

        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => service.ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), CancellationToken.None));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task OverridePriorityAsync_Success_LogsPrioritiesWithoutReasonOrNurse()
    {
        var patient = SeedWaiting();
        var calculated = patient.CalculatedPriority;
        var target = calculated == 4 ? 3 : 4;

        await CreateService().OverridePriorityAsync(
            patient.Id, new(target, "Confidential reason text", "Nurse Hidden"), CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(patient.Id, entry.State["PatientId"]);
        Assert.Equal(calculated, entry.State["CalculatedPriority"]);
        Assert.Equal(target, entry.State["CurrentPriority"]);
        Assert.DoesNotContain("Confidential reason text", _logger.AllText());
        Assert.DoesNotContain("Nurse Hidden", _logger.AllText());
    }

    [Fact]
    public async Task OverridePriorityAsync_Failures_LogNothing()
    {
        var patient = SeedWaiting();
        var service = CreateService();

        await Assert.ThrowsAsync<ValidationException>(
            () => service.OverridePriorityAsync(patient.Id, new(2, " ", null), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.OverridePriorityAsync(Guid.NewGuid(), new(2, "Valid reason", null), CancellationToken.None));

        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => service.OverridePriorityAsync(patient.Id, new(2, "Valid reason", null), CancellationToken.None));

        Assert.Empty(_logger.Entries);
    }

    // ---- override ----

    [Fact]
    public async Task OverridePriorityAsync_Valid_ChangesCurrentPriorityOnlyAndRecordsTheOverride()
    {
        var patient = SeedWaiting();
        var calculated = patient.CalculatedPriority;
        var target = calculated == 4 ? 3 : 4;

        var dto = await CreateService().OverridePriorityAsync(
            patient.Id, new(target, "Stable on reassessment", "Nurse A"), CancellationToken.None);

        Assert.Equal(target, dto.CurrentPriority);
        Assert.Equal(target, dto.OverridePriority);
        Assert.Equal(calculated, dto.CalculatedPriority);
        Assert.True(dto.IsOverridden);
        Assert.Equal("Stable on reassessment", dto.OverrideReason);
        Assert.Equal("Nurse A", dto.OverriddenBy);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, dto.OverriddenAt);
        Assert.Equal(1, _unitOfWork.SaveCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OverridePriorityAsync_MissingOrBlankReason_IsRejectedAndNotSaved(string? reason)
    {
        var patient = SeedWaiting();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().OverridePriorityAsync(patient.Id, new(2, reason, null), CancellationToken.None));

        Assert.Equal("reason", Assert.Single(exception.Errors).Field);
        Assert.False(patient.IsOverridden);
        Assert.Equal(0, _unitOfWork.SaveCalls);
        Assert.Equal(0, _repository.TrackedLoads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task OverridePriorityAsync_PriorityOutOfRange_IsRejected(int priority)
    {
        var patient = SeedWaiting();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().OverridePriorityAsync(patient.Id, new(priority, "Valid reason", null), CancellationToken.None));

        Assert.Equal("priority", Assert.Single(exception.Errors).Field);
    }

    [Fact]
    public async Task OverridePriorityAsync_SecondOverride_ReplacesTheFirst()
    {
        var patient = SeedWaiting();
        var service = CreateService();

        await service.OverridePriorityAsync(patient.Id, new(1, "First reason", null), CancellationToken.None);
        var dto = await service.OverridePriorityAsync(patient.Id, new(3, "Second reason", null), CancellationToken.None);

        Assert.Equal(3, dto.OverridePriority);
        Assert.Equal("Second reason", dto.OverrideReason);
        Assert.Equal(3, dto.CurrentPriority);
        Assert.Equal(2, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task OverridePriorityAsync_DischargedPatient_ThrowsStatusConflictAndIsNotSaved()
    {
        var patient = SeedDischarged();

        await Assert.ThrowsAsync<PatientStatusConflictException>(
            () => CreateService().OverridePriorityAsync(patient.Id, new(1, "Too late", null), CancellationToken.None));

        Assert.Equal(0, _unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task OverridePriorityAsync_UnknownPatient_ThrowsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().OverridePriorityAsync(Guid.NewGuid(), new(1, "Valid reason", null), CancellationToken.None));

    [Fact]
    public async Task OverridePriorityAsync_ConcurrencyConflict_Propagates()
    {
        var patient = SeedWaiting();
        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => CreateService().OverridePriorityAsync(patient.Id, new(1, "Valid reason", null), CancellationToken.None));
    }

    // ---- notifications (D-79) ----

    [Fact]
    public async Task ChangeStatusAsync_Success_NotifiesOnceWithoutPatientData()
    {
        var patient = SeedWaiting();

        await CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), CancellationToken.None);

        var notification = Assert.Single(_notifier.Notifications);
        Assert.Equal(PatientChangeType.StatusChanged, notification.ChangeType);
        Assert.Equal(patient.Id, notification.PatientId);
        Assert.Equal(PatientStatus.InTreatment, notification.Status);
        Assert.Equal(patient.CurrentPriority, notification.Priority);
        Assert.Equal(_clock.GetUtcNow(), notification.OccurredAt);
    }

    [Fact]
    public async Task OverridePriorityAsync_Success_NotifiesOnceWithTheNewPriority()
    {
        var patient = SeedWaiting();

        await CreateService().OverridePriorityAsync(patient.Id, new(3, "Stable on review", "Nurse Test"), CancellationToken.None);

        var notification = Assert.Single(_notifier.Notifications);
        Assert.Equal(PatientChangeType.PriorityOverridden, notification.ChangeType);
        Assert.Equal(patient.Id, notification.PatientId);
        Assert.Equal(3, notification.Priority);
        Assert.Equal(PatientStatus.Waiting, notification.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_RejectedTransition_DoesNotNotify()
    {
        var patient = SeedWaiting();

        await Assert.ThrowsAsync<InvalidStatusTransitionException>(
            () => CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.Discharged, "Too early."), CancellationToken.None));

        Assert.Empty(_notifier.Notifications);
    }

    [Fact]
    public async Task ChangeStatusAsync_SaveFails_DoesNotNotify()
    {
        var patient = SeedWaiting();
        _unitOfWork.ToThrow = new ConcurrencyConflictException(new InvalidOperationException("inner"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => CreateService().ChangeStatusAsync(patient.Id, new(PatientStatus.InTreatment, null), CancellationToken.None));

        Assert.Empty(_notifier.Notifications);
    }

    [Fact]
    public async Task OverridePriorityAsync_MissingReason_DoesNotNotify()
    {
        var patient = SeedWaiting();

        await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().OverridePriorityAsync(patient.Id, new(1, "  ", null), CancellationToken.None));

        Assert.Empty(_notifier.Notifications);
    }
}
