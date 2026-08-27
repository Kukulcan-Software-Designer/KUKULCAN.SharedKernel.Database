using System.Reflection;
using Microsoft.EntityFrameworkCore;

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

        // Resolve the provider assembly from the package reference itself rather than
        // assuming that NuGet copied the optional provider DLL to the test output root.
        // Referencing the public EF Core SQL Server extension type gives us the exact
        // assembly that ConfigureSqlServer must load and invoke.
        string providerAssemblyPath = typeof(SqlServerDbContextOptionsBuilderExtensions).Assembly.Location;

        Assert.That(providerAssemblyPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(providerAssemblyPath), Is.True,
            $"Expected SQL Server provider assembly at '{providerAssemblyPath}'.");

        _ = Assembly.LoadFrom(providerAssemblyPath);

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
            Assert.That(invocation.InnerException.InnerException!.InnerException, Is.TypeOf<ArgumentNullException>());
        }
    }
}
