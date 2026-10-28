namespace HouseholdApp.Application.Shared.Events;

/// <summary>
/// Request-scoped switch that bulk writers (e.g. history imports) use to keep user-facing
/// notification handlers quiet. Projections and all other handlers run as usual.
/// </summary>
public sealed class EventDispatchScope
{
    public bool SuppressNotifications { get; private set; }

    public IDisposable Suppress()
    {
        var previous = SuppressNotifications;
        SuppressNotifications = true;
        return new Restore(this, previous);
    }

    private sealed class Restore(EventDispatchScope scope, bool previous) : IDisposable
    {
        public void Dispose() => scope.SuppressNotifications = previous;
    }
}
