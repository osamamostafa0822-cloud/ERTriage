using ERTriage.Domain.Common;
using ERTriage.Domain.Triage;

namespace ERTriage.Domain.Patients;

/// <summary>
/// Aggregate root: one ER visit. All business invariants live here (registration validation, priority
/// calculation, mandatory override reason, status transitions) - not in controllers or EF Core.
///
/// Priority model (D-04): <see cref="CalculatedPriority"/> (from the vitals) and <see cref="OverridePriority"/>
/// (nurse decision) are separate. <see cref="CurrentPriority"/> is persisted for DB-level sorting but is only
/// ever assigned by <c>RefreshCurrentPriority</c>, so it always equals <c>OverridePriority ?? CalculatedPriority</c>.
///
/// Triage rules are ILLUSTRATIVE and NOT CLINICALLY VALIDATED. Timestamps are UTC and come from the supplied
/// <see cref="TimeProvider"/> (read once per operation). Persistence mapping is added in M3.
/// </summary>
public sealed class Patient
{
    // For EF Core (M3). Domain code creates patients through Register.
    private Patient()
    {
    }

    public Guid Id { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public int Age { get; private set; }

    public string ChiefComplaint { get; private set; } = string.Empty;

    public VitalSigns Vitals { get; private set; } = null!;

    public DateTime ArrivalTime { get; private set; }

    public int CalculatedPriority { get; private set; }

    public string PriorityReason { get; private set; } = string.Empty;

    public int? OverridePriority { get; private set; }

    public string? OverrideReason { get; private set; }

    public string? OverriddenBy { get; private set; }

    public DateTime? OverriddenAt { get; private set; }

    public int CurrentPriority { get; private set; }

    public PatientStatus Status { get; private set; }

    public DateTime? TreatmentStartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? DispositionNote { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Optimistic-concurrency token (SQL Server rowversion), managed by EF Core; never set by the Domain.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool IsOverridden => OverridePriority.HasValue;

    public static Patient Register(
        string? fullName,
        int age,
        string? chiefComplaint,
        VitalSigns? vitals,
        DateTimeOffset arrivalTime,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();

        var name = Guard.RequiredText(fullName, MessageCatalog.Get("FullName"), PatientRules.FullNameMinLength, PatientRules.FullNameMaxLength);
        Guard.InRange(age, PatientRules.AgeMin, PatientRules.AgeMax, MessageCatalog.Get("Age"), MessageCatalog.Get("Years"));
        var complaint = Guard.RequiredText(
            chiefComplaint, MessageCatalog.Get("ChiefComplaint"), PatientRules.ChiefComplaintMinLength, PatientRules.ChiefComplaintMaxLength);

        if (vitals is null)
        {
            throw new DomainValidationException(MessageCatalog.Get("VitalsRequired"));
        }

        if (arrivalTime > now + PatientRules.ArrivalFutureTolerance)
        {
            throw new DomainValidationException(MessageCatalog.Format("ArrivalFuture", (int)PatientRules.ArrivalFutureTolerance.TotalMinutes));
        }

        var assessment = TriagePriorityCalculator.Calculate(vitals);

        var patient = new Patient
        {
            // CreateVersion7 has no TimeProvider overload: pass the provider's time so the id is testable.
            Id = Guid.CreateVersion7(now),
            FullName = name,
            Age = age,
            ChiefComplaint = complaint,
            Vitals = vitals,
            ArrivalTime = arrivalTime.UtcDateTime,
            CalculatedPriority = assessment.Priority,
            PriorityReason = assessment.Reason,
            Status = PatientStatus.Waiting,
            CreatedAt = now.UtcDateTime
        };

        patient.RefreshCurrentPriority();
        return patient;
    }

    /// <summary>
    /// Nurse override of the calculated priority. A reason is mandatory; allowed while Waiting or InTreatment.
    /// A new override replaces the previous one. Everything is validated before anything is changed.
    /// </summary>
    public void ApplyPriorityOverride(int priority, string? reason, string? overriddenBy, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();

        if (Status is PatientStatus.Discharged or PatientStatus.Admitted)
        {
            throw new PatientStatusConflictException("override priority", Status.ToString());
        }

        if (!TriagePriority.IsValid(priority))
        {
            throw new DomainValidationException(
                MessageCatalog.Format("PriorityRange", TriagePriority.MostUrgent, TriagePriority.LeastUrgent));
        }

        var validReason = Guard.RequiredText(
            reason, MessageCatalog.Get("OverrideReason"), PatientRules.OverrideReasonMinLength, PatientRules.OverrideReasonMaxLength);
        var validOverriddenBy = Guard.OptionalText(overriddenBy, MessageCatalog.Get("OverriddenBy"), PatientRules.OverriddenByMaxLength);

        OverridePriority = priority;
        OverrideReason = validReason;
        OverriddenBy = validOverriddenBy;
        OverriddenAt = now.UtcDateTime;
        RefreshCurrentPriority();
    }

    /// <summary>
    /// Moves the patient to <paramref name="target"/> if the transition matrix allows it. The disposition note is
    /// required for Discharged/Admitted and not allowed for InTreatment. An invalid transition is reported first.
    /// </summary>
    public void ChangeStatus(PatientStatus target, string? dispositionNote, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();

        if (!PatientStatusTransitions.IsAllowed(Status, target))
        {
            throw new InvalidStatusTransitionException(Status.ToString(), target.ToString());
        }

        string? note = null;

        if (target is PatientStatus.Discharged or PatientStatus.Admitted)
        {
            note = Guard.RequiredText(
                dispositionNote, MessageCatalog.Get("DispositionNote"), PatientRules.DispositionNoteMinLength, PatientRules.DispositionNoteMaxLength);
        }
        else if (!string.IsNullOrWhiteSpace(dispositionNote))
        {
            throw new DomainValidationException(MessageCatalog.Get("DispositionNotAllowed"));
        }

        Status = target;

        if (target == PatientStatus.InTreatment)
        {
            TreatmentStartedAt = now.UtcDateTime;
        }
        else
        {
            CompletedAt = now.UtcDateTime;
            DispositionNote = note;
        }
    }

    /// <summary>Statuses this patient may move to next (for DTOs; the UI never duplicates the matrix).</summary>
    public IReadOnlyList<PatientStatus> AllowedTransitions() => PatientStatusTransitions.From(Status);

    // The only place CurrentPriority is assigned (decision D-33).
    private void RefreshCurrentPriority() => CurrentPriority = OverridePriority ?? CalculatedPriority;
}
