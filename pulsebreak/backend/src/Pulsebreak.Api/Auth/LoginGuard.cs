using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pulsebreak.Api.Auth;

/// <summary>
/// Slows down password guessing. After a number of wrong passwords in a row, sign-in for
/// that account is refused for a while, and each further failure doubles the wait.
///
/// The count is kept per account being targeted, not per address doing the guessing, so
/// spreading guesses over many addresses does not get round it. The limit on requests per
/// address is a separate, second line of defence.
///
/// The cost is that someone can keep another player locked out by failing on purpose.
/// The waits are short and capped for that reason, and a correct password clears the count.
/// </summary>
public sealed class LoginGuard(IConnectionMultiplexer redis, IOptions<AuthOptions> options, TimeProvider clock)
{
    readonly IDatabase _db = redis.GetDatabase();
    readonly AuthOptions _options = options.Value;

    static string Key(string subject) => "pb:login-fail:" + subject;

    /// <summary>Seconds until this subject may try again, or 0 if they may try now.</summary>
    /// <param name="subject">The account id, or for a name that matches no account, a hash of that name.</param>
    public async Task<int> SecondsLockedAsync(string subject)
    {
        RedisValue until = await _db.HashGetAsync(Key(subject), "until");
        if (!until.TryParse(out long lockedUntil)) return 0;
        long remaining = lockedUntil - clock.GetUtcNow().ToUnixTimeSeconds();
        return remaining > 0 ? (int)remaining : 0;
    }

    public async Task RecordFailureAsync(string subject)
    {
        string key = Key(subject);
        long failures = await _db.HashIncrementAsync(key, "count");
        await _db.KeyExpireAsync(key, TimeSpan.FromHours(1));
        if (failures < _options.MaxFailedLogins) return;

        // 5th failure: one period. 6th: two. 7th: four. Never more than the cap.
        int doublings = (int)Math.Min(failures - _options.MaxFailedLogins, 16);
        long seconds = Math.Min((long)_options.LockoutSeconds << doublings, _options.MaxLockoutSeconds);
        await _db.HashSetAsync(key, "until", clock.GetUtcNow().ToUnixTimeSeconds() + seconds);
    }

    public Task ClearAsync(string subject) => _db.KeyDeleteAsync(Key(subject));
}
