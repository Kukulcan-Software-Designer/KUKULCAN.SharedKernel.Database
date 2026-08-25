namespace KUKULCAN.SharedKernel.Database.MySQL.Integration;

[TestFixture]
[NonParallelizable]
public sealed class MySqlMissingTransactionAndDomainEventCoverageIntegrationTests
{
    [Test]
    public async Task EndTransaction_ShouldRollbackSavedChangesObservedFromNewMySqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        const string name = "End transaction independent context";

        await using (var context = await MySqlIntegrationContextFactory.CreateAsync(tenantId))
        {
            await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            context.Entities.Add(new MySqlIntegrationEntity
            {
                TenantId = tenantId,
                Name = name
            });

            await unit.SaveChangesAsync();
            await unit.EndTransactionAsync();
        }

        await using var verificationContext = await MySqlIntegrationContextFactory.CreateAsync(tenantId);
        Assert.That(
            await verificationContext.Entities.IgnoreQueryFilters().AnyAsync(x => x.Name == name),
            Is.False);
    }

    [Test]
    public async Task CommitTransaction_ShouldPersistEntityAndDispatchDomainEventAgainstNewMySqlContext()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        const string name = "Commit with event";
        var domainEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);

        await using (var context = await MySqlIntegrationContextFactory.CreateAsync(tenantId, dispatcher.Object))
        {
            await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
            await unit.BeginTransactionAsync();

            var entity = new MySqlDomainEventEntity
            {
                TenantId = tenantId,
                Name = name
            };
            entity.AddDomainEventForTest(domainEvent);
            context.DomainEventEntities.Add(entity);

            await unit.CommitTransactionAsync();
        }

        await using var verificationContext = await MySqlIntegrationContextFactory.CreateAsync(tenantId);

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
    public async Task CommitTransaction_ShouldDispatchAllDomainEventsAgainstRealMySql()
    {
        Guid tenantId = Guid.NewGuid();
        var dispatcher = new Mock<IDomainEventDispatcher>();
        var firstEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow);
        var secondEvent = new MySqlTestDomainEvent(MySqlIntegrationConstants.FixedNow.AddSeconds(1));

        await using var context = await MySqlIntegrationContextFactory.CreateAsync(tenantId, dispatcher.Object);
        await using var unit = new UnitOfWork<MySqlIntegrationDbContext>(context);
        await unit.BeginTransactionAsync();

        var first = new MySqlDomainEventEntity { TenantId = tenantId, Name = "First event" };
        var second = new MySqlDomainEventEntity { TenantId = tenantId, Name = "Second event" };
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
