using ERTriage.Application.Common;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;
using ERTriage.Domain.Patients;

namespace ERTriage.Application.Tests;

public class ChangePatientStatusRequestValidatorTests
{
    [Fact]
    public void Validate_InTreatmentWithoutNote_IsValid() =>
        Assert.Empty(ChangePatientStatusRequestValidator.Validate(new(PatientStatus.InTreatment, null)));

    [Theory]
    [InlineData(PatientStatus.Discharged)]
    [InlineData(PatientStatus.Admitted)]
    public void Validate_DispositionWithNote_IsValid(PatientStatus target) =>
        Assert.Empty(ChangePatientStatusRequestValidator.Validate(new(target, "Treated and released.")));

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    public void Validate_NoteLengthBoundaries_AreValid(int length) =>
        Assert.Empty(ChangePatientStatusRequestValidator.Validate(new(PatientStatus.Discharged, new string('n', length))));

    [Fact]
    public void Validate_NoteOver500_IsRejected()
    {
        var error = Assert.Single(ChangePatientStatusRequestValidator.Validate(new(PatientStatus.Admitted, new string('n', 501))));

        Assert.Equal("dispositionNote", error.Field);
    }

    [Theory]
    [InlineData(PatientStatus.Discharged, null)]
    [InlineData(PatientStatus.Discharged, "")]
    [InlineData(PatientStatus.Admitted, "   ")]
    public void Validate_DispositionWithoutNote_IsRejected(PatientStatus target, string? note)
    {
        var error = Assert.Single(ChangePatientStatusRequestValidator.Validate(new(target, note)));

        Assert.Equal("dispositionNote", error.Field);
    }

    [Fact]
    public void Validate_InTreatmentWithNote_IsRejected()
    {
        var error = Assert.Single(ChangePatientStatusRequestValidator.Validate(new(PatientStatus.InTreatment, "not allowed")));

        Assert.Equal("dispositionNote", error.Field);
    }

    [Fact]
    public void Validate_MissingTarget_IsRejected()
    {
        var error = Assert.Single(ChangePatientStatusRequestValidator.Validate(new(null, "note")));

        Assert.Equal("targetStatus", error.Field);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Validate_UndefinedTarget_IsRejected(int value)
    {
        var error = Assert.Single(ChangePatientStatusRequestValidator.Validate(new((PatientStatus)value, null)));

        Assert.Equal("targetStatus", error.Field);
    }

    [Fact]
    public void Validate_WaitingTarget_IsLeftToTheDomainTransitionRule() =>
        Assert.Empty(ChangePatientStatusRequestValidator.Validate(new(PatientStatus.Waiting, null)));

    [Fact]
    public void Validate_NullRequest_ReturnsOneBodyError() =>
        Assert.Null(Assert.Single(ChangePatientStatusRequestValidator.Validate(null)).Field);

    [Fact]
    public void EnsureValid_Invalid_ThrowsValidationException() =>
        Assert.Throws<ValidationException>(() => ChangePatientStatusRequestValidator.EnsureValid(new(null, null)));
}
