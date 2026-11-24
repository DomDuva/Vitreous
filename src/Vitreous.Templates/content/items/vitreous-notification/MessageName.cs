#if (handler && AddSystemUsings)
using System;
using System.Threading;
using System.Threading.Tasks;
#endif
using Vitreous.Abstractions;

namespace ItemNamespace;

public sealed record MessageName : INotification;
#if (handler)

// Add as many handlers as you need: every one of them gets the notification.
public sealed class MessageNameHandler : INotificationHandler<MessageName>
{
    public ValueTask Handle(MessageName notification, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
#endif
