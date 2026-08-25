namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerAdditionalSlowQueryIntegrationTests
{
    [Test]
    public async Task SlowQueryInterceptor_ShouldLogRealSqlServerNonQueryCommandAboveThreshold()
    {
        var logger = new SqlServerCapturingLogger<SlowQueryInterceptor>();
        int previous = SlowQueryInterceptor.SlowQueryThresholdMs;
        SlowQueryInterceptor.SlowQueryThresholdMs = 0;
        try
        {
            var interceptor = new SlowQueryInterceptor(logger, Options.Create(new KukulcanDatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = SqlServerIntegrationDatabase.ConnectionString
            }));
            await using var context = await SqlServerIntegrationContextFactory.CreateAsync(Guid.NewGuid(), slowQueryInterceptor: interceptor);
            await context.Database.ExecuteSqlRawAsync("SELECT 1;");
            Assert.That(logger.WarningMessages, Has.Some.Contains("[SlowQuery]"));
        }
        finally
        {
            SlowQueryInterceptor.SlowQueryThresholdMs = previous;
        }
    }
}
