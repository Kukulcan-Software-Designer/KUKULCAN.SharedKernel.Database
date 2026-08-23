using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class DomainEventDispatchAdditionalIntegrationTests
{
    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldDispatchAllEventsFromMultipleAggregatesAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();

        await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            CreateOptions(),
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            dispatcher.Object);

        await context.Database.EnsureCreatedAsync();

        var firstEntity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
        {
            TenantId = tenantId,
            Name = "First event source"
        };
        var secondEntity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
        {
            TenantId = tenantId,
            Name = "Second event source"
        };

        var firstEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        var secondEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow.AddSeconds(1));
        var thirdEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow.AddSeconds(2));

        firstEntity.AddDomainEventForTest(firstEvent);
        firstEntity.AddDomainEventForTest(secondEvent);
        secondEntity.AddDomainEventForTest(thirdEvent);

        context.DomainEventEntities.AddRange(firstEntity, secondEntity);
        await context.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstEntity.DomainEvents, Is.Empty);
            Assert.That(secondEntity.DomainEvents, Is.Empty);
            dispatcher.Verify(x => x.DispatchAsync(firstEvent, It.IsAny<CancellationToken>()), Times.Once);
            dispatcher.Verify(x => x.DispatchAsync(secondEvent, It.IsAny<CancellationToken>()), Times.Once);
            dispatcher.Verify(x => x.DispatchAsync(thirdEvent, It.IsAny<CancellationToken>()), Times.Once);
            dispatcher.Verify(x => x.DispatchAsync(It.IsAny<IDomainEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }
    }

    [Test]
    public async Task DomainEventDispatchInterceptor_ShouldPropagateSaveChangesCancellationTokenAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        using var cancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellation.Token;

        await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            CreateOptions(),
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            dispatcher.Object);

        await context.Database.EnsureCreatedAsync();

        var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
        {
            TenantId = tenantId,
            Name = "Cancellation event source"
        };
        var domainEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        entity.AddDomainEventForTest(domainEvent);
        context.DomainEventEntities.Add(entity);

        await context.SaveChangesAsync(cancellationToken);

        dispatcher.Verify(x => x.DispatchAsync(domainEvent, cancellationToken), Times.Once);
        Assert.That(entity.DomainEvents, Is.Empty);
    }

    private static IOptions<KukulcanDatabaseOptions> CreateOptions()
        => Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });
}
