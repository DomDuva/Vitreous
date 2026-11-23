using Microsoft.Extensions.Logging;
using VitreousClean._1.Application.Abstractions;

namespace VitreousClean._1.Infrastructure.Email;

/// <summary>Writes the email to the log. Call your mail provider here.</summary>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task Send(string to, string subject, CancellationToken ct)
    {
        logger.LogInformation("Email to {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
