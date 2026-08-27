using System.Reflection;
using System.Runtime.Loader;

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

        // The coverage fixture has the real provider assembly identity but a different
        // physical file name. ConfigureSqlServer uses Assembly.Load by simple assembly name,
        // so explicitly resolve that identity to the fixture for this test.
        string providerAssemblyPath = typeof(PartiallyLoadableProvider.SqlServerDbContextOptionsExtensions)
            .Assembly
            .Location;

        Assert.That(providerAssemblyPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(providerAssemblyPath), Is.True,
            $"Expected SQL Server coverage fixture assembly at '{providerAssemblyPath}'.");

        Assembly? ResolveProvider(AssemblyLoadContext context, AssemblyName assemblyName)
            => string.Equals(assemblyName.Name, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal)
                ? context.LoadFromAssemblyPath(providerAssemblyPath)
                : null;

        AssemblyLoadContext.Default.Resolving += ResolveProvider;
        try
        {
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
        finally
        {
            AssemblyLoadContext.Default.Resolving -= ResolveProvider;
        }
    }
}
