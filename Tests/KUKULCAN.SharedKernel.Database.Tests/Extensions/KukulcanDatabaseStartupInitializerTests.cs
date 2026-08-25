using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class KukulcanDatabaseStartupInitializerTests
{
    [Test]
    public async Task InitializeAsync_WithAutoMigrateEnabled_ShouldApplyMigration()
    {
        await using var database = new StartupTestDatabase();
        await using var provider = BuildProvider(database, new KukulcanDatabaseOptions
        {
            Migration = new KukulcanDatabaseOptions.MigrationOptions
            {
                AutoMigrateOnStartup = true,
                SeedDataOnStartup = false
            }
        });

        var initializer = provider.GetRequiredService<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();
        await initializer.InitializeAsync();

        await using var context = provider.GetRequiredService<StartupTestDbContext>();
        Assert.That(await context.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM StartupInitializedRows"), Is.EqualTo(0));
    }

    [Test]
    public async Task InitializeAsync_WithSeedEnabled_ShouldResolveAndInvokeSeeder()
    {
        await using var database = new StartupTestDatabase();
        var seeder = new RecordingSeeder();
        await using var provider = BuildProvider(database, new KukulcanDatabaseOptions
        {
            Migration = new KukulcanDatabaseOptions.MigrationOptions
            {
                AutoMigrateOnStartup = false,
                SeedDataOnStartup = true
            }
        }, seeder);

        var initializer = provider.GetRequiredService<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();
        await initializer.InitializeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeder.CallCount, Is.EqualTo(1));
            Assert.That(seeder.Contexts, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task InitializeAsync_WithBothOptionsDisabled_ShouldNotCreateContextScope()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var initializer = new KukulcanDatabaseStartupInitializer<StartupTestDbContext>(
            scopeFactory.Object,
            Options.Create(new KukulcanDatabaseOptions
            {
                Migration = new KukulcanDatabaseOptions.MigrationOptions
                {
                    AutoMigrateOnStartup = false,
                    SeedDataOnStartup = false
                }
            }));

        await initializer.InitializeAsync();

        scopeFactory.Verify(x => x.CreateScope(), Times.Never);
    }

    [Test]
    public async Task InitializeAsync_WithSeedEnabledAndNoSeederRegistered_ShouldNotThrow()
    {
        await using var database = new StartupTestDatabase();
        await using var provider = BuildProvider(database, new KukulcanDatabaseOptions
        {
            Migration = new KukulcanDatabaseOptions.MigrationOptions
            {
                AutoMigrateOnStartup = false,
                SeedDataOnStartup = true
            }
        });

        var initializer = provider.GetRequiredService<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();

        Assert.DoesNotThrowAsync(async () => await initializer.InitializeAsync());
    }

    [Test]
    public async Task HostedService_StartAsync_ShouldRunConfiguredInitializer()
    {
        await using var database = new StartupTestDatabase();
        var seeder = new RecordingSeeder();
        await using var provider = BuildProvider(database, new KukulcanDatabaseOptions
        {
            Migration = new KukulcanDatabaseOptions.MigrationOptions
            {
                AutoMigrateOnStartup = false,
                SeedDataOnStartup = true
            }
        }, seeder);

        var initializer = provider.GetRequiredService<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();
        var hostedService = new KukulcanDatabaseStartupHostedService<StartupTestDbContext>(initializer);

        await hostedService.StartAsync(CancellationToken.None);

        Assert.That(seeder.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task HostedService_StopAsync_ShouldCompleteSuccessfully()
    {
        await using var database = new StartupTestDatabase();
        await using var provider = BuildProvider(database, new KukulcanDatabaseOptions());
        var initializer = provider.GetRequiredService<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();
        var hostedService = new KukulcanDatabaseStartupHostedService<StartupTestDbContext>(initializer);

        await hostedService.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider BuildProvider(
        StartupTestDatabase database,
        KukulcanDatabaseOptions options,
        RecordingSeeder? seeder = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<KukulcanDatabaseOptions>().Configure(current => CopyOptions(current, options));
        services.AddSingleton(database);
        services.AddScoped<StartupTestDbContext>();
        services.AddScoped<ITenantContext>(_ => new StartupTestTenantContext(Guid.NewGuid()));
        services.AddScoped<IClock>(_ => new StartupTestClock(DateTimeOffset.UtcNow));
        services.AddScoped<IDomainEventDispatcher>(_ => Mock.Of<IDomainEventDispatcher>());
        services.AddScoped<KukulcanDatabaseStartupInitializer<StartupTestDbContext>>();

        if (seeder is not null)
            services.AddScoped<IKukulcanDatabaseSeeder<StartupTestDbContext>>(_ => seeder);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void CopyOptions(KukulcanDatabaseOptions target, KukulcanDatabaseOptions source)
    {
        target.Provider = DatabaseProvider.SqlServer;
        target.ConnectionString = "Data Source=:memory:";
        target.CommandTimeoutSeconds = source.CommandTimeoutSeconds;
        target.EnableSensitiveDataLogging = source.EnableSensitiveDataLogging;
        target.EnableDetailedErrors = source.EnableDetailedErrors;
        target.Retry = source.Retry;
        target.Pool = source.Pool;
        target.Migration = source.Migration;
    }

    private sealed class StartupTestDatabase : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");

        public StartupTestDatabase()
        {
            Connection.Open();
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class StartupTestDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher,
        StartupTestDatabase database)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher)
    {
        public DbSet<StartupInitializedRow> Rows => Set<StartupInitializedRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(
                    database.Connection,
                    sqlite => sqlite.MigrationsAssembly(typeof(StartupTestMigration).Assembly.FullName));
            }

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StartupInitializedRow>().ToTable("StartupInitializedRows");
            modelBuilder.Entity<StartupInitializedRow>().HasKey(x => x.Id);
            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class StartupInitializedRow
    {
        public int Id { get; set; }
    }

    private sealed class StartupTestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class StartupTestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class RecordingSeeder : IKukulcanDatabaseSeeder<StartupTestDbContext>
    {
        public int CallCount { get; private set; }
        public List<StartupTestDbContext> Contexts { get; } = [];

        public Task SeedAsync(StartupTestDbContext context, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Contexts.Add(context);
            return Task.CompletedTask;
        }
    }

    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(StartupTestDbContext))]
    [Migration("202608250001_StartupTest")]
    private sealed class StartupTestMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.CreateTable(
                name: "StartupInitializedRows",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                },
                constraints: table => constraints.PrimaryKey("PK_StartupInitializedRows", x => x.Id));

        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.DropTable("StartupInitializedRows");
    }
}
