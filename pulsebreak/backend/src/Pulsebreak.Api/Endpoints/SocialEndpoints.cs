using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Social;

namespace Pulsebreak.Api.Endpoints;

public sealed record PollRequest(string? Status, long ChatAfter);
public sealed record FriendRequest(string? Username);
public sealed record InviteRequest(string? AccountId);
public sealed record ChatRequest(string? Text);
public sealed record ReportStatusRequest(string? Status);
public sealed record ReportDto(string Id, string Reporter, string Reported, string ReportedId, string Reason, string Details, string MatchId,
    string Chat, long CreatedAt, string Status);

/// <summary>Friends, blocks, reports, parties and party chat, for signed-in players; and the report list, for moderators.</summary>
public static class SocialEndpoints
{
    public const string AdminKeyHeader = "X-Admin-Key";

    public static void Map(IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder social = routes.MapGroup("/v1").RequireAuthorization();
        social.MapPost("/social/poll", Poll);
        social.MapPost("/friends", RequestFriend);
        social.MapPost("/friends/{id:guid}/accept", (Guid id, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.AcceptAsync(me, id)));
        social.MapDelete("/friends/{id:guid}", (Guid id, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.RemoveAsync(me, id)));
        social.MapPost("/blocks/{id:guid}", (Guid id, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.BlockAsync(me, id)));
        social.MapDelete("/blocks/{id:guid}", (Guid id, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.UnblockAsync(me, id)));
        social.MapPost("/reports", (ReportRequest? request, HttpContext http, AppDbContext db, SocialService s) =>
            Run(http, db, me => s.ReportAsync(me, request ?? new ReportRequest(null, null, null, null, null)), StatusCodes.Status202Accepted));
        social.MapPost("/party/invites", (InviteRequest? request, HttpContext http, AppDbContext db, SocialService s) =>
            Guid.TryParse(request?.AccountId, out Guid friend) ? Run(http, db, me => s.InviteAsync(me, friend)) : Task.FromResult(Invalid("accountId")));
        social.MapPost("/party/invites/{partyId}/accept", (string partyId, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.JoinAsync(me, partyId)));
        social.MapDelete("/party/invites/{partyId}", (string partyId, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.DeclineAsync(me, partyId)));
        social.MapPost("/party/leave", (HttpContext http, AppDbContext db, SocialService s) => Run(http, db, s.LeaveAsync));
        social.MapPost("/party/members/{id:guid}/kick", (Guid id, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.KickAsync(me, id)));
        social.MapPost("/party/chat", (ChatRequest? request, HttpContext http, AppDbContext db, SocialService s) => Run(http, db, me => s.ChatAsync(me, request?.Text)));

        RouteGroupBuilder admin = routes.MapGroup("/v1/admin").AddEndpointFilter(RequireAdminKey);
        admin.MapGet("/reports", ListReports);
        admin.MapPost("/reports/{id:guid}/status", SetReportStatus);
    }

    static async Task<IResult> Poll(PollRequest? request, HttpContext http, AppDbContext db, SocialService social)
    {
        Account? me = await db.CurrentAccountAsync(http.User);
        if (me == null) return Api.Unauthorized();
        return Results.Ok(await social.PollAsync(me, request?.Status, Math.Max(0, request?.ChatAfter ?? 0)));
    }

    static async Task<IResult> RequestFriend(FriendRequest? request, HttpContext http, AppDbContext db, SocialService social)
    {
        Account? me = await db.CurrentAccountAsync(http.User);
        if (me == null) return Api.Unauthorized();
        (SocialResult result, PersonDto? person) = await social.RequestAsync(me, request?.Username);
        return result == SocialResult.Ok ? Results.Created("/v1/friends/" + person!.AccountId, person) : Problem(result);
    }

    static async Task<IResult> Run(HttpContext http, AppDbContext db, Func<Account, Task<SocialResult>> action, int success = StatusCodes.Status204NoContent)
    {
        Account? me = await db.CurrentAccountAsync(http.User);
        if (me == null) return Api.Unauthorized();
        SocialResult result = await action(me);
        return result == SocialResult.Ok ? Results.StatusCode(success) : Problem(result);
    }

    /// <summary>A sentence for each way a social action can be refused.</summary>
    public static IResult Problem(SocialResult result) => result switch
    {
        SocialResult.NotFound => Api.Error(StatusCodes.Status404NotFound, "not_found", "There is nobody by that name, or nothing to do with them."),
        SocialResult.Self => Api.Error(StatusCodes.Status409Conflict, "self", "That is you."),
        SocialResult.Blocked => Api.Error(StatusCodes.Status409Conflict, "blocked", "You cannot do that with this player."),
        SocialResult.AlreadyFriends => Api.Error(StatusCodes.Status409Conflict, "already_friends", "You are already friends."),
        SocialResult.TooManyFriends => Api.Error(StatusCodes.Status409Conflict, "too_many_friends", "Your friends list is full."),
        SocialResult.NotFriends => Api.Error(StatusCodes.Status409Conflict, "not_friends", "You can only invite friends to a party."),
        SocialResult.PartyFull => Api.Error(StatusCodes.Status409Conflict, "party_full", "That party is full."),
        SocialResult.NotLeader => Api.Error(StatusCodes.Status403Forbidden, "not_leader", "Only the party leader can do that."),
        SocialResult.NoInvite => Api.Error(StatusCodes.Status404NotFound, "no_invite", "That invite has run out or been withdrawn."),
        SocialResult.NotInParty => Api.Error(StatusCodes.Status409Conflict, "not_in_party", "You are not in a party."),
        SocialResult.RateLimited => Api.Error(StatusCodes.Status429TooManyRequests, "slow_down", "That is a lot in a short time. Wait a moment."),
        _ => Invalid("request"),
    };

    static IResult Invalid(string field) => Api.Invalid([new FieldError(field, "That cannot be accepted.")]);

    // ------------------------------------------------------------------ moderation

    static async ValueTask<object?> RequireAdminKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<SocialOptions>>().Value.AdminKey;
        string given = context.HttpContext.Request.Headers[AdminKeyHeader].ToString();
        if (Encoding.UTF8.GetByteCount(expected) < 32 ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
            return Api.Error(StatusCodes.Status401Unauthorized, "admin_key", "This needs the moderation key.");
        return await next(context);
    }

    static async Task<IResult> ListReports(AppDbContext db, string? status, int? limit)
    {
        string wanted = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        var rows = await (from report in db.Reports
                          join reporter in db.Accounts on report.ReporterId equals reporter.Id
                          join reported in db.Accounts on report.ReportedId equals reported.Id
                          where report.Status == wanted
                          orderby report.CreatedAt descending
                          select new { report, Reporter = reporter.Username, Reported = reported.Username })
            .Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync();
        return Results.Ok(rows.Select(r => new ReportDto(r.report.Id.ToString(), r.Reporter, r.Reported, r.report.ReportedId.ToString(),
            r.report.Reason, r.report.Details, r.report.MatchId, r.report.Chat, r.report.CreatedAt.ToUnixTimeSeconds(), r.report.Status)));
    }

    static async Task<IResult> SetReportStatus(Guid id, ReportStatusRequest? request, AppDbContext db)
    {
        string status = (request?.Status ?? "").Trim().ToLowerInvariant();
        if (status.Length is 0 or > 16) return Invalid("status");
        ReportRow? report = await db.Reports.FindAsync(id);
        if (report == null) return Api.Error(StatusCodes.Status404NotFound, "not_found", "There is no such report.");
        report.Status = status;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
