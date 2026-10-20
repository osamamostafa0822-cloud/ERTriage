using ERTriage.Api.Configuration;
using ERTriage.Api.IntegrationTests.Persistence;
using ERTriage.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ERTriage.Api.IntegrationTests.Http;

/// <summary>
/// Hosts the real API (the real <c>Program</c>, middleware, controllers, services, repositories) in memory, in the
/// <c>Test</c> environment, against one generated <c>ERTriage_Test_{hex}</c> database. Only three things differ from a
/// real run: the connection string, the clock (<see cref="FixedTimeProvider"/>) and the log sink. Auto-migration stays
/// off; the schema is applied by <see cref="SqlServerFixture.EnsureSchemaAsync"/> to the generated database only.
/// </summary>
public sealed class ErTriageApiFactory(string connectionString, TimeProvider clock, ILoggerProvider logs)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(ApiConfiguration.TestEnvironment);
        builder.UseSetting(ApiConfiguration.ConnectionStringKey, connectionString);
        builder.UseSetting(ApiConfiguration.MigrateOnStartupKey, "false");

        builder.ConfigureLogging(logging =>
        {
            // No console/debug/event-log output from tests; everything goes to the capturing provider.
            logging.ClearProviders();
            logging.AddProvider(logs);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);

            // Second line of defence: whichever configuration source wins, the DbContext targets the generated
            // database (EF Core composes AddDbContext calls in order; the last UseSqlServer sets the connection).
            // EnsureTargets verifies the result before any test runs.
            services.AddDbContext<ErTriageDbContext>(options => options.UseSqlServer(connectionString));
        });
    }

    /// <summary>
    /// Starts the host and proves that the API's DbContext points at <paramref name="expectedDatabase"/>, a generated
    /// test database. Throws otherwise, so no request is ever sent to ERTriage, ERTriage_Test or Production.
    /// </summary>
    public void EnsureTargets(string expectedDatabase)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErTriageDbContext>();
        var actual = context.Database.GetDbConnection().Database;

        if (!SqlServerTestPolicy.IsSafeDatabaseName(actual)
            || !string.Equals(actual, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The API under test is not connected to the generated test database; refusing to run HTTP tests.");
        }
    }
}
