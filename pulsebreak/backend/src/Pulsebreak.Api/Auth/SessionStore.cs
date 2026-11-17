using StackExchange.Redis;
using Microsoft.Extensions.Options;

namespace Pulsebreak.Api.Auth;

public enum RefreshOutcome
{
    /// <summary>The token is not known, has expired, or its session was ended.</summary>
    Invalid,
    /// <summary>
    /// The token was valid once but has already been exchanged. Two parties hold it, so one
    /// of them stole it; the whole session has been ended and both must sign in again.
    /// </summary>
    Reused,
    Rotated,
}

public readonly record struct RefreshResult(RefreshOutcome Outcome, Guid AccountId, string NewToken);

/// <summary>
/// Signed-in sessions, kept in Redis.
///
/// Signing in starts a session and returns a refresh token. Each time that token is used
/// to get a new access token it is replaced by a fresh one, so a refresh token works
/// exactly once. If a used token turns up again, someone has a copy they should not, and
/// the session is ended.
///
/// Only hashes of tokens are stored, so reading Redis does not hand anyone a working token.
/// </summary>
public sealed class SessionStore(IConnectionMultiplexer redis, IOptions<AuthOptions> options)
{
    // Marks a token as used, but only if it is still unused and its session still exists.
    // Done in one step inside Redis, so two requests racing with the same token cannot both win.
    const string RotateScript = """
        local state = redis.call('HGET', KEYS[1], 'state')
        if not state then return 0 end
        if redis.call('EXISTS', KEYS[2]) == 0 then return 0 end
        if state ~= 'active' then
            redis.call('DEL', KEYS[2])
            return -1
        end
        redis.call('HSET', KEYS[1], 'state', 'used')
        return 1
        """;

    readonly IDatabase _db = redis.GetDatabase();
    readonly TimeSpan _lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);

    static string TokenKey(string token) => "pb:rt:" + TokenService.Hash(token);
    static string SessionKey(string sessionId) => "pb:session:" + sessionId;
    static string AccountKey(Guid accountId) => "pb:sessions:" + accountId.ToString("N");

    /// <summary>Starts a session for an account and returns its first refresh token.</summary>
    public async Task<string> StartAsync(Guid accountId)
    {
        string sessionId = Guid.NewGuid().ToString("N");
        await _db.StringSetAsync(SessionKey(sessionId), accountId.ToString("N"), _lifetime);
        await _db.SetAddAsync(AccountKey(accountId), sessionId);
        await _db.KeyExpireAsync(AccountKey(accountId), _lifetime);
        return await IssueAsync(accountId, sessionId);
    }

    /// <summary>Exchanges a refresh token for a new one.</summary>
    public async Task<RefreshResult> RotateAsync(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return new RefreshResult(RefreshOutcome.Invalid, default, "");

        string tokenKey = TokenKey(token);
        HashEntry[] entry = await _db.HashGetAllAsync(tokenKey);
        string? account = null, sessionId = null;
        foreach (HashEntry field in entry)
        {
            if (field.Name == "account") account = field.Value;
            else if (field.Name == "session") sessionId = field.Value;
        }
        if (account == null || sessionId == null || !Guid.TryParseExact(account, "N", out Guid accountId))
            return new RefreshResult(RefreshOutcome.Invalid, default, "");

        var outcome = (int)await _db.ScriptEvaluateAsync(RotateScript, [tokenKey, SessionKey(sessionId)]);
        if (outcome == -1) return new RefreshResult(RefreshOutcome.Reused, accountId, "");
        if (outcome != 1) return new RefreshResult(RefreshOutcome.Invalid, default, "");

        await _db.KeyExpireAsync(SessionKey(sessionId), _lifetime);
        await _db.KeyExpireAsync(AccountKey(accountId), _lifetime);
        return new RefreshResult(RefreshOutcome.Rotated, accountId, await IssueAsync(accountId, sessionId));
    }

    /// <summary>Ends the session a refresh token belongs to (signing out on one device).</summary>
    public async Task EndAsync(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return;
        RedisValue sessionId = await _db.HashGetAsync(TokenKey(token), "session");
        RedisValue account = await _db.HashGetAsync(TokenKey(token), "account");
        if (sessionId.IsNullOrEmpty) return;

        await _db.KeyDeleteAsync(SessionKey(sessionId!));
        if (Guid.TryParseExact((string?)account, "N", out Guid accountId)) await _db.SetRemoveAsync(AccountKey(accountId), sessionId);
    }

    /// <summary>Ends every session of an account (after a password change, or "sign out everywhere").</summary>
    public async Task EndAllAsync(Guid accountId)
    {
        RedisValue[] sessions = await _db.SetMembersAsync(AccountKey(accountId));
        foreach (RedisValue sessionId in sessions) await _db.KeyDeleteAsync(SessionKey(sessionId!));
        await _db.KeyDeleteAsync(AccountKey(accountId));
    }

    async Task<string> IssueAsync(Guid accountId, string sessionId)
    {
        string token = TokenService.NewRefreshToken();
        string key = TokenKey(token);
        await _db.HashSetAsync(key,
        [
            new HashEntry("account", accountId.ToString("N")),
            new HashEntry("session", sessionId),
            new HashEntry("state", "active"),
        ]);
        await _db.KeyExpireAsync(key, _lifetime);
        return token;
    }
}
