namespace KUKULCAN.SharedKernel.Database.Tests.Configuration;

[TestFixture]
public sealed class KukulcanDatabaseOptionsTests
{
    [Test]
    public void Defaults_ShouldMatchContract()
    {
        var options = new KukulcanDatabaseOptions();

        Assert.Multiple(() =>
        {
            Assert.That(KukulcanDatabaseOptions.SectionKey, Is.EqualTo("Kukulcan:Database"));
            Assert.That(options.Provider, Is.EqualTo(DatabaseProvider.SqlServer));
            Assert.That(options.ConnectionString, Is.Empty);
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(30));
            Assert.That(options.EnableSensitiveDataLogging, Is.False);
            Assert.That(options.EnableDetailedErrors, Is.False);
            Assert.That(options.Retry, Is.Not.Null);
            Assert.That(options.Retry.Enabled, Is.True);
            Assert.That(options.Retry.MaxRetryCount, Is.EqualTo(3));
            Assert.That(options.Retry.MaxRetryDelaySeconds, Is.EqualTo(30));
            Assert.That(options.Pool.Enabled, Is.True);
            Assert.That(options.Pool.MinSize, Is.EqualTo(5));
            Assert.That(options.Pool.MaxSize, Is.EqualTo(100));
            Assert.That(options.Migration.AutoMigrateOnStartup, Is.False);
            Assert.That(options.Migration.SeedDataOnStartup, Is.True);
        });
    }

    [Test]
    public void Properties_ShouldBeMutable()
    {
        var options = new KukulcanDatabaseOptions
        {
            Provider = DatabaseProvider.PostgresSql,
            ConnectionString = "Host=test",
            CommandTimeoutSeconds = 60,
            EnableSensitiveDataLogging = true,
            EnableDetailedErrors = true,
            Retry = new KukulcanDatabaseOptions.RetryOptions
            {
                Enabled = false, MaxRetryCount = 7, MaxRetryDelaySeconds = 12
            },
            Pool = new KukulcanDatabaseOptions.PoolOptions
            {
                Enabled = false, MinSize = 2, MaxSize = 20
            },
            Migration = new KukulcanDatabaseOptions.MigrationOptions
            {
                AutoMigrateOnStartup = true, SeedDataOnStartup = false
            }
        };

        Assert.Multiple(() =>
        {
            Assert.That(options.Provider, Is.EqualTo(DatabaseProvider.PostgresSql));
            Assert.That(options.ConnectionString, Is.EqualTo("Host=test"));
            Assert.That(options.CommandTimeoutSeconds, Is.EqualTo(60));
            Assert.That(options.EnableSensitiveDataLogging, Is.True);
            Assert.That(options.EnableDetailedErrors, Is.True);
            Assert.That(options.Retry.Enabled, Is.False);
            Assert.That(options.Retry.MaxRetryCount, Is.EqualTo(7));
            Assert.That(options.Pool.MinSize, Is.EqualTo(2));
            Assert.That(options.Pool.MaxSize, Is.EqualTo(20));
            Assert.That(options.Migration.AutoMigrateOnStartup, Is.True);
            Assert.That(options.Migration.SeedDataOnStartup, Is.False);
        });
    }
}
