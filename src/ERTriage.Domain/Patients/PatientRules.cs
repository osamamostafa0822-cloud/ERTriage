namespace ERTriage.Domain.Patients;

/// <summary>Business-rule limits for a patient record. Shared so Application validators never duplicate numbers.</summary>
public static class PatientRules
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;

    public const int AgeMin = 0;
    public const int AgeMax = 120;

    public const int ChiefComplaintMinLength = 2;
    public const int ChiefComplaintMaxLength = 200;

    public const int OverrideReasonMinLength = 3;
    public const int OverrideReasonMaxLength = 500;
    public const int OverriddenByMaxLength = 100;

    public const int DispositionNoteMinLength = 1;
    public const int DispositionNoteMaxLength = 500;

    /// <summary>Arrival may be at most this far ahead of the clock (small client/server clock differences).</summary>
    public static readonly TimeSpan ArrivalFutureTolerance = TimeSpan.FromMinutes(5);
}
