using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Email;
using StackExchange.Redis;

namespace Pulsebreak.Api.Auth;

/// <summary>Signs an account in: starts a session and puts the reply together.</summary>
public sealed class SessionIssuer(SessionStore sessions, TokenService tokens)
{
    public async Task<SessionDto> StartAsync(Account account) =>
        Reply(account, await sessions.StartAsync(account.Id));

    public SessionDto Reply(Account account, string refreshToken) =>
        new(tokens.CreateAccessToken(account), tokens.AccessTokenSeconds, refreshToken, AccountDto.From(account));
}

/// <summary>
/// Makes access tokens stop working before they expire. An access token is checked without
/// looking anything up, which is what makes it cheap, but it also means it would outlive a
/// password reset by a few minutes. This closes that gap: one small lookup per request says
/// "tokens for this account issued before such a time no longer count".
/// </summary>
public sealed class TokenRevocation(IConnectionMultiplexer redis, IOptions<AuthOptions> options, TimeProvider clock)
{
    readonly IDatabase _db = redis.GetDatabase();

    static string Key(Guid accountId) => "pb:revoked-before:" + accountId.ToString("N");

    /// <summary>Every access token this account holds right now stops working.</summary>
    public Task RevokeAllAsync(Guid accountId) =>
        // Remembered only for as long as such a token could still be alive.
        _db.StringSetAsync(Key(accountId), clock.GetUtcNow().ToUnixTimeSeconds(), TimeSpan.FromMinutes(options.Value.AccessTokenMinutes + 2));

    public async Task<bool> IsRevokedAsync(Guid accountId, DateTimeOffset issuedAt)
    {
        RedisValue cutoff = await _db.StringGetAsync(Key(accountId));
        return cutoff.TryParse(out long revokedBefore) && issuedAt.ToUnixTimeSeconds() < revokedBefore;
    }
}

/// <summary>
/// Emails waiting to be sent. Requests add to it and return at once; a background worker
/// does the sending. Besides keeping requests quick, this means "forgot password" takes the
/// same time whether or not the address has an account, so its timing gives nothing away.
/// </summary>
public sealed class EmailQueue
{
    readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

public sealed class EmailWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        await foreach (EmailMessage message in queue.Reader.ReadAllAsync(stopping))
        {
            try
            {
                await sender.SendAsync(message, stopping);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The address is logged, the contents are not: they hold a working code.
                logger.LogError(error, "Could not send \"{Subject}\" to {To}", message.Subject, message.To);
            }
        }
    }
}

/// <summary>Decides which account emails go out, and puts the right code in each.</summary>
public sealed class AccountMailer(EmailCodes codes, EmailQueue queue, IOptions<EmailOptions> email, IOptions<AuthOptions> auth)
{
    /// <summary>Sends a fresh verification code and link. False if one was sent too recently.</summary>
    public async Task<bool> SendVerificationAsync(Account account)
    {
        if (!await codes.TryStartCooldownAsync(CodeKind.VerifyEmail, account.Id)) return false;

        string code = await codes.CreateAsync(CodeKind.VerifyEmail, account.Id);
        string token = await codes.CreateVerifyLinkAsync(account.Id);
        string link = email.Value.PublicBaseUrl.TrimEnd('/') + "/v1/auth/verify-email?token=" + token;
        queue.Enqueue(EmailTemplates.VerifyEmail(account.Email, account.Username, code, link, Math.Max(1, auth.Value.EmailCodeMinutes / 60)));
        return true;
    }

    public async Task SendPasswordResetAsync(Account account)
    {
        if (!await codes.TryStartCooldownAsync(CodeKind.ResetPassword, account.Id)) return;
        string code = await codes.CreateAsync(CodeKind.ResetPassword, account.Id);
        queue.Enqueue(EmailTemplates.ResetPassword(account.Email, account.Username, code, auth.Value.ResetCodeMinutes));
    }

    public void SendPasswordChanged(Account account) =>
        queue.Enqueue(EmailTemplates.PasswordChanged(account.Email, account.Username));
}
