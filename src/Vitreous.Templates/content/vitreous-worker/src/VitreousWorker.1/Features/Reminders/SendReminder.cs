#if (UseValidation)
using FluentValidation;
#endif
using Vitreous;
using Vitreous.Abstractions;

namespace VitreousWorker._1.Features.Reminders;

// A vertical slice: the command, its validator and its handler live in one file, so a feature is
// read, changed or deleted in one place. Add the next one from its folder with:
//   dotnet new vitreous-command -n <Name>

// A command without a result: ICommand, and the handler returns Result.
public sealed record SendReminderCommand(Guid Id, string Recipient, string Text) : ICommand;
#if (UseValidation)

// Runs before the handler. When it fails, the handler is skipped and the worker gets
// Error.Validation with the messages per property.
public sealed class SendReminderValidator : AbstractValidator<SendReminderCommand>
{
    public SendReminderValidator()
    {
        RuleFor(c => c.Recipient).NotEmpty();
        RuleFor(c => c.Text).NotEmpty().MaximumLength(200);
    }
}
#endif

public sealed class SendReminderHandler(
    ReminderStore store,
    TimeProvider time,
    ISender sender,
    IPublisher publisher,
    ILogger<SendReminderHandler> logger) : ICommandHandler<SendReminderCommand>
{
    public async ValueTask<Result> Handle(SendReminderCommand command, CancellationToken ct = default)
    {
#if (!UseValidation)
        // Expected failures are returned as an Error, not thrown.
        if (string.IsNullOrWhiteSpace(command.Text))
            return Error.Validation("A reminder needs a text.");

#endif
        if (!store.IsPending(command.Id))
            return Error.NotFound($"Reminder {command.Id} was not found, or it was sent already.");

        var recipient = await sender.Send(new GetRecipientQuery(command.Recipient), ct);
        if (recipient.IsFailure)
            return recipient.Error;

        // Send your email or push message here. Marked as sent only afterwards: when sending throws,
        // the reminder stays pending and is tried again.
        logger.LogInformation("To {Email}: {Text}", recipient.Value.Email, command.Text);
        store.MarkSent(command.Id);

        // Other features react to the change through a notification, without this handler
        // knowing who listens.
        await publisher.Publish(new ReminderSent(command.Id, time.GetUtcNow()), ct);
        return Result.Success;
    }
}
