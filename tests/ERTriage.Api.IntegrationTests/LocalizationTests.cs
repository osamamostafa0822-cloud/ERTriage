using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using ERTriage.Api.ExceptionHandling;
using ERTriage.Api.Localization;
using ERTriage.Application.Common;
using ERTriage.Application.Patients;
using ERTriage.Application.Patients.Requests;
using ERTriage.Application.Patients.Validation;
using ERTriage.Application.Triage;
using ERTriage.Domain.Common;
using ERTriage.Domain.Patients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace ERTriage.Api.IntegrationTests;

public class LocalizationTests
{
    [Theory]
    [InlineData("ar", "فشل التحقق من صحة البيانات.")]
    [InlineData("ar-EG", "فشل التحقق من صحة البيانات.")]
    [InlineData("en", "Validation failed.")]
    [InlineData("fr", "Validation failed.")]
    [InlineData("", "Validation failed.")]
    [InlineData("en;q=0.5,ar;q=0.9", "فشل التحقق من صحة البيانات.")]
    public async Task RequestLanguage_LocalizesErrorEnvelope(string language, string expected)
    {
        using var services = new ServiceCollection().AddLogging().AddTranslationResources().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestLocalization();
        app.Run(async context =>
        {
            var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, LocalizationTestServices.Translator);
            await handler.TryHandleAsync(context,
                new ValidationException(RegisterPatientRequestValidator.Validate(null, DateTimeOffset.UtcNow)),
                CancellationToken.None);
        });
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.AcceptLanguage = language;
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal(expected, response.RootElement.GetProperty("message").GetString());
        Assert.Equal(expected.StartsWith("فشل") ? "محتوى الطلب مطلوب." : "Request body is required.",
            response.RootElement.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void Arabic_TranslatesValidationAndMappingWithoutChangingPatientData()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            var now = DateTimeOffset.UtcNow;
            var patient = Patient.Register("Test Patient", 30, "Original complaint",
                VitalSigns.Create(112, 138, 86, 37.9m, 88), now, TimeProvider.System);
            var dto = PatientMapper.ToDto(patient);
            Assert.Equal("SpO2 88% < 90", patient.PriorityReason);
            Assert.Equal(patient.PriorityReason, dto.PriorityReason);
            using var services = new ServiceCollection().AddLogging().AddTranslationResources().BuildServiceProvider();
            var filter = services.GetRequiredService<LocalizedResponseFilter>();
            var result = new ObjectResult(GeneralResponse<ERTriage.Application.Patients.Dtos.PatientDto>.Ok(dto));
            var action = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
            filter.OnResultExecuting(new ResultExecutingContext(action, [], result, new object()));
            var localized = Assert.IsType<GeneralResponse<ERTriage.Application.Patients.Dtos.PatientDto>>(result.Value).Data!;
            Assert.Equal("تشبع الأكسجين 88% أقل من 90", localized.PriorityReason);
            Assert.Equal("SpO2 88% < 90", dto.PriorityReason);
            var item = TriageBoardMapper.ToItem(patient, now);
            var page = new PagedResult<ERTriage.Application.Triage.Dtos.TriageBoardItemDto>([item], 1, 1, 10);
            var boardResult = new ObjectResult(
                GeneralResponse<PagedResult<ERTriage.Application.Triage.Dtos.TriageBoardItemDto>>.Ok(page));
            filter.OnResultExecuting(new ResultExecutingContext(action, [], boardResult, new object()));
            var translatedPage = Assert.IsType<
                GeneralResponse<PagedResult<ERTriage.Application.Triage.Dtos.TriageBoardItemDto>>>(boardResult.Value).Data!;
            Assert.Equal(localized.PriorityReason, Assert.Single(translatedPage.Items).PriorityReason);
            Assert.Equal(page.TotalCount, translatedPage.TotalCount);
            Assert.Equal(patient.PriorityReason, item.PriorityReason);
            Assert.Equal(patient.FullName, localized.FullName);
            Assert.Equal(patient.ChiefComplaint, localized.ChiefComplaint);
            Assert.Equal(patient.FullName, dto.FullName);
            Assert.Equal(patient.ChiefComplaint, dto.ChiefComplaint);
            Assert.Equal(patient.Status, dto.Status);

            var errors = RegisterPatientRequestValidator.Validate(
                new RegisterPatientRequest(null, null, null, null, null, null, null, null, null), now);
            var nameError = Assert.Single(errors, error => error.Field == "fullName");
            Assert.Equal("Full name is required.", nameError.Message);
            var translator = services.GetRequiredService<MessageLocalizer>();
            Assert.Equal("الاسم الكامل مطلوب.", translator.Localize(nameError.Description!));
            Assert.Equal("يجب أن يكون حجم الصفحة بين 1 و100.",
                translator.Localize(new PaginationRequest(1, 101).GetErrors().Single().Description!));
            var domainError = Assert.Throws<DomainValidationException>(() => VitalSigns.Create(0, 120, 80, 37, 98));
            Assert.Equal("Heart rate must be between 20 and 250 bpm.", domainError.Message);
            Assert.Equal("يجب أن تكون قيمة معدل ضربات القلب بين 20 و250 نبضة/دقيقة.",
                translator.Localize(domainError.Description!));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Resources_HaveMatchingKeysAndValidFormatsInBothLanguages()
    {
        var manager = new ResourceManager("ERTriage.Api.Localization.Messages", typeof(Messages).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        var arabic = manager.GetResourceSet(CultureInfo.GetCultureInfo("ar"), true, false)!;
        var englishKeys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray();
        Assert.Equal(englishKeys, arabic.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).Order().ToArray());
        foreach (var key in englishKeys)
        {
            var en = System.Text.CompositeFormat.Parse(english.GetString(key)!);
            var ar = System.Text.CompositeFormat.Parse(arabic.GetString(key)!);
            Assert.Equal(en.MinimumArgumentCount, ar.MinimumArgumentCount);
        }
    }

    [Fact]
    public void MissingKey_UsesDiagnosticFallbackWithoutThrowing()
    {
        Assert.Equal("Safe fallback 5", LocalizationTestServices.Translator.Localize(
            new MessageDescriptor("MissingResourceKey", "Safe fallback {0}", 5)));
    }

    [Fact]
    public async Task ArabicRequest_TranslatesDomainExceptionCreatedBeforeRequest()
    {
        var exception = new InvalidStatusTransitionException("Waiting", "Discharged");
        using var services = new ServiceCollection().AddLogging().AddTranslationResources().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestLocalization();
        app.Run(async context => await new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance, services.GetRequiredService<MessageLocalizer>())
            .TryHandleAsync(context, exception, CancellationToken.None));
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.AcceptLanguage = "ar";
        context.Response.Body = new MemoryStream();
        await app.Build()(context);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("لا يمكن تغيير الحالة من في الانتظار إلى خرج من المستشفى.",
            response.RootElement.GetProperty("message").GetString());
        Assert.Equal("Cannot change status from Waiting to Discharged.", exception.Message);
    }
}
