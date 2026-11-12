using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pulsebreak.Api.Auth;
using Pulsebreak.Api.Data;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Api.Loadouts;

/// <summary>
/// Keeps a player's loadouts on their account, so they follow them to any machine.
///
/// The game sends everything it has; the server merges that with what it has stored, using
/// the same rule the game uses (<see cref="LoadoutSync"/>), and sends back the result. Doing
/// it again changes nothing, so a retry after a dropped connection is harmless.
///
/// What is checked here is shape and size: nothing malformed or oversized reaches the
/// database. Whether a loadout is *legal* to play with is decided where it matters, by the
/// game server when it places the player, using the game's own rules and content.
/// </summary>
public sealed partial class LoadoutService(AppDbContext db, TimeProvider clock)
{
    public const int MaxIncomingLoadouts = 50, MaxIncomingDeleted = 200, MaxAttachments = 8, MaxNameLength = 24;
    /// <summary>A timestamp this far ahead of the server's clock is treated as "now": a device with a wrong clock must not win every argument.</summary>
    public const int MaxClockSkewSeconds = 300;

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    [GeneratedRegex("^[A-Za-z0-9_.:-]{1,64}$")]
    private static partial Regex IdPattern();

    public async Task<LoadoutCollection> GetAsync(Guid accountId)
    {
        List<LoadoutRow> rows = await RowsAsync(accountId);
        return ToCollection(rows, await db.LoadoutStates.FirstOrDefaultAsync(s => s.AccountId == accountId));
    }

    /// <summary>Merges what the game sent into what is stored and returns the result.</summary>
    public async Task<LoadoutCollection> SyncAsync(Guid accountId, LoadoutCollection incoming)
    {
        long now = clock.GetUtcNow().ToUnixTimeSeconds();

        await using var transaction = await db.Database.BeginTransactionAsync();
        // One sync at a time per account. Two devices syncing at the same instant take turns
        // instead of each overwriting half of the other's result.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))");

        List<LoadoutRow> rows = await RowsAsync(accountId);
        LoadoutStateRow? state = await db.LoadoutStates.FirstOrDefaultAsync(s => s.AccountId == accountId);
        LoadoutCollection merged = LoadoutSync.Merge(ToCollection(rows, state), incoming, now);

        var byId = rows.ToDictionary(r => r.LoadoutId);
        var kept = new HashSet<string>();

        foreach (LoadoutData loadout in merged.loadouts)
        {
            kept.Add(loadout.id);
            if (!byId.TryGetValue(loadout.id, out LoadoutRow? row))
            {
                row = new LoadoutRow { AccountId = accountId, LoadoutId = loadout.id, CreatedAt = clock.GetUtcNow() };
                db.Loadouts.Add(row);
            }
            else if (!row.Deleted && row.UpdatedAt == loadout.updatedAt)
            {
                continue;       // the stored copy won
            }
            row.Data = JsonSerializer.Serialize(loadout, Json);
            row.UpdatedAt = loadout.updatedAt;
            row.Deleted = false;
        }

        foreach (DeletedLoadout record in merged.deleted)
        {
            kept.Add(record.id);
            if (!byId.TryGetValue(record.id, out LoadoutRow? row))
            {
                row = new LoadoutRow { AccountId = accountId, LoadoutId = record.id, CreatedAt = clock.GetUtcNow() };
                db.Loadouts.Add(row);
            }
            row.Data = "{}";
            row.UpdatedAt = record.deletedAt;
            row.Deleted = true;
        }

        // Deletion records the merge has let go of.
        foreach (LoadoutRow row in rows)
        {
            if (!kept.Contains(row.LoadoutId)) db.Loadouts.Remove(row);
        }

        if (state == null)
        {
            state = new LoadoutStateRow { AccountId = accountId };
            db.LoadoutStates.Add(state);
        }
        state.EquippedId = merged.equippedId;
        state.EquippedUpdatedAt = merged.equippedUpdatedAt;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return merged;
    }

    /// <summary>
    /// Checks what the game sent and tidies it in place. Returns what is wrong with it; an
    /// empty list means it can be merged.
    /// </summary>
    public List<FieldError> Check(LoadoutCollection? incoming)
    {
        var errors = new List<FieldError>();
        if (incoming == null)
        {
            errors.Add("body", "Send your loadouts as a JSON object.");
            return errors;
        }

        long now = clock.GetUtcNow().ToUnixTimeSeconds();
        incoming.loadouts ??= [];
        incoming.deleted ??= [];
        incoming.equippedId = (incoming.equippedId ?? "").Trim();
        incoming.equippedUpdatedAt = ClampTime(incoming.equippedUpdatedAt, now);

        if (incoming.loadouts.Length > MaxIncomingLoadouts) errors.Add("loadouts", $"At most {MaxIncomingLoadouts} loadouts can be sent at once.");
        if (incoming.deleted.Length > MaxIncomingDeleted) errors.Add("deleted", $"At most {MaxIncomingDeleted} deletions can be sent at once.");
        if (incoming.equippedId.Length > 0 && !IdPattern().IsMatch(incoming.equippedId)) errors.Add("equippedId", "That is not a loadout id.");
        if (errors.Count > 0) return errors;

        for (int i = 0; i < incoming.loadouts.Length; i++)
        {
            LoadoutData? loadout = incoming.loadouts[i];
            string where = $"loadouts[{i}]";
            if (loadout == null || !IsId(loadout.id))
            {
                errors.Add(where + ".id", "Every loadout needs an id of letters, numbers, dashes or underscores.");
                continue;
            }

            loadout.name = CleanName(loadout.name);
            loadout.updatedAt = ClampTime(loadout.updatedAt, now);
            loadout.utility ??= "";
            loadout.perkSet ??= "";
            loadout.primary ??= new WeaponBuildData();
            loadout.secondary ??= new WeaponBuildData();
            loadout.melee ??= new WeaponBuildData();

            if (!IsIdOrEmpty(loadout.utility) || !IsIdOrEmpty(loadout.perkSet))
                errors.Add(where, "Utility and perk ids can use letters, numbers, dashes and underscores.");

            for (int slot = 0; slot < 3; slot++)
            {
                WeaponBuildData weapon = loadout.GetWeapon(slot);
                weapon.weaponId ??= "";
                weapon.attachments ??= [];
                if (!IsIdOrEmpty(weapon.weaponId)) errors.Add(where, "Weapon ids can use letters, numbers, dashes and underscores.");
                if (weapon.attachments.Length > MaxAttachments) errors.Add(where, $"A weapon can have at most {MaxAttachments} attachments.");
                else if (weapon.attachments.Any(a => !IsId(a))) errors.Add(where, "Attachment ids can use letters, numbers, dashes and underscores.");
            }
        }

        for (int i = 0; i < incoming.deleted.Length; i++)
        {
            DeletedLoadout? record = incoming.deleted[i];
            if (record == null || !IsId(record.id)) errors.Add($"deleted[{i}].id", "Every deletion needs the id of the loadout that was deleted.");
            else record.deletedAt = ClampTime(record.deletedAt, now);
        }
        return errors;
    }

    Task<List<LoadoutRow>> RowsAsync(Guid accountId) =>
        db.Loadouts.Where(l => l.AccountId == accountId).OrderBy(l => l.CreatedAt).ThenBy(l => l.LoadoutId).ToListAsync();

    static LoadoutCollection ToCollection(List<LoadoutRow> rows, LoadoutStateRow? state)
    {
        var loadouts = new List<LoadoutData>();
        var deleted = new List<DeletedLoadout>();
        foreach (LoadoutRow row in rows)
        {
            if (row.Deleted)
            {
                deleted.Add(new DeletedLoadout { id = row.LoadoutId, deletedAt = row.UpdatedAt });
                continue;
            }
            LoadoutData? data = JsonSerializer.Deserialize<LoadoutData>(row.Data, Json);
            if (data == null) continue;
            // The columns are the truth for these two; the JSON only carries the contents.
            data.id = row.LoadoutId;
            data.updatedAt = row.UpdatedAt;
            loadouts.Add(data);
        }
        deleted.Sort((a, b) => b.deletedAt.CompareTo(a.deletedAt));

        return new LoadoutCollection
        {
            equippedId = state?.EquippedId ?? "",
            equippedUpdatedAt = state?.EquippedUpdatedAt ?? 0,
            loadouts = loadouts.ToArray(),
            deleted = deleted.ToArray(),
        };
    }

    static bool IsId(string? id) => id != null && IdPattern().IsMatch(id);
    static bool IsIdOrEmpty(string? id) => string.IsNullOrEmpty(id) || IdPattern().IsMatch(id);

    static long ClampTime(long time, long now) => time < 0 ? 0 : time > now + MaxClockSkewSeconds ? now : time;

    /// <summary>The same tidying the game gives a name: no control characters, trimmed, not too long, never empty.</summary>
    static string CleanName(string? name)
    {
        string clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].TrimEnd();
        return clean.Length > 0 ? clean : "Loadout";
    }
}
