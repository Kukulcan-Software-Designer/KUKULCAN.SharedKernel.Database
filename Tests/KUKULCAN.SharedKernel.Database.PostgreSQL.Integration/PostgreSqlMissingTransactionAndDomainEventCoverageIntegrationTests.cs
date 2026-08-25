using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.Database.UnitOfWork;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class PostgreSqlMissingTransactionAndDomainEventCoverageIntegrationTests
{
    [Test]
    public async Task EndTransaction_ShouldRollbackSavedChangesObservedFromNewPostgreSqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        const string name = "End transaction independent context";

        await using (var context = await IntegrationTestDatabase.CreateContextAsync(tenantId))
        {
            await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
            {
                TenantId = tenantId,
                Name = name
            });

            await unit.SaveChangesAsync();
            await unit.EndTransactionAsync();
        }

        await using var verificationContext = await IntegrationTestDatabase.CreateContextAsync(tenantId);
        Assert.That(
            await verificationContext.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == name),
            Is.False);
    }

    [Test]
    public async Task CommitTransaction_ShouldPersistEntityAndDispatchDomainEventAgainstNewPostgreSqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        const string name = "Commit with event";
        var domainEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);

        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using (var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
                         options,
                         new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
                         new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
                         dispatcher.Object))
        {
            await context.Database.EnsureCreatedAsync();
            await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            var entity = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity
            {
                TenantId = tenantId,
                Name = name
            };
            entity.AddDomainEventForTest(domainEvent);
            context.DomainEventEntities.Add(entity);

            await unit.CommitTransactionAsync();
        }

        await using var verificationContext = await IntegrationTestDatabase.CreateContextAsync(tenantId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await verificationContext.DomainEventEntities.AnyAsync(x => x.Name == name),
                Is.True);
            dispatcher.Verify(
                x => x.DispatchAsync(domainEvent, It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Test]
    public async Task CommitTransaction_ShouldDispatchAllDomainEventsAgainstRealPostgreSql()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var firstEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow);
        var secondEvent = new PostgreSqlDatabaseIntegrationTests.TestDomainEvent(PostgreSqlDatabaseIntegrationTests.FixedNow.AddSeconds(1));
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = IntegrationTestDatabase.ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false }
        });

        await using var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            options,
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            dispatcher.Object);
        await context.Database.EnsureCreatedAsync();
        await using var unit = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);
        await unit.BeginTransactionAsync();

        var first = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = tenantId, Name = "First event" };
        var second = new PostgreSqlDatabaseIntegrationTests.DomainEventEntity { TenantId = tenantId, Name = "Second event" };
        first.AddDomainEventForTest(firstEvent);
        second.AddDomainEventForTest(secondEvent);
        context.DomainEventEntities.AddRange(first, second);

        await unit.CommitTransactionAsync();

        using (Assert.EnterMultipleScope())
        {
            dispatcher.Verify(
                x => x.DispatchAsync(firstEvent, It.IsAny<CancellationToken>()),
                Times.Once);
            dispatcher.Verify(
                x => x.DispatchAsync(secondEvent, It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(first.DomainEvents, Is.Empty);
            Assert.That(second.DomainEvents, Is.Empty);
        }
    }
}
