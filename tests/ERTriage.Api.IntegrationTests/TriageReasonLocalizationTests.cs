using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using ERTriage.Api.Localization;
using ERTriage.Domain.Triage;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests;

/// <summary>
/// Guards the link between <see cref="TriageThresholds"/> (English band descriptions stored in PriorityReason) and
/// <see cref="TriageReasonLocalizer"/> (description -> resource key). The localizer deliberately returns an unknown
/// description verbatim, so a band added or renamed without a mapping would silently show English in Arabic. The
/// theory data is generated from the thresholds themselves, so such a change fails here. No database needed.
/// </summary>
public class TriageReasonLocalizationTests
{
    // Vital names written by TriagePriorityCalculator and recognised by TriageReasonLocalizer.
    private static readonly (string Vital, IReadOnlyList<PriorityBand> Bands)[] Vitals =
    [
        ("SpO2", TriageThresholds.SpO2),
        ("HR", TriageThresholds.HeartRate),
        ("SBP", TriageThresholds.SystolicBp),
        ("Temp", TriageThresholds.Temperature)
    ];

    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddLogging().AddTranslationResources().BuildServiceProvider();

    private static TriageReasonLocalizer Localizer => Services.GetRequiredService<TriageReasonLocalizer>();

    public static TheoryData<string, string> Bands()
    {
        var data = new TheoryData<string, string>();

        foreach (var (vital, bands) in Vitals)
        {
            foreach (var band in bands)
            {
                data.Add(vital, band.Description);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Bands))]
    public void EveryThresholdBand_HasMappingAndTranslationInBothLanguages(string vital, string description)
    {
        var finding = $"{vital} 42 {description}";

        // English resources must reproduce the stored description exactly (a drifted English text would change the board).
        Assert.Equal(finding, LocalizeIn("en", finding));

        // Arabic must not fall back to the stored English: label and band both translated, value untouched.
        var arabic = LocalizeIn("ar", finding);
        Assert.NotEqual(finding, arabic);
        Assert.DoesNotContain(description, arabic, StringComparison.Ordinal);
        Assert.False(arabic.StartsWith(vital + " ", StringComparison.Ordinal), $"'{vital}' label was not translated.");
        Assert.Contains(" 42 ", arabic, StringComparison.Ordinal);
    }

    [Fact]
    public void BandResourceKeys_MatchTheNumberOfThresholdBands_InBothLanguages()
    {
        var expected = Vitals.Sum(vital => vital.Bands.Count);
        var manager = new ResourceManager("ERTriage.Api.Localization.Messages", typeof(Messages).Assembly);

        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("ar") })
        {
            var set = manager.GetResourceSet(culture, true, false)!;
            var bandKeys = set.Cast<DictionaryEntry>()
                .Select(entry => (string)entry.Key)
                .Where(key => Regex.IsMatch(key, @"^Band\d+$", RegexOptions.CultureInvariant))
                .ToArray();

            Assert.Equal(expected, bandKeys.Length);
        }
    }

    [Fact]
    public void AllNormalReason_IsTranslated()
    {
        Assert.Equal(TriagePriorityCalculator.AllNormalReason, LocalizeIn("en", TriagePriorityCalculator.AllNormalReason));
        Assert.NotEqual(
            TriagePriorityCalculator.AllNormalReason,
            LocalizeIn("ar", TriagePriorityCalculator.AllNormalReason));
    }

    [Fact]
    public void UnmappedDescription_IsReturnedVerbatim()
    {
        // Documents the silent fallback the tests above exist to prevent.
        const string finding = "HR 42 in 1–2";

        Assert.Equal(finding, LocalizeIn("ar", finding));
    }

    private static string LocalizeIn(string culture, string reason)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            return Localizer.Localize(reason);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
