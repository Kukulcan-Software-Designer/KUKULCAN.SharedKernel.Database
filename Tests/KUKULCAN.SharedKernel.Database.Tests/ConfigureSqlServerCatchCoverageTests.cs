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

        // ReflectionCoverageAssemblyBootstrap loads the fixture under the real
        // Microsoft.EntityFrameworkCore.SqlServer assembly identity. The fixture is a
        // build dependency of this project and is copied deterministically as
        // SqlServerCoverageFixture.dll, so this test never loads the real provider.
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "SqlServerCoverageFixture.dll");
        Assert.That(File.Exists(fixturePath), Is.True,
            $"Expected SQL Server coverage fixture at '{fixturePath}'.");

        // Passing null as the DbContextOptionsBuilder makes the reflected provider
        // invocation fail deterministically with ArgumentNullException. ConfigureSqlServer
        // must catch that non-NotSupportedException and wrap it in NotSupportedException.
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
            Assert.That(invocation.InnerException.InnerException, Is.TypeOf<TargetInvocationException>());
            Assert.That(
                invocation.InnerException.InnerException!.InnerException,
                Is.TypeOf<TargetInvocationException>());
            Assert.That(
                invocation.InnerException.InnerException!.InnerException!.InnerException,
                Is.TypeOf<ArgumentNullException>());
        }
    }
}
