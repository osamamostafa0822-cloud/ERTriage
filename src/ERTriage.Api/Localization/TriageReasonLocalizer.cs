using Microsoft.Extensions.Localization;
using ERTriage.Domain.Triage;

namespace ERTriage.Api.Localization;

/// <summary>Formats stored, invariant triage explanations without changing clinical values or recalculating priority.</summary>
public sealed class TriageReasonLocalizer(IStringLocalizer<Messages> localizer)
{
    private static readonly IReadOnlyDictionary<string, string> BandKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["< 90"] = "Band0",
        ["in 90–93"] = "Band1",
        ["in 94–95"] = "Band2",
        ["in 96–100"] = "Band3",
        ["< 40"] = "Band4",
        ["> 140"] = "Band5",
        ["in 40–49"] = "Band6",
        ["in 121–140"] = "Band7",
        ["in 50–59"] = "Band8",
        ["in 101–120"] = "Band9",
        ["in 60–100"] = "Band10",
        ["< 80"] = "Band11",
        ["≥ 220"] = "Band12",
        ["in 80–89"] = "Band13",
        ["in 180–219"] = "Band14",
        ["in 90–99"] = "Band15",
        ["in 160–179"] = "Band16",
        ["in 100–159"] = "Band17",
        ["< 35.0"] = "Band18",
        ["≥ 41.0"] = "Band19",
        ["in [35.0, 35.5)"] = "Band20",
        ["in [39.5, 41.0)"] = "Band21",
        ["in [35.5, 36.0)"] = "Band22",
        ["in [38.0, 39.5)"] = "Band23",
        ["in [36.0, 38.0)"] = "Band24",
    };

    public string Localize(string reason)
    {
        if (reason == TriagePriorityCalculator.AllNormalReason)
        {
            return localizer["AllNormal"].Value;
        }

        return string.Join("; ", reason.Split("; ", StringSplitOptions.None).Select(LocalizeFinding));
    }

    private string LocalizeFinding(string finding)
    {
        var parts = finding.Split(' ', 3);
        if (parts.Length != 3 || parts[0] is not ("SpO2" or "HR" or "SBP" or "Temp")
            || !BandKeys.TryGetValue(parts[2], out var bandKey))
        {
            // Preserve unknown historical explanations verbatim.
            return finding;
        }

        return $"{localizer[parts[0]].Value} {parts[1]} {localizer[bandKey].Value}";
    }
}

