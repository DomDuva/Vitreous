using Vitreous.Abstractions;

namespace VitreousClean._1.Domain;

/// <summary>
/// An entity that records domain events. Infrastructure publishes them once the entity is saved,
/// so the domain never needs an <see cref="IPublisher"/>.
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>Returns the events recorded since the last call and clears them.</summary>
    IReadOnlyList<INotification> DequeueDomainEvents();
}
