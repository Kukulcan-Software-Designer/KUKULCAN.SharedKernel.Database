using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace KUKULCAN.SharedKernel.Database.Tests.Coverage;

internal static class ReflectionCoverageAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void LoadPartiallyLoadableProviderFixture()
    {
        const string assemblyName = "Microsoft.EntityFrameworkCore.SqlServer";
        string fixturePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "SqlServerCoverageFixture.dll"));

        if (!File.Exists(fixturePath))
            throw new FileNotFoundException(
                "The SQL Server coverage fixture was not copied to the test output directory.",
                fixturePath);

        Assembly? loadedAssembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));

        if (loadedAssembly is not null)
        {
            string loadedPath = Path.GetFullPath(loadedAssembly.Location);
            if (!string.Equals(loadedPath, fixturePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The SQL Server provider assembly was already loaded from '{loadedPath}' instead of the coverage fixture '{fixturePath}'.");
            }

            return;
        }

        AssemblyLoadContext.Default.LoadFromAssemblyPath(fixturePath);
    }
}
