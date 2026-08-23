using KUKULCAN.SharedKernel.Abstractions;
using KUKULCAN.SharedKernel.Database.Abstractions;
using KUKULCAN.SharedKernel.Database.Configuration;
using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Testcontainers.PostgreSql;

namespace KUKULCAN.SharedKernel.Database.Integration;

[SetUpFixture]
public sealed class IntegrationTestDatabase
{
    private static PostgreSqlContainer? _container;

    public static string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The integration test database has not been initialized.");

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("database_integration_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    internal static async Task<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext> CreateContextAsync(Guid tenantId)
    {
        var options = Options.Create(new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = ConnectionString,
            Retry = new KukulcanDatabaseOptions.RetryOptions { Enabled = false },
            Pool = new KukulcanDatabaseOptions.PoolOptions { Enabled = false },
        });

        var context = new PostgreSqlDatabaseIntegrationTests.IntegrationDbContext(
            options,
            new PostgreSqlDatabaseIntegrationTests.IntegrationTenantContext(tenantId),
            new PostgreSqlDatabaseIntegrationTests.FixedClock(PostgreSqlDatabaseIntegrationTests.FixedNow),
            Mock.Of<IDomainEventDispatcher>());

        await context.Database.EnsureCreatedAsync();
        return context;
    }
}

[TestFixture]
[NonParallelizable]
public sealed class PostgreSqlDatabaseIntegrationTests
{
    internal static readonly DateTimeOffset FixedNow =
        new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private IntegrationDbContext _context = null!;
    private Guid _tenantId;

    [SetUp]
    public async Task SetUp()
    {
        _tenantId = Guid.NewGuid();
        _context = await IntegrationTestDatabase.CreateContextAsync(_tenantId);

        await _context.Entities
            .IgnoreQueryFilters()
            .ExecuteDeleteAsync();
    }

    [TearDown]
    public async Task TearDown()
        => await _context.DisposeAsync();

    [Test]
    public async Task Provider_ShouldConnectAndPersistData()
    {
        var entity = new IntegrationEntity
        {
            TenantId = _tenantId,
            Name = "PostgreSQL integration"
        };

        _context.Entities.Add(entity);

        int affected = await _context.SaveChangesAsync();

        IntegrationEntity? persisted = await _context.Entities
            .SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(affected, Is.EqualTo(1));
            Assert.That(persisted.Name, Is.EqualTo("PostgreSQL integration"));
            Assert.That(persisted.TenantId, Is.EqualTo(_tenantId));
        }
    }

    [Test]
    public async Task TenantFilter_ShouldIsolateRealDatabaseRows()
    {
        Guid otherTenant = Guid.NewGuid();

        _context.Entities.AddRange(
            new IntegrationEntity { TenantId = _tenantId, Name = "Current tenant" },
            new IntegrationEntity { TenantId = otherTenant, Name = "Other tenant" });

        await _context.SaveChangesAsync();

        List<IntegrationEntity> visible = await _context.Entities.ToListAsync();

        Assert.That(visible, Has.Count.EqualTo(1));
        Assert.That(visible[0].Name, Is.EqualTo("Current tenant"));
    }

    [Test]
    public async Task SoftDeleteInterceptor_ShouldConvertDeleteIntoLogicalDelete()
    {
        var entity = new IntegrationEntity
        {
            TenantId = _tenantId,
            Name = "To delete"
        };

        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();

        _context.Entities.Remove(entity);
        await _context.SaveChangesAsync();

        Assert.That(await _context.Entities.AnyAsync(x => x.Id == entity.Id), Is.False);

        IntegrationEntity? deleted = await _context.Entities
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == entity.Id);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.IsDeleted, Is.True);
            Assert.That(deleted.DeletedOn, Is.EqualTo(FixedNow));
        }
    }

    [Test]
    public async Task AuditInterceptor_ShouldPersistCreationAndModificationTimestamps()
    {
        var entity = new IntegrationEntity
        {
            TenantId = _tenantId,
            Name = "Audited"
        };

        _context.Entities.Add(entity);
        await _context.SaveChangesAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entity.CreatedOn, Is.EqualTo(FixedNow));
            Assert.That(entity.ModifiedOn, Is.Null);
        }

        entity.Name = "Audited updated";
        await _context.SaveChangesAsync();

        Assert.That(entity.ModifiedOn, Is.EqualTo(FixedNow));
    }

    [Test]
    public async Task UnitOfWork_Commit_ShouldPersistTransaction()
    {
        var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);

        await unitOfWork.BeginTransactionAsync();

        _context.Entities.Add(new IntegrationEntity
        {
            TenantId = _tenantId,
            Name = "Committed"
        });

        await unitOfWork.CommitTransactionAsync();

        Assert.That(
            await _context.Entities.CountAsync(x => x.Name == "Committed"),
            Is.EqualTo(1));

        await unitOfWork.DisposeAsync();
    }

    [Test]
    public async Task UnitOfWork_Rollback_ShouldNotPersistTransaction()
    {
        var unitOfWork = new UnitOfWork<IntegrationDbContext>(_context);

        await unitOfWork.BeginTransactionAsync();

        _context.Entities.Add(new IntegrationEntity
        {
            TenantId = _tenantId,
            Name = "Rolled back"
        });

        await unitOfWork.RollbackTransactionAsync();

        Assert.That(
            await _context.Entities.CountAsync(x => x.Name == "Rolled back"),
            Is.EqualTo(0));

        await unitOfWork.DisposeAsync();
    }

    internal sealed class IntegrationTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    internal sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    internal sealed class IntegrationDbContext : KukulcanDbContextBase
    {
        private readonly string _connectionString;

        public IntegrationDbContext(
            IOptions<KukulcanDatabaseOptions> options,
            ITenantContext tenantContext,
            IClock clock,
            IDomainEventDispatcher dispatcher)
            : base(options, tenantContext, clock, dispatcher)
        {
            _connectionString = options.Value.ConnectionString;
        }

        internal DbSet<IntegrationEntity> Entities => Set<IntegrationEntity>();

        protected override void ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql(_connectionString);
    }

    internal sealed class IntegrationEntity : IAuditable, ISoftDelete
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedOn { get; set; }
        public DateTimeOffset? ModifiedOn { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedOn { get; set; }
    }
}
