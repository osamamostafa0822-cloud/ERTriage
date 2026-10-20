using System.Diagnostics;
using ERTriage.Application.Common;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ERTriage.Api.Localization;

public static class LocalizationExtensions
{
    public static IServiceCollection AddTranslationResources(this IServiceCollection services)
    {
        services.AddLocalization();
        services.AddSingleton<MessageLocalizer>();
        services.AddSingleton<TriageReasonLocalizer>();
        services.AddScoped<LocalizedResponseFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<LocalizedResponseFilter>());
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture("en")
                .AddSupportedCultures("en", "ar")
                .AddSupportedUICultures("en", "ar");
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
            options.ApplyCurrentCultureToResponseHeaders = true;
        });

        services.AddOptions<ApiBehaviorOptions>().Configure<MessageLocalizer>((options, localizer) =>
        {
            // Framework binding errors can contain raw input or English exception messages.
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .Select(entry => new ApiError(entry.Key, localizer.Get("InvalidRequest")))
                    .ToArray();
                return new BadRequestObjectResult(GeneralResponse<object>.Fail(
                    localizer.Get("ValidationFailed"), errors, Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
            };
        });
        return services;
    }
}
