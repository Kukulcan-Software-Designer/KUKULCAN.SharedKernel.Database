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

        // The SQL Server provider is an optional runtime dependency of the production
        // assembly. Load the actual provider assembly from the test output directory
        // before invoking ConfigureSqlServer so LoadProviderExtensionType can resolve
        // it deterministically without depending on AssemblyLoadContext probing.
        string providerAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Microsoft.EntityFrameworkCore.SqlServer.dll");

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
