using System.Reflection;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class ConfigureSqlServerCatchCoverageTests
{
    [Test]
    public void ConfigureSqlServer_WhenProviderUseMethodThrowsNonNotSupportedException_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigureSqlServer",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var optionsBuilder = new DbContextOptionsBuilder();

        // CommandTimeout receives a negative value and the real SQL Server provider
        // throws a non-NotSupportedException while executing the configuration action.
        // This must execute ConfigureSqlServer's catch body.
        var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(
            null,
            [
                optionsBuilder,
                "Server=localhost;Database=KukulcanCoverage;Integrated Security=true;TrustServerCertificate=true",
                -1,
                0,
                TimeSpan.Zero
            ]));

        Assert.That(invocation!.InnerException, Is.TypeOf<NotSupportedException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invocation.InnerException!.Message, Does.Contain("Failed to configure provider."));
            Assert.That(invocation.InnerException.Message, Does.Contain("Microsoft.EntityFrameworkCore.SqlServer"));
            Assert.That(invocation.InnerException.InnerException, Is.Not.Null);
            Assert.That(invocation.InnerException.InnerException, Is.Not.TypeOf<NotSupportedException>());
        }
    }
}
