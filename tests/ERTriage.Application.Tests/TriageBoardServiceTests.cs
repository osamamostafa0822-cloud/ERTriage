using ERTriage.Application.Common;
using ERTriage.Domain.Patients;
using Microsoft.Extensions.Logging;
using ERTriage.Application.Triage.Requests;
using ERTriage.Application.Triage.Services;
using ERTriage.Application.Triage.Waiting;

namespace ERTriage.Application.Tests;

public class TriageBoardServiceTests
{
    private readonly FakeTimeProvider _clock = TestData.Clock();
    private readonly CapturingLogger<TriageBoardService> _logger = new();
    private readonly FakePatientRepository _repository = new();

    private TriageBoardService CreateService() => new(_repository, _clock, _logger);

    [Fact]
    public async Task GetBoardAsync_NoStatusFilter_UsesWaitingAndInTreatment()
    {
        await CreateService().GetBoardAsync(new TriageBoardRequest(), CancellationToken.None);

        var query = _repository.LastBoardQuery!;
        Assert.Equal([PatientStatus.Waiting, PatientStatus.InTreatment], query.Statuses);
        Assert.Empty(query.Priorities!);
        Assert.Equal(new PaginationRequest(1, 10), query.Page);
    }

    [Fact]
    public async Task GetBoardAsync_EmptyStatusList_AlsoUsesTheDefault()
    {
        await CreateService().GetBoardAsync(new TriageBoardRequest(Statuses: []), CancellationToken.None);

        Assert.Equal(TriageBoardService.DefaultStatuses, _repository.LastBoardQuery!.Statuses);
    }

    [Fact]
    public async Task GetBoardAsync_ExplicitStatuses_OverrideTheDefaultAndAreDistinct()
    {
        var request = new TriageBoardRequest(
            [PatientStatus.Discharged, PatientStatus.Admitted, PatientStatus.Discharged]);

        await CreateService().GetBoardAsync(request, CancellationToken.None);

        Assert.Equal([PatientStatus.Discharged, PatientStatus.Admitted], _repository.LastBoardQuery!.Statuses);
    }

    [Fact]
    public async Task GetBoardAsync_PriorityFilter_IsPassedDistinctToTheRepository()
    {
        await CreateService().GetBoardAsync(new TriageBoardRequest(Priorities: [2, 1, 2]), CancellationToken.None);

        Assert.Equal([2, 1], _repository.LastBoardQuery!.Priorities);
    }

    [Fact]
    public async Task GetBoardAsync_Paging_IsPassedToTheRepositoryAndMetadataIsPreserved()
    {
        var first = TestData.NewPatient(_clock, 45, "Patient One");
        var second = TestData.NewPatient(_clock, 5, "Patient Two");
        _repository.BoardResult = new PagedResult<Patient>([first, second], 12, 2, 5);

        var page = await CreateService().GetBoardAsync(new TriageBoardRequest(PageNumber: 2, PageSize: 5), CancellationToken.None);

        Assert.Equal(new PaginationRequest(2, 5), _repository.LastBoardQuery!.Page);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(["Patient One", "Patient Two"], page.Items.Select(i => i.FullName));
    }

    [Fact]
    public async Task GetBoardAsync_Items_CarryTheWaitingIndicatorFromTheClock()
    {
        var long_ = TestData.NewPatient(_clock, 65, "Long Wait");
        var warning = TestData.NewPatient(_clock, 30, "Warning Wait");
        var fresh = TestData.NewPatient(_clock, 2, "Fresh");
        _repository.BoardResult = new PagedResult<Patient>([long_, warning, fresh], 3, 1, 10);

        var page = await CreateService().GetBoardAsync(new TriageBoardRequest(), CancellationToken.None);

        Assert.Equal(
            [(65, WaitingLevel.Critical), (30, WaitingLevel.Warning), (2, WaitingLevel.Normal)],
            page.Items.Select(i => (i.WaitingMinutes!.Value, i.WaitingLevel)));
        Assert.All(page.Items, i => Assert.Equal([PatientStatus.InTreatment], i.AllowedTransitions));
    }

    [Fact]
    public async Task GetBoardAsync_OverriddenPatient_ShowsOverrideInTheItem()
    {
        var patient = TestData.NewPatient(_clock, 10);
        patient.ApplyPriorityOverride(4, "Stable on reassessment", null, _clock);
        _repository.BoardResult = new PagedResult<Patient>([patient], 1, 1, 10);

        var item = Assert.Single((await CreateService().GetBoardAsync(new TriageBoardRequest(), CancellationToken.None)).Items);

        Assert.True(item.IsOverridden);
        Assert.Equal(4, item.CurrentPriority);
        Assert.Equal("Stable on reassessment", item.OverrideReason);
    }

    [Fact]
    public async Task GetBoardAsync_Success_LogsDebugCountsOnly()
    {
        _repository.BoardResult = new PagedResult<Patient>([TestData.NewPatient(_clock, 1, "Hidden Name")], 7, 2, 5);

        await CreateService().GetBoardAsync(new TriageBoardRequest(PageNumber: 2, PageSize: 5), CancellationToken.None);

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal(1, entry.State["ReturnedCount"]);
        Assert.Equal(7, entry.State["TotalCount"]);
        Assert.DoesNotContain("Hidden Name", _logger.AllText());
    }

    [Fact]
    public async Task GetBoardAsync_InvalidRequest_LogsNothing()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().GetBoardAsync(new TriageBoardRequest(PageNumber: 0), CancellationToken.None));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task GetBoardAsync_EmptyResult_ReturnsAnEmptyPage()
    {
        var page = await CreateService().GetBoardAsync(new TriageBoardRequest(), CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetBoardAsync_InvalidRequest_ThrowsAllErrorsAndNeverQueries()
    {
        var request = new TriageBoardRequest([(PatientStatus)99], [7], 0, 101);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().GetBoardAsync(request, CancellationToken.None));

        Assert.Equal(
            ["pageNumber", "pageSize", "priority", "status"],
            exception.Errors.Select(e => e.Field).Order(StringComparer.Ordinal));
        Assert.Equal(0, _repository.BoardCalls);
    }

    [Fact]
    public async Task GetBoardAsync_NullRequest_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().GetBoardAsync(null, CancellationToken.None));

        Assert.Equal(0, _repository.BoardCalls);
    }

    [Fact]
    public async Task GetBoardAsync_PassesTheCancellationToken()
    {
        using var source = new CancellationTokenSource();

        await CreateService().GetBoardAsync(new TriageBoardRequest(), source.Token);

        Assert.Equal(source.Token, _repository.LastToken);
    }
}
