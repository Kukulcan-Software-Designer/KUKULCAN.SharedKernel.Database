namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class TestDbContext(IOptions<KukulcanDatabaseOptions> options, ITenantContext tenantContext,
    IClock clock, IDomainEventDispatcher dispatcher) : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
{
    public DbSet<AuditableEntityForTests> AuditableEntities => Set<AuditableEntityForTests>();
    public DbSet<SoftDeleteEntityForTests> SoftDeleteEntities => Set<SoftDeleteEntityForTests>();
    public DbSet<ImmutableEntityForTests> ImmutableEntities => Set<ImmutableEntityForTests>();
    public DbSet<TenantEntityForTests> TenantEntities => Set<TenantEntityForTests>();
    public DbSet<CombinedEntityForTests> CombinedEntities => Set<CombinedEntityForTests>();
    public DbSet<DomainEventEntityForTests> DomainEventEntities => Set<DomainEventEntityForTests>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DomainEventEntityForTests>().Ignore(x => x.DomainEvents);

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
}


