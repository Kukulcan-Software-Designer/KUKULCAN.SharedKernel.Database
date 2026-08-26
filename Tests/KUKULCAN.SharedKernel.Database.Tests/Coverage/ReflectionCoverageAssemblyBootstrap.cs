using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

internal static class ReflectionCoverageAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void LoadPartiallyLoadableProviderFixture()
    {
        const string assemblyName = "Microsoft.EntityFrameworkCore.SqlServer";
        string fixturePath = Path.Combine(AppContext.BaseDirectory, "SqlServerCoverageFixture.dll");

        if (!File.Exists(fixturePath))
            throw new FileNotFoundException("The partially loadable provider fixture was not copied to the test output directory.", fixturePath);

        if (AssemblyLoadContext.Default.Assemblies.Any(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)))
            return;

        AssemblyLoadContext.Default.LoadFromAssemblyPath(fixturePath);
    }
}
