using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;

namespace Pulsebreak.Api;

/// <summary>The body of every error reply. A machine-readable code, a sentence for a person, and which fields were at fault.</summary>
public sealed record ApiError(string Code, string Message, IReadOnlyList<FieldError> Fields);

public sealed record ErrorBody(ApiError Error);

public sealed record ProfileDto(int Level, int Xp, int Currency, int Rating);

public sealed record AccountDto(string Id, string Username, string Email, bool EmailVerified, long CreatedAt, ProfileDto Profile)
{
    public static AccountDto From(Account account) => new(
        account.Id.ToString(), account.Username, account.Email, account.EmailVerifiedAt != null,
        account.CreatedAt.ToUnixTimeSeconds(),
        new ProfileDto(account.Profile.Level, account.Profile.Xp, account.Profile.Currency, account.Profile.Rating));
}

/// <summary>What a successful sign-in returns: who you are, and the two tokens that keep you signed in.</summary>
public sealed record SessionDto(string AccessToken, int ExpiresIn, string RefreshToken, AccountDto Account);

public static class Api
{
    public static IResult Error(int status, string code, string message, IEnumerable<FieldError>? fields = null) =>
        Results.Json(new ErrorBody(new ApiError(code, message, fields?.ToList() ?? [])), statusCode: status);

    public static IResult Invalid(IEnumerable<FieldError> fields) =>
        Error(StatusCodes.Status422UnprocessableEntity, "invalid_request", "Some of what was sent cannot be accepted.", fields);

    public static IResult Unauthorized() =>
        Error(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to do that.");

    /// <summary>The account id inside a validated access token, if there is one.</summary>
    public static bool TryGetAccountId(this ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue("sub"), out accountId);

    /// <summary>The signed-in account, with its profile. Null if it no longer exists or was disabled since the token was issued.</summary>
    public static async Task<Account?> CurrentAccountAsync(this AppDbContext db, ClaimsPrincipal user)
    {
        if (!user.TryGetAccountId(out Guid id)) return null;
        Account? account = await db.Accounts.Include(a => a.Profile).FirstOrDefaultAsync(a => a.Id == id);
        return account is { Disabled: false } ? account : null;
    }

    /// <summary>True if saving failed because a unique value (email or username) was taken a moment before.</summary>
    public static bool IsUniqueViolation(DbUpdateException error) =>
        error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static void Add(this List<FieldError> errors, string field, string? problem)
    {
        if (problem != null) errors.Add(new FieldError(field, problem));
    }
}
