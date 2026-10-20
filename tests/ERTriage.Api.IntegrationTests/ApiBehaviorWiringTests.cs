using ERTriage.Api.Localization;
using ERTriage.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace ERTriage.Api.IntegrationTests;

/// <summary>
/// Regression guard (found by the P4 HTTP tests): the binding-error factory registered by AddApi must survive MVC's own
/// ApiBehaviorOptions setup, so binding errors return GeneralResponse instead of ProblemDetails. No database, no host.
/// </summary>
public class ApiBehaviorWiringTests
{
    [Fact]
    public void AddApi_InvalidModelStateFactory_ReturnsGeneralResponseWithoutFrameworkMessages()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://localhost:4200" })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddApi(configuration, new HostingEnvironment { EnvironmentName = "Development" })
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.InvalidModelStateResponseFactory;
        var httpContext = new DefaultHttpContext { RequestServices = provider, TraceIdentifier = "wiring-trace" };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        actionContext.ModelState.AddModelError("$.age", "could not be converted to ERTriage.Application.Raw");

        var result = Assert.IsType<BadRequestObjectResult>(factory(actionContext));

        var response = Assert.IsType<GeneralResponse<object>>(result.Value);
        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal("wiring-trace", response.TraceId); // no Activity in this unit test, so the fallback is used
        var error = Assert.Single(response.Errors);
        Assert.Equal("$.age", error.Field);
        Assert.Equal(provider.GetRequiredService<MessageLocalizer>().Get("InvalidRequest"), error.Message);
        Assert.DoesNotContain("ERTriage", error.Message, StringComparison.Ordinal);
    }
}
