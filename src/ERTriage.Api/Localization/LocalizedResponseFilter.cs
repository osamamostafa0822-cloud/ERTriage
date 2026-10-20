using ERTriage.Application.Common;
using ERTriage.Application.Patients.Dtos;
using ERTriage.Application.Triage.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERTriage.Api.Localization;

/// <summary>Localizes display-only DTO fields immediately before MVC serializes a response.</summary>
public sealed class LocalizedResponseFilter(TriageReasonLocalizer reasons) : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult result) return;

        result.Value = result.Value switch
        {
            PatientDto patient => Localize(patient),
            TriageBoardItemDto item => Localize(item),
            PagedResult<TriageBoardItemDto> page => Localize(page),
            GeneralResponse<PatientDto> response => Map(response, Localize),
            GeneralResponse<TriageBoardItemDto> response => Map(response, Localize),
            GeneralResponse<PagedResult<TriageBoardItemDto>> response => Map(response, Localize),
            _ => result.Value
        };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    private PatientDto Localize(PatientDto patient) =>
        patient with { PriorityReason = reasons.Localize(patient.PriorityReason) };

    private TriageBoardItemDto Localize(TriageBoardItemDto item) =>
        item with { PriorityReason = reasons.Localize(item.PriorityReason) };

    private PagedResult<TriageBoardItemDto> Localize(PagedResult<TriageBoardItemDto> page) =>
        page with { Items = page.Items.Select(Localize).ToArray() };

    private static GeneralResponse<T> Map<T>(GeneralResponse<T> response, Func<T, T> map) where T : class =>
        new()
        {
            Success = response.Success,
            Message = response.Message,
            Data = response.Data is null ? null : map(response.Data),
            Errors = response.Errors,
            TraceId = response.TraceId
        };
}
