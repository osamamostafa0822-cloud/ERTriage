using ERTriage.Application.Common;

namespace ERTriage.Application.Tests;

public class PaginationRequestTests
{
    [Fact]
    public void Defaults_NotSpecified_AreFirstPageAndTenItems()
    {
        var request = new PaginationRequest();

        Assert.Equal(1, request.PageNumber);
        Assert.Equal(10, request.PageSize);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(5, 100)]
    public void EnsureValid_InRange_DoesNotThrow(int pageNumber, int pageSize)
    {
        new PaginationRequest(pageNumber, pageSize).EnsureValid();
    }

    [Theory]
    [InlineData(0, 10, "pageNumber")]
    [InlineData(-1, 10, "pageNumber")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, -5, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void EnsureValid_OutOfRange_ThrowsValidationExceptionForField(int pageNumber, int pageSize, string field)
    {
        var exception = Assert.Throws<ValidationException>(() => new PaginationRequest(pageNumber, pageSize).EnsureValid());

        var error = Assert.Single(exception.Errors);
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void EnsureValid_BothInvalid_ReportsBothFields()
    {
        var exception = Assert.Throws<ValidationException>(() => new PaginationRequest(0, 0).EnsureValid());

        Assert.Equal(2, exception.Errors.Count);
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 25, 50)]
    public void Skip_ValidRequest_ReturnsZeroBasedOffset(int pageNumber, int pageSize, int expected)
    {
        Assert.Equal(expected, new PaginationRequest(pageNumber, pageSize).Skip);
    }

    [Fact]
    public void Skip_HugePageNumber_DoesNotOverflow()
    {
        Assert.Equal(int.MaxValue, new PaginationRequest(int.MaxValue, 100).Skip);
    }
}
