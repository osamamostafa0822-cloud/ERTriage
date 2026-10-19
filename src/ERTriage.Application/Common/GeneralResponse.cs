namespace ERTriage.Application.Common;

/// <summary>
/// Standard envelope for every JSON response (api-response-contract.md).
/// This type carries no HTTP concerns; the Api layer decides the status code.
/// </summary>
public sealed class GeneralResponse<T>
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public IReadOnlyList<ApiError> Errors { get; init; } = [];

    public string? TraceId { get; init; }

    public static GeneralResponse<T> Ok(T data, string message = "") =>
        new() { Success = true, Message = message, Data = data };

    public static GeneralResponse<T> Fail(
        string message,
        IReadOnlyList<ApiError>? errors = null,
        string? traceId = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [], TraceId = traceId };
}
