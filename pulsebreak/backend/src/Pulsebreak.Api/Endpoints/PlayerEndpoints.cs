using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Loadouts;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Api.Endpoints;

/// <summary>The settings that follow a player between machines. Out-of-range values are pulled back into range rather than refused.</summary>
public sealed record SettingsData(float Sensitivity, float AdsSensitivityMultiplier, float FieldOfView, bool ShowDamageNumbers, float MasterVolume)
{
    public SettingsData Clamped() => new(
        Math.Clamp(Sensitivity, 0.2f, 8f),
        Math.Clamp(AdsSensitivityMultiplier, 0.3f, 1.5f),
        Math.Clamp(FieldOfView, 70f, 120f),
        ShowDamageNumbers,
        Math.Clamp(MasterVolume, 0f, 1f));
}

/// <param name="Data">Null if the player has never saved settings.</param>
/// <param name="UpdatedAt">When they were last changed, by the player's clock. Seconds since 1970.</param>
public sealed record SettingsDocument(SettingsData? Data, long UpdatedAt);

public sealed record TicketRequest(string? Mode);
public sealed record TicketResponse(string Ticket, int ExpiresIn);

/// <summary>Everything a signed-in player can read or change about themselves.</summary>
public static class PlayerEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder player = routes.MapGroup("/v1").RequireAuthorization();

        player.MapGet("/me", Me);
        player.MapGet("/loadouts", GetLoadouts);
        player.MapPost("/loadouts/sync", SyncLoadouts);
        player.MapGet("/settings", GetSettings);
        player.MapPut("/settings", PutSettings);
        player.MapPost("/game/tickets", CreateTicket);
    }

    static async Task<IResult> Me(ClaimsPrincipal user, AppDbContext db)
    {
        Account? account = await db.CurrentAccountAsync(user);
        return account != null ? Results.Ok(AccountDto.From(account)) : Api.Unauthorized();
    }

    // ------------------------------------------------------------------ loadouts

    static async Task<IResult> GetLoadouts(ClaimsPrincipal user, AppDbContext db, LoadoutService loadouts)
    {
        Account? account = await db.CurrentAccountAsync(user);
        return account != null ? Results.Ok(await loadouts.GetAsync(account.Id)) : Api.Unauthorized();
    }

    static async Task<IResult> SyncLoadouts(LoadoutCollection? incoming, ClaimsPrincipal user, AppDbContext db, LoadoutService loadouts)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();

        List<FieldError> errors = loadouts.Check(incoming);
        if (errors.Count > 0) return Api.Invalid(errors);
        return Results.Ok(await loadouts.SyncAsync(account.Id, incoming!));
    }

    // ------------------------------------------------------------------ settings

    static async Task<IResult> GetSettings(ClaimsPrincipal user, AppDbContext db)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();
        return Results.Ok(ToDocument(await db.Settings.FirstOrDefaultAsync(s => s.AccountId == account.Id)));
    }

    /// <summary>Saves settings if they are newer than what is stored, and returns whichever won.</summary>
    static async Task<IResult> PutSettings(SettingsDocument? document, ClaimsPrincipal user, AppDbContext db, TimeProvider clock)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();
        if (document?.Data == null) return Api.Invalid([new FieldError("data", "Send the settings to save.")]);

        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        long updatedAt = document.UpdatedAt < 0 ? 0 : document.UpdatedAt > now + LoadoutService.MaxClockSkewSeconds ? now : document.UpdatedAt;

        SettingsRow? row = await db.Settings.FirstOrDefaultAsync(s => s.AccountId == account.Id);
        if (row == null)
        {
            row = new SettingsRow { AccountId = account.Id, UpdatedAt = -1 };
            db.Settings.Add(row);
        }
        if (updatedAt > row.UpdatedAt)
        {
            row.Data = JsonSerializer.Serialize(document.Data.Clamped(), JsonSerializerOptions.Web);
            row.UpdatedAt = updatedAt;
            await db.SaveChangesAsync();
        }
        return Results.Ok(ToDocument(row));
    }

    static SettingsDocument ToDocument(SettingsRow? row) => row == null || row.UpdatedAt < 0
        ? new SettingsDocument(null, 0)
        : new SettingsDocument(JsonSerializer.Deserialize<SettingsData>(row.Data, JsonSerializerOptions.Web), row.UpdatedAt);

    // ------------------------------------------------------------------ joining a game server

    /// <summary>
    /// Hands out a ticket to show a game server. This is where "verify your email before
    /// ranked" is enforced: no verified email, no ranked ticket, and a game server running
    /// ranked matches lets nobody in without one.
    /// </summary>
    static async Task<IResult> CreateTicket(TicketRequest request, ClaimsPrincipal user, AppDbContext db, TokenService tokens)
    {
        Account? account = await db.CurrentAccountAsync(user);
        if (account == null) return Api.Unauthorized();

        string mode = (request.Mode ?? GameTicket.Casual).Trim().ToLowerInvariant();
        if (mode != GameTicket.Casual && mode != GameTicket.Ranked)
            return Api.Invalid([new FieldError("mode", "The mode is either \"casual\" or \"ranked\".")]);
        if (mode == GameTicket.Ranked && account.EmailVerifiedAt == null)
            return Api.Error(StatusCodes.Status403Forbidden, "email_not_verified", "Verify your email address to play ranked.");
        if (!tokens.TicketsEnabled)
            return Api.Error(StatusCodes.Status503ServiceUnavailable, "tickets_unavailable", "This server is not set up to hand out game tickets.");

        return Results.Ok(new TicketResponse(tokens.CreateTicket(account, mode), tokens.TicketSeconds));
    }
}
