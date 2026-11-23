using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vitreous.Abstractions;
using VitreousWorker._1.Features.Reminders;

namespace VitreousWorker._1.Tests;

/// <summary>
/// Sends one command through the app's own services, without starting the worker. A handler that
/// takes nothing but plain classes needs no host at all: see the last tests.
/// </summary>
public sealed class SendReminderTests : IDisposable
{
    private readonly IHost _host = Program.CreateBuilder(["--environment=Development"]).Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sent_reminder_is_no_longer_pending()
    {
        var store    = _host.Services.GetRequiredService<ReminderStore>();
        var reminder = store.Add("ada", "Stand up", DateTimeOffset.UtcNow);

        await using var scope = _host.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var first  = await sender.Send(new SendReminderCommand(reminder.Id, reminder.Recipient, reminder.Text), Ct);
        var second = await sender.Send(new SendReminderCommand(reminder.Id, reminder.Recipient, reminder.Text), Ct);

        Assert.True(first.IsSuccess);
        Assert.Equal("NOT_FOUND", second.Error.Code);
        Assert.Equal(0, store.PendingCount);
    }

    [Fact]
    public async Task A_reminder_without_a_text_is_rejected()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new SendReminderCommand(Guid.NewGuid(), "ada", " "), Ct);

        Assert.Equal("VALIDATION", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_recipient_is_not_found()
    {
        var handler = new GetRecipientHandler(new RecipientDirectory());

        var result = await handler.Handle(new GetRecipientQuery("nobody"), Ct);

        Assert.Equal("NOT_FOUND", result.Error.Code);
    }
#if (UseValidation)

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task The_validator_rejects_a_blank_recipient(string recipient)
    {
        var result = await new SendReminderValidator().ValidateAsync(new SendReminderCommand(Guid.NewGuid(), recipient, "Stand up"), Ct);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SendReminderCommand.Recipient));
    }
#endif

    public void Dispose() => _host.Dispose();
}
