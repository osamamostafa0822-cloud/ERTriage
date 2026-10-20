using ERTriage.Application.Common;
using ERTriage.Application.Triage.Validation;
using ERTriage.Domain.Patients;
using ERTriage.Application.Triage.Requests;

namespace ERTriage.Application.Tests;

public class TriageBoardRequestValidatorTests
{
    [Fact]
    public void Validate_Defaults_AreValid() =>
        Assert.Empty(TriageBoardRequestValidator.Validate(new TriageBoardRequest()));

    [Fact]
    public void Validate_ExplicitFilters_AreValid() =>
        Assert.Empty(TriageBoardRequestValidator.Validate(new TriageBoardRequest(
            [PatientStatus.Waiting, PatientStatus.InTreatment, PatientStatus.Discharged, PatientStatus.Admitted],
            [1, 2, 3, 4],
            3,
            25)));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(int.MaxValue, 100)]
    public void Validate_PaginationBoundaries_AreValid(int pageNumber, int pageSize) =>
        Assert.Empty(TriageBoardRequestValidator.Validate(new TriageBoardRequest(PageNumber: pageNumber, PageSize: pageSize)));

    [Theory]
    [InlineData(0, 10, "pageNumber")]
    [InlineData(-1, 10, "pageNumber")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void Validate_PaginationOutOfRange_IsRejectedNotClamped(int pageNumber, int pageSize, string field)
    {
        var error = Assert.Single(TriageBoardRequestValidator.Validate(new TriageBoardRequest(PageNumber: pageNumber, PageSize: pageSize)));

        Assert.Equal(field, error.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-3)]
    public void Validate_PriorityOutsideOneToFour_IsRejected(int priority)
    {
        var error = Assert.Single(TriageBoardRequestValidator.Validate(new TriageBoardRequest(Priorities: [1, priority])));

        Assert.Equal("priority", error.Field);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Validate_UndefinedStatus_IsRejected(int status)
    {
        var error = Assert.Single(TriageBoardRequestValidator.Validate(new TriageBoardRequest(Statuses: [PatientStatus.Waiting, (PatientStatus)status])));

        Assert.Equal("status", error.Field);
    }

    [Fact]
    public void Validate_SeveralProblems_ReportsAllOfThemAtOnce()
    {
        var errors = TriageBoardRequestValidator.Validate(new TriageBoardRequest([(PatientStatus)99], [7], 0, 101));

        Assert.Equal(
            ["pageNumber", "pageSize", "priority", "status"],
            errors.Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_NullRequest_ReturnsOneError() =>
        Assert.Null(Assert.Single(TriageBoardRequestValidator.Validate(null)).Field);

    [Fact]
    public void EnsureValid_Invalid_ThrowsValidationException() =>
        Assert.Throws<ValidationException>(() => TriageBoardRequestValidator.EnsureValid(new TriageBoardRequest(PageSize: 0)));

    [Fact]
    public void EnsureValid_Valid_DoesNotThrow() =>
        TriageBoardRequestValidator.EnsureValid(new TriageBoardRequest());
}
