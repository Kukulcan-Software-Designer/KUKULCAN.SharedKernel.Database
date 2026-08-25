using KUKULCAN.SharedKernel.Database.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class ProviderConfigurationAdditionalIntegrationTests
{
    [Test]
    public void ConfigureProvider_ShouldRejectUnsupportedProvider()
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = (DatabaseProvider)999,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
        {
            using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                options,
                new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(Guid.NewGuid()),
                new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                Mock.Of<KUKULCAN.SharedKernel.DomainEvents.Abstractions.IDomainEventDispatcher>());

            _ = context.Database.ProviderName;
        })!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("999"));
            Assert.That(exception.Message, Does.Contain("not supported"));
        }
    }
}
