namespace Vitreous.Abstractions;

/// <summary>
/// Marker interface for a notification — a fire-and-forget event dispatched to zero or more handlers.
/// Unlike commands and queries, notifications do not return a value.
/// </summary>
public interface INotification { }
