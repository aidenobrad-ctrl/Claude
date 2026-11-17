using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;

namespace Pulsebreak.Api.Endpoints;

public sealed record RegisterRequest(string? Email, string? Username, string? Password);
public sealed record LoginRequest(string? Login, string? Password);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record VerifyEmailRequest(string? Code);
public sealed record ForgotPasswordRequest(string? Email);
public sealed record ResetPasswordRequest(string? Email, string? Code, string? NewPassword);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>Everything to do with proving who you are.</summary>
public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void Map(IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder auth = routes.MapGroup("/v1/auth").RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        auth.MapPost("/refresh", Refresh);
        auth.MapPost("/logout", Logout);
        auth.MapPost("/forgot-password", ForgotPassword);
        auth.MapPost("/reset-password", ResetPassword);
        auth.MapGet("/verify-email", VerifyEmailLink);

        auth.MapPost("/logout-all", LogoutAll).RequireAuthorization();
        auth.MapPost("/verify-email", VerifyEmailCode).RequireAuthorization();
        auth.MapPost("/resend-verification", ResendVerification).RequireAuthorization();
        auth.MapPost("/change-password", ChangePassword).RequireAuthorization();
    }

    static async Task<IResult> Register(RegisterRequest request, AppDbContext db, PasswordHasher hasher,
        AccountMailer mailer, SessionIssuer sessions, TimeProvider clock)
    {
        var errors = new List<FieldError>();
        errors.Add("email", Rules.CheckEmail(request.Email));
        errors.Add("username", Rules.CheckUsername(request.Username));
        errors.Add("password", Rules.CheckPassword(request.Password, request.Username, request.Email));
        if (errors.Count > 0) return Api.Invalid(errors);

        string email = request.Email!.Trim(), username = request.Username!.Trim();
        string emailKey = Rules.NormalizeEmail(email), usernameKey = Rules.NormalizeUsername(username);

        if (await db.Accounts.AnyAsync(a => a.EmailNormalized == emailKey)) errors.Add("email", "An account already uses that email address.");
        if (await db.Accounts.AnyAsync(a => a.UsernameNormalized == usernameKey)) errors.Add("username", "That username is taken.");
        if (errors.Count > 0) return Api.Error(StatusCodes.Status409Conflict, "already_exists", "That email address or username is already in use.", errors);

        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailNormalized = emailKey,
            Username = username,
            UsernameNormalized = usernameKey,
            PasswordHash = await hasher.HashAsync(request.Password!),
            CreatedAt = clock.GetUtcNow(),
            LastLoginAt = clock.GetUtcNow(),
            Profile = new Profile(),
        };
        db.Accounts.Add(account);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (Api.IsUniqueViolation(error))
        {
            // Someone else took the name between the check above and the save.
            return Api.Error(StatusCodes.Status409Conflict, "already_exists", "That email address or username is already in use.");
        }

        await mailer.SendVerificationAsync(account);
        return Results.Json(await sessions.StartAsync(account), statusCode: StatusCodes.Status201Created);
    }

    static async Task<IResult> Login(LoginRequest request, HttpContext http, AppDbContext db, PasswordHasher hasher,
        LoginGuard guard, SessionIssuer sessions, TimeProvider clock)
    {
        string login = (request.Login ?? "").Trim(), password = request.Password ?? "";
        if (login.Length == 0 || login.Length > Rules.MaxEmailLength || password.Length == 0 || password.Length > Rules.MaxPasswordLength)
            return InvalidCredentials();

        // An email address has an @ and a username cannot, so one box serves for both.
        Account? account;
        string key;
        if (login.Contains('@'))
        {
            key = Rules.NormalizeEmail(login);
            account = await db.Accounts.Include(a => a.Profile).FirstOrDefaultAsync(a => a.EmailNormalized == key);
        }
        else
        {
            key = Rules.NormalizeUsername(login);
            account = await db.Accounts.Include(a => a.Profile).FirstOrDefaultAsync(a => a.UsernameNormalized == key);
        }

        // Names that match no account are counted too, so being locked out says nothing
        // about whether an account exists.
        string subject = account != null ? account.Id.ToString("N") : "unknown:" + TokenService.Hash(key);
        int locked = await guard.SecondsLockedAsync(subject);
        if (locked > 0)
        {
            http.Response.Headers.RetryAfter = locked.ToString();
            return Api.Error(StatusCodes.Status429TooManyRequests, "too_many_attempts",
                $"Too many wrong passwords. Try again in {(locked >= 120 ? locked / 60 + " minutes" : locked + " seconds")}.");
        }

        if (account == null)
        {
            await hasher.BurnAsync(password);
            await guard.RecordFailureAsync(subject);
            return InvalidCredentials();
        }

        PasswordCheck check = await hasher.VerifyAsync(password, account.PasswordHash);
        if (check == PasswordCheck.Wrong)
        {
            await guard.RecordFailureAsync(subject);
            return InvalidCredentials();
        }
        if (account.Disabled) return Api.Error(StatusCodes.Status403Forbidden, "account_disabled", "This account has been disabled.");

        await guard.ClearAsync(subject);
        if (check == PasswordCheck.CorrectNeedsRehash) account.PasswordHash = await hasher.HashAsync(password);
        account.LastLoginAt = clock.GetUtcNow();
        await db.SaveChangesAsync();

        return Results.Ok(await sessions.StartAsync(account));
    }

    /// <summary>One answer for a wrong password and for an account that does not exist.</summary>
    static IResult InvalidCredentials() =>
        Api.Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "That email or username and password do not match an account.");

    static async Task<IResult> Refresh(RefreshRequest request, AppDbContext db, SessionStore store, SessionIssuer sessions)
    {
        RefreshResult result = await store.RotateAsync(request.RefreshToken ?? "");
        if (result.Outcome == RefreshOutcome.Reused)
            return Api.Error(StatusCodes.Status401Unauthorized, "session_revoked", "This session was ended for your safety. Sign in again.");
        if (result.Outcome != RefreshOutcome.Rotated)
            return Api.Error(StatusCodes.Status401Unauthorized, "invalid_refresh_token", "Your session has expired. Sign in again.");

        Account? account = await db.Accounts.Include(a => a.Profile).FirstOrDefaultAsync(a => a.Id == result.AccountId);
        if (account == null || account.Disabled)
        {
            await store.EndAllAsync(result.AccountId);
            return Api.Error(StatusCodes.Status401Unauthorized, "invalid_refresh_token", "Your session has expired. Sign in again.");
        }
        return Results.Ok(sessions.Reply(account, result.NewToken));
    }

    static async Task<IResult> Logout(RefreshRequest request, SessionStore store)
    {
        await store.EndAsync(request.RefreshToken ?? "");
        return Results.NoContent();
    }

    static async Task<IResult> LogoutAll(ClaimsPrincipal user, SessionStore store, TokenRevocation revocation)
    {
        if (!user.TryGetAccountId(out Guid accountId)) return Api.Unauthorized();
        await store.EndAllAsync(accountId);
        await revocation.RevokeAllAsync(accountId);
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ email verification

    static async Task<IResult> VerifyEmailCode(VerifyEmailRequest request, ClaimsPrincipal user, AppDbContext db, EmailCodes codes, TimeProvider clock)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();
        if (account.EmailVerifiedAt != null) return Results.NoContent();

        if (!await codes.RedeemAsync(CodeKind.VerifyEmail, account.Id, request.Code))
            return Api.Error(StatusCodes.Status400BadRequest, "invalid_code", "That code is wrong or has expired. Ask for a new one.");

        account.EmailVerifiedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>The link in the verification email. Opened in a browser, so it answers with a page, not JSON.</summary>
    static async Task<IResult> VerifyEmailLink(string? token, AppDbContext db, EmailCodes codes, TimeProvider clock)
    {
        Guid? accountId = await codes.RedeemVerifyLinkAsync(token);
        Account? account = accountId != null ? await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId) : null;
        if (account == null)
            return Page("This link has expired", "Ask for a new verification email from the game and use the newest one.", StatusCodes.Status400BadRequest);

        if (account.EmailVerifiedAt == null)
        {
            account.EmailVerifiedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
            await codes.ForgetAsync(CodeKind.VerifyEmail, account.Id);
        }
        return Page("Email verified", "You can close this page and go back to the game. Ranked play is now open to you.", StatusCodes.Status200OK);
    }

    static IResult Page(string title, string text, int status) => Results.Content(
        $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        $"<title>{title} - Pulsebreak</title><style>body{{font-family:system-ui,sans-serif;background:#121720;color:#f0f4fa;display:grid;place-items:center;min-height:100vh;margin:0}}" +
        $"main{{max-width:28rem;padding:2rem;border-top:3px solid #26d8ff;background:#1a2130;border-radius:10px}}h1{{margin-top:0}}</style></head>" +
        $"<body><main><h1>{title}</h1><p>{text}</p></main></body></html>",
        "text/html; charset=utf-8", statusCode: status);

    static async Task<IResult> ResendVerification(ClaimsPrincipal user, AppDbContext db, AccountMailer mailer)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();
        if (account.EmailVerifiedAt != null) return Results.NoContent();

        return await mailer.SendVerificationAsync(account)
            ? Results.Accepted()
            : Api.Error(StatusCodes.Status429TooManyRequests, "too_soon", "A verification email was sent a moment ago. Check your inbox, then try again in a minute.");
    }

    // ------------------------------------------------------------------ passwords

    static async Task<IResult> ForgotPassword(ForgotPasswordRequest request, AppDbContext db, AccountMailer mailer)
    {
        // The reply is the same whether or not the address has an account.
        if (Rules.CheckEmail(request.Email) == null)
        {
            string key = Rules.NormalizeEmail(request.Email!);
            Account? account = await db.Accounts.FirstOrDefaultAsync(a => a.EmailNormalized == key);
            if (account is { Disabled: false }) await mailer.SendPasswordResetAsync(account);
        }
        return Results.Accepted();
    }

    static async Task<IResult> ResetPassword(ResetPasswordRequest request, AppDbContext db, PasswordHasher hasher, EmailCodes codes,
        SessionStore store, TokenRevocation revocation, LoginGuard guard, AccountMailer mailer)
    {
        string emailKey = Rules.NormalizeEmail(request.Email ?? "");
        Account? account = emailKey.Length is > 0 and <= Rules.MaxEmailLength
            ? await db.Accounts.FirstOrDefaultAsync(a => a.EmailNormalized == emailKey)
            : null;

        // Check the new password before spending the code, so a rejected password does not cost a fresh email.
        string? problem = Rules.CheckPassword(request.NewPassword, account?.Username, request.Email);
        if (problem != null) return Api.Invalid([new FieldError("newPassword", problem)]);

        if (account == null || account.Disabled || !await codes.RedeemAsync(CodeKind.ResetPassword, account.Id, request.Code))
            return Api.Error(StatusCodes.Status400BadRequest, "invalid_code", "That code is wrong or has expired. Ask for a new one.");

        account.PasswordHash = await hasher.HashAsync(request.NewPassword!);
        await db.SaveChangesAsync();

        // Whoever knew the old password is signed out everywhere, at once.
        await store.EndAllAsync(account.Id);
        await revocation.RevokeAllAsync(account.Id);
        await guard.ClearAsync(account.Id.ToString("N"));
        mailer.SendPasswordChanged(account);
        return Results.NoContent();
    }

    static async Task<IResult> ChangePassword(ChangePasswordRequest request, ClaimsPrincipal user, AppDbContext db, PasswordHasher hasher,
        SessionStore store, TokenRevocation revocation, SessionIssuer sessions, AccountMailer mailer)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();

        string current = request.CurrentPassword ?? "";
        if (current.Length is 0 or > Rules.MaxPasswordLength || await hasher.VerifyAsync(current, account.PasswordHash) == PasswordCheck.Wrong)
            return Api.Invalid([new FieldError("currentPassword", "That is not your current password.")]);

        string? problem = Rules.CheckPassword(request.NewPassword, account.Username, account.Email);
        if (problem != null) return Api.Invalid([new FieldError("newPassword", problem)]);

        account.PasswordHash = await hasher.HashAsync(request.NewPassword!);
        await db.SaveChangesAsync();

        // Every other device is signed out; this one gets a fresh session in the reply.
        await store.EndAllAsync(account.Id);
        await revocation.RevokeAllAsync(account.Id);
        mailer.SendPasswordChanged(account);
        return Results.Ok(await sessions.StartAsync(account));
    }
}
