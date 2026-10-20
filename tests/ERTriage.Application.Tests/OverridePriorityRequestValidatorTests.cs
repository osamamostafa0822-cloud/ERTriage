using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;

namespace ERTriage.Application.Tests;

public class OverridePriorityRequestValidatorTests
{
    private static OverridePriorityRequest Valid() => new(1, "Patient appears acutely distressed.", "Nurse A");

    [Fact]
    public void Validate_ValidRequest_ReturnsNoErrors() =>
        Assert.Empty(OverridePriorityRequestValidator.Validate(Valid()));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Validate_EveryPriorityOneToFour_IsValid(int priority) =>
        Assert.Empty(OverridePriorityRequestValidator.Validate(Valid() with { Priority = priority }));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void Validate_PriorityOutOfRange_IsRejected(int priority)
    {
        var error = Assert.Single(OverridePriorityRequestValidator.Validate(Valid() with { Priority = priority }));

        Assert.Equal("priority", error.Field);
    }

    [Fact]
    public void Validate_MissingPriority_IsRejected() =>
        Assert.Equal("priority", Assert.Single(OverridePriorityRequestValidator.Validate(Valid() with { Priority = null })).Field);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    public void Validate_MissingBlankOrTooShortReason_IsRejected(string? reason)
    {
        var error = Assert.Single(OverridePriorityRequestValidator.Validate(Valid() with { Reason = reason }));

        Assert.Equal("reason", error.Field);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(500)]
    public void Validate_ReasonLengthBoundaries_AreValid(int length) =>
        Assert.Empty(OverridePriorityRequestValidator.Validate(Valid() with { Reason = new string('r', length) }));

    [Fact]
    public void Validate_ReasonOver500_IsRejected() =>
        Assert.Equal("reason", Assert.Single(OverridePriorityRequestValidator.Validate(Valid() with { Reason = new string('r', 501) })).Field);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_OverriddenByIsOptional(string? overriddenBy) =>
        Assert.Empty(OverridePriorityRequestValidator.Validate(Valid() with { OverriddenBy = overriddenBy }));

    [Fact]
    public void Validate_OverriddenByOver100_IsRejected() =>
        Assert.Equal("overriddenBy", Assert.Single(OverridePriorityRequestValidator.Validate(Valid() with { OverriddenBy = new string('b', 101) })).Field);

    [Fact]
    public void Validate_SeveralProblems_ReportsAllOfThemAtOnce()
    {
        var errors = OverridePriorityRequestValidator.Validate(new(null, "   ", new string('b', 101)));

        Assert.Equal(["overriddenBy", "priority", "reason"], errors.Select(e => e.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Validate_Reason_IsNeverEchoedInMessages()
    {
        var errors = OverridePriorityRequestValidator.Validate(Valid() with { Reason = "xy" });

        Assert.DoesNotContain("xy", Assert.Single(errors).Message);
    }

    [Fact]
    public void Validate_NullRequest_ReturnsOneBodyError() =>
        Assert.Null(Assert.Single(OverridePriorityRequestValidator.Validate(null)).Field);

    [Fact]
    public void EnsureValid_Invalid_ThrowsValidationException() =>
        Assert.Throws<ValidationException>(() => OverridePriorityRequestValidator.EnsureValid(new(1, null, null)));
}
