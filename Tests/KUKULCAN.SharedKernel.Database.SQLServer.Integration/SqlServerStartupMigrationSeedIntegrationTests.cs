using Microsoft.EntityFrameworkCore.Migrations;

namespace KUKULCAN.SharedKernel.Database.SQLServer.Integration;

[TestFixture]
[NonParallelizable]
public sealed class SqlServerStartupMigrationSeedIntegrationTests
{
    [Test]
    public async Task StartupInitializer_ShouldApplyMigrationAndSeedAgainstRealSqlServer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KukulcanDatabaseOptions.SectionKey}:Provider"] = nameof(DatabaseProvider.SqlServer),
                [$"{KukulcanDatabaseOptions.SectionKey}:ConnectionString"] = SqlServerIntegrationDatabase.ConnectionString,
                [$"{KukulcanDatabaseOptions.SectionKey}:CommandTimeoutSeconds"] = "30",
                [$"{KukulcanDatabaseOptions.SectionKey}:Retry:Enabled"] = "false",
                [$"{KukulcanDatabaseOptions.SectionKey}:Pool:Enabled"] = "false",
                [$"{KukulcanDatabaseOptions.SectionKey}:Migration:AutoMigrateOnStartup"] = "true",
                [$"{KukulcanDatabaseOptions.SectionKey}:Migration:SeedDataOnStartup"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddScoped<ITenantContext>(_ => new SqlServerTenantContext(Guid.NewGuid()));
        services.AddSingleton<IClock>(new FixedClock(SqlServerIntegrationConstants.FixedNow));
        services.AddSingleton<IDomainEventDispatcher>(Mock.Of<IDomainEventDispatcher>());
        services.AddKukulcanDbContext<SqlServerStartupCoverageDbContext>(configuration);
        services.AddScoped<IKukulcanDatabaseSeeder<SqlServerStartupCoverageDbContext>, SqlServerStartupCoverageSeeder>();

        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        KukulcanDatabaseStartupInitializer<SqlServerStartupCoverageDbContext> initializer =
            scope.ServiceProvider.GetRequiredService<KukulcanDatabaseStartupInitializer<SqlServerStartupCoverageDbContext>>();

        await initializer.InitializeAsync();

        SqlServerStartupCoverageDbContext context =
            scope.ServiceProvider.GetRequiredService<SqlServerStartupCoverageDbContext>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await context.Database.GetPendingMigrationsAsync(), Is.Empty);
            Assert.That(await context.StartupRows.AnyAsync(x => x.Name == "seeded-by-startup"), Is.True);
        }
    }
}

internal sealed class SqlServerStartupCoverageDbContext(
    IOptions<KukulcanDatabaseOptions> options,
    ITenantContext tenantContext,
    IClock clock,
    IDomainEventDispatcher dispatcher,
    SlowQueryInterceptor? slowQueryInterceptor = null)
    : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor)
{
    public DbSet<SqlServerStartupCoverageRow> StartupRows => Set<SqlServerStartupCoverageRow>();
}

internal sealed class SqlServerStartupCoverageRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal sealed class SqlServerStartupCoverageRowConfiguration : IEntityTypeConfiguration<SqlServerStartupCoverageRow>
{
    public void Configure(EntityTypeBuilder<SqlServerStartupCoverageRow> builder)
    {
        builder.ToTable("KukulcanStartupCoverageRows");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
    }
}

internal sealed class SqlServerStartupCoverageMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "KukulcanStartupCoverageRows",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KukulcanStartupCoverageRows", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "KukulcanStartupCoverageRows");
}

internal sealed class SqlServerStartupCoverageSeeder : IKukulcanDatabaseSeeder<SqlServerStartupCoverageDbContext>
{
    public async Task SeedAsync(SqlServerStartupCoverageDbContext context, CancellationToken cancellationToken = default)
    {
        if (!await context.StartupRows.AnyAsync(x => x.Name == "seeded-by-startup", cancellationToken))
        {
            context.StartupRows.Add(new SqlServerStartupCoverageRow { Name = "seeded-by-startup" });
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
