using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Matchmaking;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Api.Endpoints;

public sealed record GuestRequest(string? Name);
public sealed record GuestResponse(string Token, string GuestId, string Name, int ExpiresIn);
public sealed record QueueRequest(string? Playlist, uint Version);

/// <summary>A place in a queue as the game sees it.</summary>
public sealed record TicketStatus(string Id, string Status, string Playlist, int WaitedSeconds, int Searching,
    string Address, string JoinTicket, string MatchId);

public sealed record PlaylistInfo(string Id, string Title, int Players, int Searching);

public sealed record RegisterServerRequest(string? Address, int Port, uint Version);
public sealed record RegisterServerResponse(string Id, float HeartbeatSeconds);
public sealed record HeartbeatRequest(string? State, string? MatchId, int Players);
/// <summary>Empty strings when there is nothing new to play.</summary>
public sealed record HeartbeatResponse(string MatchId, string Playlist);

/// <summary>
/// Finding a match, for players (signed in, or with a guest pass), and offering servers to
/// play matches on, for the game servers themselves (with the fleet key).
/// </summary>
public static class MatchmakingEndpoints
{
    public const string FleetKeyHeader = "X-Fleet-Key";

    public static void Map(IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder players = routes.MapGroup("/v1/matchmaking");
        players.MapPost("/guest", CreateGuest).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        players.MapGet("/playlists", ListPlaylists);
        players.MapPost("/tickets", Enqueue);
        players.MapGet("/tickets/{id}", Check);
        players.MapDelete("/tickets/{id}", Cancel);

        RouteGroupBuilder fleet = routes.MapGroup("/v1/fleet").AddEndpointFilter(RequireFleetKey);
        fleet.MapPost("/servers", Register);
        fleet.MapPost("/servers/{id}/heartbeat", Heartbeat);
        fleet.MapDelete("/servers/{id}", Deregister);
    }

    // ------------------------------------------------------------------ players

    static IResult CreateGuest(GuestRequest? request, GuestPasses guests)
    {
        (string token, Caller guest) = guests.Issue(request?.Name);
        return Results.Ok(new GuestResponse(token, guest.Key, guest.Name, guests.Seconds));
    }

    static async Task<IResult> ListPlaylists(MatchmakingService matchmaking)
    {
        var list = new List<PlaylistInfo>();
        foreach (Playlist playlist in Playlists.All)
            list.Add(new PlaylistInfo(playlist.Id, playlist.Title, playlist.Players, await matchmaking.SearchingAsync(playlist.Id)));
        return Results.Ok(list);
    }

    static async Task<IResult> Enqueue(QueueRequest? request, HttpContext http, GuestPasses guests, AppDbContext db,
        MatchmakingService matchmaking, TimeProvider clock)
    {
        Caller? caller = await WhoAsync(http, guests, db);
        if (caller == null) return Api.Unauthorized();

        Playlist? playlist = Playlists.Find((request?.Playlist ?? "").Trim().ToLowerInvariant());
        if (playlist == null)
            return Api.Invalid([new FieldError("playlist", "Pick one of: " + string.Join(", ", Playlists.All.Select(p => p.Id)) + ".")]);
        if (request!.Version == 0)
            return Api.Invalid([new FieldError("version", "Send the game's network version, so you are only matched with servers that can talk to you.")]);

        MatchTicket ticket = await matchmaking.EnqueueAsync(caller, playlist, request.Version);
        return Results.Created("/v1/matchmaking/tickets/" + ticket.Id, await StatusOf(ticket, matchmaking, clock));
    }

    static async Task<IResult> Check(string id, HttpContext http, GuestPasses guests, AppDbContext db, MatchmakingService matchmaking,
        TimeProvider clock)
    {
        Caller? caller = await WhoAsync(http, guests, db);
        if (caller == null) return Api.Unauthorized();
        MatchTicket? ticket = await matchmaking.CheckAsync(id, caller.Key);
        return ticket != null ? Results.Ok(await StatusOf(ticket, matchmaking, clock)) : NoTicket();
    }

    static async Task<IResult> Cancel(string id, HttpContext http, GuestPasses guests, AppDbContext db, MatchmakingService matchmaking)
    {
        Caller? caller = await WhoAsync(http, guests, db);
        if (caller == null) return Api.Unauthorized();
        return await matchmaking.CancelAsync(id, caller.Key) ? Results.NoContent() : NoTicket();
    }

    static IResult NoTicket() => Api.Error(StatusCodes.Status404NotFound, "no_ticket", "That search is not running any more. Find a match again.");

    static async Task<TicketStatus> StatusOf(MatchTicket ticket, MatchmakingService matchmaking, TimeProvider clock) => new(
        ticket.Id, ticket.Status, ticket.Playlist,
        (int)Math.Max(0, (clock.GetUtcNow().ToUnixTimeMilliseconds() - ticket.CreatedMs) / 1000),
        ticket.Status == MatchTicket.Searching ? await matchmaking.SearchingAsync(ticket.Playlist) : 0,
        ticket.Address, ticket.JoinTicket, ticket.MatchId);

    /// <summary>
    /// Who is calling: a signed-in account (an access token), or a guest (a guest pass).
    /// Matchmaking accepts either; everything else in the API wants an account.
    /// </summary>
    static async Task<Caller?> WhoAsync(HttpContext http, GuestPasses guests, AppDbContext db)
    {
        if (http.User.TryGetAccountId(out _))
        {
            Account? account = await db.CurrentAccountAsync(http.User);
            return account != null ? new Caller(account.Id.ToString(), account.Username, false) : null;
        }
        string header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? await guests.ReadAsync(header["Bearer ".Length..].Trim()) : null;
    }

    // ------------------------------------------------------------------ game servers

    static async ValueTask<object?> RequireFleetKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        MatchmakingService matchmaking = http.RequestServices.GetRequiredService<MatchmakingService>();
        if (!matchmaking.FleetEnabled)
            return Api.Error(StatusCodes.Status503ServiceUnavailable, "fleet_disabled", "This backend has no fleet key, so it takes no game servers.");

        string expected = http.RequestServices.GetRequiredService<IOptions<MatchmakingOptions>>().Value.FleetKey;
        string given = http.Request.Headers[FleetKeyHeader].ToString();
        bool match = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        return match ? await next(context) : Api.Error(StatusCodes.Status403Forbidden, "bad_fleet_key", "That is not this fleet's key.");
    }

    static async Task<IResult> Register(RegisterServerRequest? request, MatchmakingService matchmaking, IOptions<MatchmakingOptions> options)
    {
        string address = (request?.Address ?? "").Trim();
        var errors = new List<FieldError>();
        if (address.Length is 0 or > 255 || address.Contains(' ')) errors.Add(new FieldError("address", "Give the address players should connect to."));
        if (request == null || request.Port is < 1 or > 65535) errors.Add(new FieldError("port", "The port must be between 1 and 65535."));
        if (request == null || request.Version == 0) errors.Add(new FieldError("version", "Send the game's network version."));
        if (errors.Count > 0) return Api.Invalid(errors);

        FleetServer server = await matchmaking.RegisterAsync(address, request!.Port, request.Version);
        return Results.Ok(new RegisterServerResponse(server.Id, options.Value.HeartbeatSeconds));
    }

    static async Task<IResult> Heartbeat(string id, HeartbeatRequest? request, MatchmakingService matchmaking)
    {
        FleetServer? server = await matchmaking.HeartbeatAsync(id, request?.State ?? "busy", request?.MatchId ?? "", request?.Players ?? 0);
        if (server == null) return Api.Error(StatusCodes.Status404NotFound, "unknown_server", "This server is not registered. Register again.");

        bool newMatch = server.AssignedMatch.Length > 0 && server.AssignedMatch != server.ReportedMatch;
        return Results.Ok(newMatch ? new HeartbeatResponse(server.AssignedMatch, server.AssignedPlaylist) : new HeartbeatResponse("", ""));
    }

    static async Task<IResult> Deregister(string id, MatchmakingService matchmaking)
    {
        await matchmaking.RemoveServerAsync(id);
        return Results.NoContent();
    }
}
