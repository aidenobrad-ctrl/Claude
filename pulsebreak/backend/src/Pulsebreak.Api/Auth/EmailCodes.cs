using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pulsebreak.Api.Auth;

public enum CodeKind
{
    VerifyEmail,
    ResetPassword,
}

/// <summary>
/// The short codes emailed to prove someone controls an address: one kind to verify a new
/// account's email, another to reset a forgotten password.
///
/// A code works once, expires, and stops working after a handful of wrong guesses, so it
/// cannot be found by trying them all. Asking for a new code replaces the old one.
/// </summary>
public sealed class EmailCodes(IConnectionMultiplexer redis, IOptions<AuthOptions> options)
{
    // No 0/O, 1/I/L: a code is read off an email and typed into a game.
    const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // Compares and counts in one step inside Redis, so guesses made at the same moment
    // cannot each be counted as "the first".
    const string CheckScript = """
        local stored = redis.call('HGET', KEYS[1], 'code')
        if not stored then return 0 end
        if stored == ARGV[1] then
            redis.call('DEL', KEYS[1])
            return 1
        end
        local attempts = redis.call('HINCRBY', KEYS[1], 'attempts', 1)
        if attempts >= tonumber(ARGV[2]) then redis.call('DEL', KEYS[1]) end
        return 0
        """;

    readonly IDatabase _db = redis.GetDatabase();
    readonly AuthOptions _options = options.Value;

    static string CodeKey(CodeKind kind, Guid accountId) => $"pb:code:{kind}:{accountId:N}";
    static string LinkKey(string token) => "pb:verify-link:" + TokenService.Hash(token);
    static string CooldownKey(CodeKind kind, Guid accountId) => $"pb:mail-cooldown:{kind}:{accountId:N}";

    /// <summary>
    /// True if an email of this kind may be sent to this account now, and if so starts the
    /// wait before the next one. Stops the API being used to flood someone's inbox.
    /// </summary>
    public Task<bool> TryStartCooldownAsync(CodeKind kind, Guid accountId) =>
        _db.StringSetAsync(CooldownKey(kind, accountId), "1", TimeSpan.FromSeconds(_options.EmailCooldownSeconds), When.NotExists);

    /// <summary>Makes a new code for an account, replacing any earlier one of the same kind.</summary>
    public async Task<string> CreateAsync(CodeKind kind, Guid accountId)
    {
        // Six digits to verify an address; eight letters and digits (about 40 bits) to reset a password.
        string code = kind == CodeKind.VerifyEmail
            ? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000")
            : RandomNumberGenerator.GetString(Alphabet, 8);

        string key = CodeKey(kind, accountId);
        await _db.KeyDeleteAsync(key);
        await _db.HashSetAsync(key, [new HashEntry("code", code), new HashEntry("attempts", 0)]);
        await _db.KeyExpireAsync(key, Lifetime(kind));
        return code;
    }

    /// <summary>True if the code is right. Right or wrong, it has been spent or counted against.</summary>
    public async Task<bool> RedeemAsync(CodeKind kind, Guid accountId, string? code)
    {
        string normalized = Normalize(code);
        if (normalized.Length == 0 || normalized.Length > 16) return false;
        var outcome = (int)await _db.ScriptEvaluateAsync(CheckScript, [CodeKey(kind, accountId)], [normalized, _options.MaxCodeAttempts]);
        return outcome == 1;
    }

    /// <summary>A long random token for the link in the verification email. Being unguessable, it needs no attempt limit.</summary>
    public async Task<string> CreateVerifyLinkAsync(Guid accountId)
    {
        string token = TokenService.NewRefreshToken();
        await _db.StringSetAsync(LinkKey(token), accountId.ToString("N"), Lifetime(CodeKind.VerifyEmail));
        return token;
    }

    public async Task<Guid?> RedeemVerifyLinkAsync(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        RedisValue account = await _db.StringGetDeleteAsync(LinkKey(token));
        return Guid.TryParseExact((string?)account, "N", out Guid accountId) ? accountId : null;
    }

    public Task ForgetAsync(CodeKind kind, Guid accountId) => _db.KeyDeleteAsync(CodeKey(kind, accountId));

    TimeSpan Lifetime(CodeKind kind) =>
        TimeSpan.FromMinutes(kind == CodeKind.VerifyEmail ? _options.EmailCodeMinutes : _options.ResetCodeMinutes);

    /// <summary>Forgives the ways people mistype a code: spaces, dashes, lower case.</summary>
    static string Normalize(string? code) =>
        new((code ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
