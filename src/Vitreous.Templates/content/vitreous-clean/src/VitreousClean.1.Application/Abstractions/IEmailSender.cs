namespace VitreousClean._1.Application.Abstractions;

/// <summary>Something the use cases need from outside. Infrastructure implements it.</summary>
public interface IEmailSender
{
    Task Send(string to, string subject, CancellationToken ct);
}
