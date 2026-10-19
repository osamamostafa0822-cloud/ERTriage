using ERTriage.Application.Common;

namespace ERTriage.Application.Tests;

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(101, 100, 2)]
    public void TotalPages_Computed_RoundsUp(int totalCount, int pageSize, int expectedPages)
    {
        var result = new PagedResult<int>([], totalCount, 1, pageSize);

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void Constructor_Created_ExposesMetadata()
    {
        var result = new PagedResult<string>(["a", "b"], 12, 2, 5);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(12, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(5, result.PageSize);
    }
}
