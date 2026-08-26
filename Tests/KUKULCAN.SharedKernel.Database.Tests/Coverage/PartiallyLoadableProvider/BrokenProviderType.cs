using PartiallyLoadableProvider.MissingDependency;

namespace PartiallyLoadableProvider;

/// <summary>
/// Type that cannot be materialized when the fixture assembly is loaded without its dependency.
/// </summary>
public sealed class BrokenProviderType : MissingBaseType
{
}
