namespace ERTriage.Api.IntegrationTests.Persistence;

/// <summary>
/// A test that needs SQL Server. Skipped (with the reason) only when no server is reachable and the tests are not
/// mandatory; in CI, or with <c>ERTRIAGE_REQUIRE_SQLSERVER=true</c>, it is never skipped, so a missing server fails.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        var reason = SqlServerTestServer.SkipReason(Environment.GetEnvironmentVariable);

        if (reason is not null)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// The D-38 measurement. Opt-in: skipped unless <c>ERTRIAGE_RUN_D38=1</c>. Once requested it follows the SQL Server
/// rules above (skip when no server and not mandatory, otherwise run/fail).
/// </summary>
public sealed class D38ExperimentFactAttribute : FactAttribute
{
    public D38ExperimentFactAttribute()
    {
        var reason = SqlServerTestPolicy.IsExperimentRequested(Environment.GetEnvironmentVariable)
            ? SqlServerTestServer.SkipReason(Environment.GetEnvironmentVariable)
            : $"Opt-in measurement (D-38): set {SqlServerTestPolicy.ExperimentVariable}=1 to run it.";

        if (reason is not null)
        {
            Skip = reason;
        }
    }
}

/// <summary>The theory counterpart of <see cref="SqlServerFactAttribute"/> with the same skip/mandatory rules.</summary>
public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute()
    {
        var reason = SqlServerTestServer.SkipReason(Environment.GetEnvironmentVariable);

        if (reason is not null)
        {
            Skip = reason;
        }
    }
}
