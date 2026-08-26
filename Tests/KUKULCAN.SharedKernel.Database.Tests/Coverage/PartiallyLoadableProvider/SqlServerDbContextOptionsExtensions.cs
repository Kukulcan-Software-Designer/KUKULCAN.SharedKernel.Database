namespace PartiallyLoadableProvider;

/// <summary>
/// Valid type whose short name matches the provider extension type expected by production code.
/// Its full name intentionally differs so Assembly.GetType(typeName) does not find it.
/// </summary>
public class SqlServerDbContextOptionsExtensions
{
}
