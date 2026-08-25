using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Moq;

namespace KUKULCAN.SharedKernel.Database.Integration.SQLServer;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerMissingTransactionAndDomainEventCoverageIntegrationTests
{
    [Test]
    public async Task EndTransaction_ShouldRollbackSavedChangesObservedFromNewSqlServerContext()
    {
        Guid tenantId = Guid.NewGuid();
        const string name = "End transaction independent context";

        await using (var context = await SqlServerIntegrationContextFactory.CreateAsync(tenantId))
        {
            await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            context.Entities.Add(new SqlServerIntegrationEntity
            {
                TenantId = tenantId,
                Name = name
            });

            await unit.SaveChangesAsync();
            await unit.EndTransactionAsync();
        }

        await using var verificationContext = await SqlServerIntegrationContextFactory.CreateAsync(tenantId);
        Assert.That(
            await verificationContext.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == name),
            Is.False);
    }

    [Test]
    public async Task CommitTransaction_ShouldPersistEntityAndDispatchDomainEventAgainstNewSqlServerContext()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        const string name = "Commit with event";
        var domainEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);

        await using (var context = await SqlServerIntegrationContextFactory.CreateAsync(tenantId, dispatcher.Object))
        {
            await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            var entity = new SqlServerDomainEventEntity
            {
                TenantId = tenantId,
                Name = name
            };
            entity.AddDomainEventForTest(domainEvent);
            context.DomainEventEntities.Add(entity);

            await unit.CommitTransactionAsync();
        }

        await using var verificationContext = await SqlServerIntegrationContextFactory.CreateAsync(tenantId);

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
    public async Task CommitTransaction_ShouldDispatchAllDomainEventsAgainstRealSqlServer()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var firstEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow);
        var secondEvent = new SqlServerTestDomainEvent(SqlServerIntegrationConstants.FixedNow.AddSeconds(1));

        await using var context = await SqlServerIntegrationContextFactory.CreateAsync(tenantId, dispatcher.Object);
        await using var unit = new UnitOfWork<SqlServerIntegrationDbContext>(context);
        await unit.BeginTransactionAsync();

        var first = new SqlServerDomainEventEntity { TenantId = tenantId, Name = "First event" };
        var second = new SqlServerDomainEventEntity { TenantId = tenantId, Name = "Second event" };
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
