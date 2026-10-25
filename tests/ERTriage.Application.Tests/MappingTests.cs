using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Triage.Dtos;
using ERTriage.Domain.Patients;
using ERTriage.Application.Patients.Mapping;
using ERTriage.Application.Triage.Mapping;
using ERTriage.Application.Triage.Waiting;

namespace ERTriage.Application.Tests;

public class MappingTests
{
    [Fact]
    public void ToDto_WaitingPatient_MapsEveryField()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 12);

        var dto = PatientMapper.ToDto(patient);

        Assert.Equal(patient.Id, dto.Id);
        Assert.Equal("Test Patient Alpha", dto.FullName);
        Assert.Equal(54, dto.Age);
        Assert.Equal("Shortness of breath", dto.ChiefComplaint);
        Assert.Equal(new VitalSignsDto(112, 138, 86, 37.9m, 88), dto.Vitals);
        Assert.Equal(patient.ArrivalTime, dto.ArrivalTime);
        Assert.Equal(patient.CalculatedPriority, dto.CalculatedPriority);
        Assert.Equal(patient.PriorityReason, dto.PriorityReason);
        Assert.Equal(patient.CurrentPriority, dto.CurrentPriority);
        Assert.Null(dto.OverridePriority);
        Assert.Null(dto.OverrideReason);
        Assert.Null(dto.OverriddenBy);
        Assert.Null(dto.OverriddenAt);
        Assert.False(dto.IsOverridden);
        Assert.Equal(PatientStatus.Waiting, dto.Status);
        Assert.Null(dto.TreatmentStartedAt);
        Assert.Null(dto.CompletedAt);
        Assert.Null(dto.DispositionNote);
        Assert.Equal([PatientStatus.InTreatment], dto.AllowedTransitions);
    }

    [Fact]
    public void ToDto_OverriddenPatientInTreatment_MapsOverrideAndTransitions()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 5);
        patient.ApplyPriorityOverride(1, "Escalated by nurse", "Nurse A", clock);
        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);

        var dto = PatientMapper.ToDto(patient);

        Assert.True(dto.IsOverridden);
        Assert.Equal(1, dto.OverridePriority);
        Assert.Equal("Escalated by nurse", dto.OverrideReason);
        Assert.Equal("Nurse A", dto.OverriddenBy);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, dto.OverriddenAt);
        Assert.Equal(1, dto.CurrentPriority);
        Assert.Equal(patient.CalculatedPriority, dto.CalculatedPriority);
        Assert.Equal(PatientStatus.InTreatment, dto.Status);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, dto.TreatmentStartedAt);
        Assert.Equal([PatientStatus.Discharged, PatientStatus.Admitted], dto.AllowedTransitions);
    }

    [Fact]
    public void ToDto_DischargedPatient_MapsDispositionAndHasNoTransitions()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock);
        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);
        patient.ChangeStatus(PatientStatus.Discharged, "Released with advice.", clock);

        var dto = PatientMapper.ToDto(patient);

        Assert.Equal(PatientStatus.Discharged, dto.Status);
        Assert.Equal("Released with advice.", dto.DispositionNote);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, dto.CompletedAt);
        Assert.Empty(dto.AllowedTransitions);
    }

    [Fact]
    public void ToItem_WaitingPatient_MapsBoardFieldsAndWaitingIndicator()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 45);

        var item = TriageBoardMapper.ToItem(TestData.ToBoardRow(patient), clock.GetUtcNow());

        Assert.Equal(patient.Id, item.Id);
        Assert.Equal("Test Patient Alpha", item.FullName);
        Assert.Equal(54, item.Age);
        Assert.Equal("Shortness of breath", item.ChiefComplaint);
        Assert.Equal(patient.ArrivalTime, item.ArrivalTime);
        Assert.Equal(patient.CurrentPriority, item.CurrentPriority);
        Assert.Equal(patient.CalculatedPriority, item.CalculatedPriority);
        Assert.Equal(patient.PriorityReason, item.PriorityReason);
        Assert.False(item.IsOverridden);
        Assert.Null(item.OverrideReason);
        Assert.Equal(PatientStatus.Waiting, item.Status);
        Assert.Equal(45, item.WaitingMinutes);
        Assert.Equal(WaitingLevel.Warning, item.WaitingLevel);
        Assert.Equal([PatientStatus.InTreatment], item.AllowedTransitions);
    }

    [Fact]
    public void ToItem_OverriddenPatient_ShowsOverrideReasonAndOverridePriority()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 10);
        patient.ApplyPriorityOverride(4, "Stable on reassessment", null, clock);

        var item = TriageBoardMapper.ToItem(TestData.ToBoardRow(patient), clock.GetUtcNow());

        Assert.True(item.IsOverridden);
        Assert.Equal(4, item.CurrentPriority);
        Assert.Equal("Stable on reassessment", item.OverrideReason);
        Assert.Equal(patient.CalculatedPriority, item.CalculatedPriority);
    }

    [Fact]
    public void ToItem_CompletedPatient_HasNoWaitingIndicator()
    {
        var clock = TestData.Clock();
        var patient = TestData.NewPatient(clock, 200);
        patient.ChangeStatus(PatientStatus.InTreatment, null, clock);
        patient.ChangeStatus(PatientStatus.Admitted, "Admitted to ward.", clock);

        var item = TriageBoardMapper.ToItem(TestData.ToBoardRow(patient), clock.GetUtcNow());

        Assert.Null(item.WaitingMinutes);
        Assert.Equal(WaitingLevel.None, item.WaitingLevel);
        Assert.Empty(item.AllowedTransitions);
    }

    [Theory]
    [InlineData(typeof(PatientDto))]
    [InlineData(typeof(VitalSignsDto))]
    [InlineData(typeof(TriageBoardItemDto))]
    public void PublicDtos_NeverExposeTheConcurrencyTokenOrTheEntity(Type dtoType)
    {
        var properties = dtoType.GetProperties();

        Assert.DoesNotContain(properties, p => p.Name.Contains("RowVersion", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(Patient) || p.PropertyType == typeof(VitalSigns));
    }

    [Fact]
    public void Mappers_NullInput_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => PatientMapper.ToDto((Patient)null!));
        Assert.Throws<ArgumentNullException>(() => PatientMapper.ToDto((VitalSigns)null!));
        Assert.Throws<ArgumentNullException>(() => TriageBoardMapper.ToItem(null!, TestData.Now));
    }
}
