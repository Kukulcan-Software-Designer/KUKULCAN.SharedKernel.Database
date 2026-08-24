using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class UnitOfWorkUnitTests
{
    [Test]
    public void Constructor_ShouldRejectNullContext()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new UnitOfWork<UnitTestDbContext>(null!))!;

        Assert.That(exception.ParamName, Is.EqualTo("context"));
    }

    private sealed class UnitTestDbContext : KukulcanDbContextBase
    {
        public UnitTestDbContext()
            : base(
                Options.Create(new KukulcanDatabaseOptions
                {
                    Provider = DatabaseProvider.PostgresSql,
                    ConnectionString = "Host=unit-test;Database=unit-test;Username=unit-test;Password=unit-test"
                }),
                Mock.Of<ITenantContext>(),
                Mock.Of<IClock>(),
                Mock.Of<IDomainEventDispatcher>())
        {
        }
    }
}
