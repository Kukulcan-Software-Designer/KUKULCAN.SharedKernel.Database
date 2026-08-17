namespace KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;

internal sealed class DomainEventEntityForTests : IHasDomainEvents
{
    private readonly List<IDomainEvent> _events = [];

    public int Id { get; set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events;

    public void AddDomainEvent(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public void ClearDomainEvents() => _events.Clear();
}
