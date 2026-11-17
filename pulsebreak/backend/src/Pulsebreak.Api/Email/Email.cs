using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Pulsebreak.Api.Email;

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancel = default);
}

/// <summary>Writes each email to the application log instead of sending it. For development: the code to type is right there in the console.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancel = default)
    {
        logger.LogInformation("Email not sent (Email:Mode is Log).\n  To: {To}\n  Subject: {Subject}\n\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

/// <summary>Writes each email to a text file in a folder. For development and for automated tests of a deployment.</summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancel = default)
    {
        string folder = options.Value.FileDirectory;
        Directory.CreateDirectory(folder);
        string safeTo = new(message.To.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' or '_' ? c : '_').ToArray());
        string path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeTo}.txt");
        await File.WriteAllTextAsync(path, $"To: {message.To}\nSubject: {message.Subject}\n\n{message.Body}\n", cancel);
    }
}

/// <summary>Sends email through an SMTP server.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancel = default)
    {
        EmailOptions settings = options.Value;
        var mail = new MimeMessage();
        mail.From.Add(MailboxAddress.Parse(settings.From));
        mail.To.Add(MailboxAddress.Parse(message.To));
        mail.Subject = message.Subject;
        mail.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort,
            settings.SmtpStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancel);
        if (!string.IsNullOrEmpty(settings.SmtpUser)) await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPassword, cancel);
        await client.SendAsync(mail, cancel);
        await client.DisconnectAsync(true, cancel);
    }
}

/// <summary>The words of every email the API sends.</summary>
public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string username, string code, string link, int validHours) => new(to,
        "Your Pulsebreak verification code: " + code,
        $"""
        Hi {username},

        Your verification code is:

            {code}

        Type it into the game, or open this link:

            {link}

        The code works for {validHours} hours. Verifying your email is what unlocks ranked play.

        If you did not create a Pulsebreak account, you can ignore this email.
        """);

    public static EmailMessage ResetPassword(string to, string username, string code, int validMinutes) => new(to,
        "Your Pulsebreak password reset code",
        $"""
        Hi {username},

        Someone asked to reset the password for your Pulsebreak account. If that was you,
        type this code into the game along with a new password:

            {code[..4]}-{code[4..]}

        The code works for {validMinutes} minutes.

        If it was not you, ignore this email. Your password has not changed.
        """);

    public static EmailMessage PasswordChanged(string to, string username) => new(to,
        "Your Pulsebreak password was changed",
        $"""
        Hi {username},

        The password for your Pulsebreak account was just changed, and every device was
        signed out.

        If that was not you, reset your password from the game's sign-in screen right away.
        """);
}
