using ERTriage.Api.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace ERTriage.Api.IntegrationTests;

internal static class LocalizationTestServices
{
    private static readonly ServiceProvider Services = new ServiceCollection()
        .AddLogging().AddTranslationResources().BuildServiceProvider();

    public static MessageLocalizer Translator => Services.GetRequiredService<MessageLocalizer>();
}
