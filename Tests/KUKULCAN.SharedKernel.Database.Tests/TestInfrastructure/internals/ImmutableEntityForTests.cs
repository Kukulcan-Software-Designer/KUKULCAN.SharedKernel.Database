namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class ImmutableEntityForTests : IImmutable
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
}
