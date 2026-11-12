using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pulsebreak.Api.Auth;

namespace Pulsebreak.Api.Matchmaking;

/// <summary>Who is asking for a match: an account, or a guest. <see cref="Key"/> is what a game server knows them by.</summary>
/// <param name="Verified">An account with a verified email: allowed into ranked. Never true for a guest.</param>
public sealed record Caller(string Key, string Name, bool Guest, bool Verified = false);

/// <summary>
/// Passes for playing without an account. A player who is not signed in gets a guest pass
/// for casual matchmaking: a signed token naming a random guest id and the name they chose.
/// It is good for matchmaking only, never ranked. The API's own sign-in refuses
/// it, because it is made out to a different audience.
/// </summary>
public sealed class GuestPasses(IOptions<AuthOptions> auth, IOptions<MatchmakingOptions> options, TimeProvider clock)
{
    public const string Audience = "pulsebreak-guest";
    public const int MaxNameLength = 24;

    readonly AuthOptions _auth = auth.Value;
    readonly JsonWebTokenHandler _handler = new();

    public int Seconds => options.Value.GuestPassHours * 3600;

    public (string Token, Caller Guest) Issue(string? requestedName)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var guest = new Caller("guest:" + Guid.NewGuid().ToString("N")[..16], CleanName(requestedName), true);
        string token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _auth.Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(Seconds),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = guest.Key,
                [JwtRegisteredClaimNames.Name] = guest.Name,
            },
            SigningCredentials = new SigningCredentials(TokenService.SigningKey(_auth), SecurityAlgorithms.HmacSha256),
        });
        return (token, guest);
    }

    /// <summary>The guest a pass names, or null if it is not a valid, current guest pass.</summary>
    public async Task<Caller?> ReadAsync(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048) return null;
        TokenValidationResult result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _auth.Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = TokenService.SigningKey(_auth),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) => expires != null && expires > clock.GetUtcNow().UtcDateTime.AddSeconds(-30),
        });
        if (!result.IsValid) return null;
        string key = result.ClaimsIdentity.FindFirst("sub")?.Value ?? "";
        string name = result.ClaimsIdentity.FindFirst("name")?.Value ?? "";
        return key.StartsWith("guest:", StringComparison.Ordinal) && name.Length > 0 ? new Caller(key, name, true) : null;
    }

    /// <summary>
    /// A name fit to show other players: letters, digits, spaces and a little punctuation,
    /// no more than <see cref="MaxNameLength"/> characters, never empty.
    /// </summary>
    public static string CleanName(string? name)
    {
        var clean = new StringBuilder(MaxNameLength);
        bool space = false;
        foreach (char c in (name ?? "").Trim())
        {
            if (clean.Length >= MaxNameLength) break;
            if (char.IsWhiteSpace(c))
            {
                space = clean.Length > 0;
                continue;
            }
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.') continue;
            if (space && clean.Length < MaxNameLength - 1) clean.Append(' ');
            space = false;
            clean.Append(c);
        }
        return clean.Length > 0 ? clean.ToString() : "Player";
    }
}
