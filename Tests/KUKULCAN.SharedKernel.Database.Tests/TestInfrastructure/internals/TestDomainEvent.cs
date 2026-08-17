namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class TestDomainEvent : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
