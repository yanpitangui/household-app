namespace HouseholdApp.Application.Shared.Domain;

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearEvents() => _events.Clear();

    protected void Raise(IDomainEvent @event) => _events.Add(@event);
}
