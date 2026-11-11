using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pulsebreak.Api.Data;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Api.Auth;

/// <summary>
/// Makes the two kinds of signed token the API hands out.
///
/// An access token proves to this API who is calling. It lasts minutes, and is renewed
/// with a refresh token (see <see cref="SessionStore"/>).
///
/// A join ticket proves to a game server who is connecting. It lasts about a minute and is
/// signed with a different key, so a game server can check tickets but could never make
/// an access token.
/// </summary>
public sealed class TokenService(IOptions<AuthOptions> auth, IOptions<GameOptions> game, TimeProvider clock)
{
    public const string AccessAudience = "pulsebreak-api";
    public const string EmailVerifiedClaim = "ev";
    public const int MinKeyLength = 32;

    readonly AuthOptions _auth = auth.Value;
    readonly GameOptions _game = game.Value;
    readonly JsonWebTokenHandler _handler = new();

    public int AccessTokenSeconds => _auth.AccessTokenMinutes * 60;

    public static SymmetricSecurityKey SigningKey(AuthOptions options) => new(Encoding.UTF8.GetBytes(options.JwtSigningKey));

    public string CreateAccessToken(Account account)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _auth.Issuer,
            Audience = AccessAudience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_auth.AccessTokenMinutes),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = account.Id.ToString(),
                [JwtRegisteredClaimNames.Name] = account.Username,
                [EmailVerifiedClaim] = account.EmailVerifiedAt != null,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(SigningKey(_auth), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>A random refresh token. Only its hash is ever stored.</summary>
    public static string NewRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public bool TicketsEnabled => Encoding.UTF8.GetByteCount(_game.TicketSigningKey) >= GameTicket.MinKeyBytes;

    public int TicketSeconds => _game.TicketSeconds;

    public string CreateTicket(Account account, string mode)
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        return GameTicket.Create(new TicketClaims
        {
            AccountId = account.Id.ToString(),
            Name = account.Username,
            EmailVerified = account.EmailVerifiedAt != null,
            Mode = mode,
            ExpiresAt = now + _game.TicketSeconds,
        }, Encoding.UTF8.GetBytes(_game.TicketSigningKey), now);
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
