using System.Reflection;

namespace KUKULCAN.SharedKernel.Database.Tests;

[TestFixture]
public sealed class ConfigureSqlServerCatchCoverageTests
{
    [Test]
    public void ConfigureSqlServer_WhenProviderConfigurationThrowsNonNotSupportedException_ShouldWrapFailure()
    {
        MethodInfo method = typeof(KukulcanDbContextBase).GetMethod(
            "ConfigureSqlServer",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // UseSqlServer validates its DbContextOptionsBuilder during the provider
        // invocation. Passing null makes that invocation fail deterministically
        // with an ArgumentNullException, which is then wrapped by ConfigureSqlServer.
        var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(
            null,
            [
                null!,
                "Server=localhost;Database=KukulcanCoverage;Integrated Security=true;TrustServerCertificate=true",
                30,
                0,
                TimeSpan.Zero
            ]));

        Assert.That(invocation!.InnerException, Is.TypeOf<NotSupportedException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(invocation.InnerException!.Message, Does.Contain("Failed to configure provider."));
            Assert.That(invocation.InnerException.Message, Does.Contain("Microsoft.EntityFrameworkCore.SqlServer"));
            Assert.That(invocation.InnerException.InnerException, Is.Not.Null);
            Assert.That(invocation.InnerException.InnerException, Is.TypeOf<TargetInvocationException>());
            Assert.That(invocation.InnerException.InnerException!.InnerException, Is.TypeOf<ArgumentNullException>());
        }
    }
}
