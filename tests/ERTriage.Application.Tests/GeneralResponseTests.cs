using ERTriage.Application.Common;

namespace ERTriage.Application.Tests;

public class GeneralResponseTests
{
    [Fact]
    public void Ok_WithData_IsSuccessWithoutErrors()
    {
        var response = GeneralResponse<int>.Ok(42, "Done.");

        Assert.True(response.Success);
        Assert.Equal("Done.", response.Message);
        Assert.Equal(42, response.Data);
        Assert.Empty(response.Errors);
        Assert.Null(response.TraceId);
    }

    [Fact]
    public void Fail_WithErrors_IsFailureWithTraceIdAndNoData()
    {
        var errors = new[] { new ApiError("name", "Name is required.") };

        var response = GeneralResponse<string>.Fail("Validation failed.", errors, "trace-1");

        Assert.False(response.Success);
        Assert.Equal("Validation failed.", response.Message);
        Assert.Null(response.Data);
        Assert.Equal("trace-1", response.TraceId);
        Assert.Equal("name", Assert.Single(response.Errors).Field);
    }

    [Fact]
    public void Fail_WithoutErrors_HasEmptyErrorList()
    {
        var response = GeneralResponse<string>.Fail("Nope.");

        Assert.False(response.Success);
        Assert.Empty(response.Errors);
    }
}
