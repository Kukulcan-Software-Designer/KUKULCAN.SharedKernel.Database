namespace KUKULCAN.SharedKernel.Database.Integration.MySQL;

[TestFixture]
[NonParallelizable]
public sealed class MySqlAuditSoftDeleteIntegrationTests
{
    private MySqlIntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        await _context.Entities.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown() => await _context.DisposeAsync();

    [Test]
    public async Task AuditInterceptor_ShouldApplySameCreationTimestampToMultipleEntities()
    {
        var first = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "A" };
        var second = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "B" };
        _context.Entities.AddRange(first, second);
        await _context.SaveChangesAsync();
        Assert.That(first.CreatedOn, Is.EqualTo(second.CreatedOn));
    }

    [Test]
    public async Task AuditInterceptor_ShouldUpdateOnlyModifiedEntityTimestamp()
    {
        var first = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "A" };
        var second = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "B" };
        _context.Entities.AddRange(first, second);
        await _context.SaveChangesAsync();
        first.Name = "A2";
        await _context.SaveChangesAsync();
        Assert.That(first.ModifiedOn, Is.EqualTo(MySqlIntegrationConstants.FixedNow));
        Assert.That(second.ModifiedOn, Is.Null);
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldApplyAuditMetadataWhenEntityIsDeleted()
    {
        var entity = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Delete" };
        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(entity);
        await _context.SaveChangesAsync();
        var persisted = await _context.Entities.IgnoreQueryFilters().SingleAsync(x => x.Id == entity.Id);
        Assert.That(persisted.ModifiedOn, Is.EqualTo(MySqlIntegrationConstants.FixedNow));
        Assert.That(persisted.DeletedOn, Is.EqualTo(MySqlIntegrationConstants.FixedNow));
        Assert.That(persisted.IsDeleted, Is.True);
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldConvertMultipleDeletesWithoutPhysicalDeletion()
    {
        var first = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "A" };
        var second = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "B" };
        _context.Entities.AddRange(first, second);
        await _context.SaveChangesAsync();
        _context.Entities.RemoveRange(first, second);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.IgnoreQueryFilters().CountAsync(x => x.Id == first.Id || x.Id == second.Id), Is.EqualTo(2));
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldKeepDeletedEntityExcludedByDefaultFilter()
    {
        var entity = new MySqlIntegrationEntity { TenantId = _tenantId, Name = "Excluded" };
        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();
        _context.Entities.Remove(entity);
        await _context.SaveChangesAsync();
        Assert.That(await _context.Entities.AnyAsync(x => x.Id == entity.Id), Is.False);
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldNotAffectEntityWithoutSoftDeleteContract()
    {
        await using var context = await MySqlIntegrationContextFactory.CreateAsync(_tenantId);
        var entity = new MySqlDomainEventEntity { TenantId = _tenantId, Name = "Physical" };
        context.DomainEventEntities.Add(entity);
        await context.SaveChangesAsync();
        context.DomainEventEntities.Remove(entity);
        await context.SaveChangesAsync();
        Assert.That(await context.DomainEventEntities.IgnoreQueryFilters().AnyAsync(x => x.Id == entity.Id), Is.False);
    }
}
