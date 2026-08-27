using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class ConfigureProviderUnsupportedOnConfiguringCoverageTests
{
    [Test]
    public void ConfigureProvider_ThroughOnConfiguring_WithUnsupportedProvider_ShouldThrow()
    {
        using var context = new ProviderTestDbContext(
            Options.Create(new KukulcanDatabaseOptions
            {
                Provider = (DatabaseProvider)999,
                ConnectionString = "Host=unit-test;Database=unit-test"
            }),
            new TestTenantContext(Guid.NewGuid()),
            new TestClock(DateTimeOffset.UtcNow),
            Mock.Of<IDomainEventDispatcher>());

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() =>
            _ = context.Database.ProviderName)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("999"));
            Assert.That(exception.Message, Does.Contain("not supported"));
        }
    }

    private sealed class ProviderTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
    }
}
