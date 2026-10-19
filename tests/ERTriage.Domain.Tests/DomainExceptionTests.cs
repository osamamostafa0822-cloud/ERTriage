using ERTriage.Domain.Common;

namespace ERTriage.Domain.Tests;

public class DomainExceptionTests
{
    [Fact]
    public void InvalidStatusTransitionException_Created_BuildsClientSafeMessage()
    {
        var exception = new InvalidStatusTransitionException("Waiting", "Discharged");

        Assert.Equal("Cannot change status from Waiting to Discharged.", exception.Message);
        Assert.Equal("Waiting", exception.From);
        Assert.Equal("Discharged", exception.To);
    }

    [Fact]
    public void DomainExceptions_Created_DeriveFromDomainException()
    {
        Assert.IsAssignableFrom<DomainException>(new DomainValidationException("Override reason is required."));
        Assert.IsAssignableFrom<DomainException>(new InvalidStatusTransitionException("A", "B"));
    }
}
