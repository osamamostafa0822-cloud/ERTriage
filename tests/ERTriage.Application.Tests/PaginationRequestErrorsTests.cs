using ERTriage.Application.Common;

namespace ERTriage.Application.Tests;

public class PaginationRequestErrorsTests
{
    [Fact]
    public void GetErrors_ValidRequest_IsEmpty() =>
        Assert.Empty(new PaginationRequest(2, 50).GetErrors());

    [Fact]
    public void GetErrors_InvalidRequest_ReportsBothFieldsWithoutThrowing() =>
        Assert.Equal(
            ["pageNumber", "pageSize"],
            new PaginationRequest(0, 0).GetErrors().Select(e => e.Field).Order(StringComparer.Ordinal));

    [Fact]
    public void EnsureValid_InvalidRequest_ThrowsTheSameErrorsGetErrorsReports()
    {
        var request = new PaginationRequest(0, 101);

        var exception = Assert.Throws<ValidationException>(request.EnsureValid);

        Assert.Equal(request.GetErrors(), exception.Errors);
    }
}
