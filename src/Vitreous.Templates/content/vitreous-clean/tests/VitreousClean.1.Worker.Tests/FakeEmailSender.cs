using System.Collections.Concurrent;
using VitreousClean._1.Application.Abstractions;

namespace VitreousClean._1.Worker.Tests;

/// <summary>Keeps the emails instead of sending them.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject)> Sent { get; } = [];

    /// <summary>The address that can't be reached, if any.</summary>
    public string? FailFor { get; set; }

    public Task Send(string to, string subject, CancellationToken ct)
    {
        if (to == FailFor)
            throw new InvalidOperationException($"{to} can't be reached.");

        Sent.Enqueue((to, subject));
        return Task.CompletedTask;
    }
}
