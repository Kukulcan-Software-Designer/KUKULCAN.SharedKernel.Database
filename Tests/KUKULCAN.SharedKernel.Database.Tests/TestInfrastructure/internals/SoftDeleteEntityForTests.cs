namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class SoftDeleteEntityForTests : ISoftDelete
{
    public int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedOn { get; set; }
}
