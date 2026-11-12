using Microsoft.EntityFrameworkCore;
using Pulsebreak.Api.Data;
using Pulsebreak.Api.Matchmaking;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Api.Progression;

public sealed record LevelDto(int Level, int Xp, long XpIntoLevel, long XpForNextLevel, int MaxLevel);
public sealed record WeaponProgressDto(string WeaponId, int Level, int Xp, long XpIntoLevel, long XpForNextLevel, int Kills);
public sealed record ChallengeDto(string Id, string Text, int Progress, int Goal, int RewardXp, bool Completed, bool Weekly, long EndsAt);
public sealed record RankDto(string Playlist, string Title, int Rating, int Matches, int Wins, int Losses, int Draws, string Name, string Tier,
    int Division, int PlacementLeft, int BestRating, string BestName);
public sealed record LastMatchDto(string MatchId, string Playlist, bool Ranked, int Outcome, int XpGained, int LevelBefore, int LevelAfter,
    int RatingBefore, int RatingAfter, string[] ChallengesCompleted, long EndedAt);
public sealed record ProgressionDto(LevelDto Level, WeaponProgressDto[] Weapons, ChallengeDto[] Challenges, RankDto[] Ranks, LastMatchDto LastMatch,
    SeasonDto Season, SeasonResultDto[] PastSeasons);
public sealed record HistoryEntryDto(string MatchId, string Playlist, bool Ranked, long EndedAt, int Outcome, bool Completed, int Kills, int Deaths,
    int Score, int XpGained, int RatingChange);
public sealed record LeaderboardEntryDto(int Place, string Name, int Rating, string RankName, int Matches, int Wins);

public enum RecordOutcome
{
    Recorded,
    AlreadyRecorded,
    UnknownMatch,
    WrongServer,
}

/// <summary>
/// Everything a player earns by playing. A game server reports each matchmade match once it
/// is over; from that report this awards match XP and weapon XP, moves daily and weekly
/// challenges along (paying out the ones finished), changes ratings in ranked, and keeps a
/// record of the match. Only matches the matchmaker set up count, and each counts once.
/// </summary>
public sealed class ProgressionService(AppDbContext db, TimeProvider clock, Seasons seasons, ILogger<ProgressionService> log)
{
    public async Task<RecordOutcome> RecordAsync(string serverId, MatchReport report, StoredMatch? match)
    {
        if (match == null) return RecordOutcome.UnknownMatch;
        if (match.ServerId != serverId) return RecordOutcome.WrongServer;
        if (await db.Matches.AnyAsync(m => m.MatchId == report.matchId)) return RecordOutcome.AlreadyRecorded;

        DateTimeOffset now = clock.GetUtcNow();
        Playlist? playlist = Playlists.Find(match.Assignment.playlist);
        bool ranked = match.Assignment.ranked && playlist is { Ranked: true };

        // Only players the matchmaker sent count, each once. In ranked, anyone sent who never
        // reported (they did not turn up, or left before the server knew them) has lost.
        var sent = match.Assignment.players.Select(p => p.accountId).ToHashSet();
        var reports = new Dictionary<Guid, PlayerReport>();
        foreach (PlayerReport player in report.players ?? [])
        {
            if (player == null || !sent.Contains(player.accountId) || !Guid.TryParse(player.accountId, out Guid id)) continue;
            reports.TryAdd(id, player);
        }
        if (ranked)
        {
            foreach (AssignedPlayer assigned in match.Assignment.players)
            {
                if (!Guid.TryParse(assigned.accountId, out Guid id) || reports.ContainsKey(id)) continue;
                reports[id] = new PlayerReport { accountId = assigned.accountId, team = assigned.team, outcome = MatchOutcome.Lost, completed = false };
            }
        }

        var accountIds = reports.Keys.ToList();
        Dictionary<Guid, Profile> profiles = await db.Profiles.Where(p => accountIds.Contains(p.AccountId)).ToDictionaryAsync(p => p.AccountId);
        Dictionary<Guid, RatingRow> ratings = ranked
            ? await db.Ratings.Where(r => r.Playlist == playlist!.Id && accountIds.Contains(r.AccountId)).ToDictionaryAsync(r => r.AccountId)
            : new Dictionary<Guid, RatingRow>();
        // Ratings from an earlier season are carried into this one before the match counts.
        foreach (RatingRow row in ratings.Values) AddResult(seasons.Roll(row));

        db.Matches.Add(new MatchRow
        {
            MatchId = report.matchId, Playlist = match.Assignment.playlist, Ranked = ranked, ServerId = serverId, EndedAt = now,
            DurationSeconds = Math.Max(0, report.durationSeconds),
        });

        // Each side's average rating before anything changes, for judging every result against.
        var teamRatings = new Dictionary<int, double>();
        if (ranked)
        {
            foreach (IGrouping<int, KeyValuePair<Guid, PlayerReport>> side in reports.GroupBy(r => r.Value.team))
                teamRatings[side.Key] = side.Average(r => ratings.TryGetValue(r.Key, out RatingRow? row) ? row.Rating : Ranks.StartRating);
        }

        foreach ((Guid accountId, PlayerReport player) in reports)
        {
            if (!profiles.TryGetValue(accountId, out Profile? profile)) continue;     // a deleted account

            int levelBefore = Levels.LevelFor(profile.Xp);
            int xp = MatchXp.For(player, ranked);
            await AddWeaponXpAsync(accountId, player);
            (int bonus, List<string> finished) = await AdvanceChallengesAsync(accountId, player, ranked, now);
            xp += bonus;
            profile.Xp = (int)Math.Min(int.MaxValue, (long)profile.Xp + xp);
            profile.Level = Levels.LevelFor(profile.Xp);

            int? ratingBefore = null, ratingAfter = null;
            if (ranked)
            {
                if (!ratings.TryGetValue(accountId, out RatingRow? row))
                {
                    row = new RatingRow { AccountId = accountId, Playlist = playlist!.Id, Season = seasons.Current, BestRating = Ranks.StartRating };
                    db.Ratings.Add(row);
                    ratings[accountId] = row;
                }
                double opponents = teamRatings.Where(t => t.Key != player.team).Select(t => t.Value).DefaultIfEmpty(Ranks.StartRating).Average();
                ratingBefore = row.Rating;
                row.Rating = Math.Max(0, row.Rating + Ranks.Change(row.Rating, (int)Math.Round(opponents), Ranks.Score(player.outcome), row.Matches));
                row.Matches++;
                if (player.outcome == MatchOutcome.Won) row.Wins++;
                else if (player.outcome == MatchOutcome.Draw) row.Draws++;
                else row.Losses++;
                row.UpdatedAt = now;
                row.BestRating = Math.Max(row.BestRating, row.Rating);
                ratingAfter = row.Rating;
            }

            db.MatchPlayers.Add(new MatchPlayerRow
            {
                MatchId = report.matchId, AccountId = accountId, Team = player.team, Outcome = (int)player.outcome, Completed = player.completed,
                Kills = player.kills, Deaths = player.deaths, Headshots = player.headshots, Objective = player.objective,
                RoundsWon = player.roundsWon, Score = player.score, XpGained = xp, LevelBefore = levelBefore, LevelAfter = profile.Level,
                RatingBefore = ratingBefore, RatingAfter = ratingAfter, ChallengesCompleted = string.Join(",", finished), EndedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException error) when (Api.IsUniqueViolation(error))
        {
            // The same match reported twice at the same moment: the other report won.
            return RecordOutcome.AlreadyRecorded;
        }
        log.LogInformation("Recorded match {Match} ({Playlist}{Ranked}) for {Count} players.", report.matchId, match.Assignment.playlist,
            ranked ? ", ranked" : "", reports.Count);
        return RecordOutcome.Recorded;
    }

    async Task AddWeaponXpAsync(Guid accountId, PlayerReport player)
    {
        foreach (WeaponReport weapon in player.weapons ?? [])
        {
            if (weapon == null || string.IsNullOrEmpty(weapon.weaponId) || weapon.weaponId.Length > 32 || weapon.kills <= 0) continue;
            WeaponProgressRow? row = await db.WeaponProgress.FindAsync(accountId, weapon.weaponId);
            if (row == null)
            {
                row = new WeaponProgressRow { AccountId = accountId, WeaponId = weapon.weaponId };
                db.WeaponProgress.Add(row);
            }
            row.Xp = (int)Math.Min(int.MaxValue, (long)row.Xp + MatchXp.ForWeapon(weapon));
            row.Kills += Math.Max(0, weapon.kills);
        }
    }

    /// <summary>Moves today's and this week's challenges along. Returns the XP for any finished, and their ids.</summary>
    async Task<(int Xp, List<string> Finished)> AdvanceChallengesAsync(Guid accountId, PlayerReport player, bool ranked, DateTimeOffset now)
    {
        int xp = 0;
        var finished = new List<string>();
        foreach ((string period, bool weekly) in new[] { (Challenges.DayKey(now), false), (Challenges.WeekKey(now), true) })
        {
            foreach (Challenge challenge in Challenges.For(accountId, period, weekly))
            {
                int gained = challenge.ProgressFrom(player, ranked);
                if (gained <= 0) continue;
                ChallengeRow? row = await db.Challenges.FindAsync(accountId, period, challenge.Id);
                if (row == null)
                {
                    row = new ChallengeRow { AccountId = accountId, Period = period, ChallengeId = challenge.Id };
                    db.Challenges.Add(row);
                }
                if (row.Completed) continue;
                row.Progress = Math.Min(challenge.Goal, row.Progress + gained);
                if (row.Progress < challenge.Goal) continue;
                row.Completed = true;
                xp += challenge.RewardXp;
                finished.Add(challenge.Id);
            }
        }
        return (xp, finished);
    }

    // ------------------------------------------------------------------ reading

    public async Task<ProgressionDto?> GetAsync(Guid accountId)
    {
        Profile? profile = await db.Profiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
        if (profile == null) return null;
        DateTimeOffset now = clock.GetUtcNow();

        int level = Levels.LevelFor(profile.Xp, out long into, out long needed);
        var weapons = (await db.WeaponProgress.Where(w => w.AccountId == accountId).ToListAsync())
            .Select(w =>
            {
                int weaponLevel = Levels.WeaponLevelFor(w.Xp, out long weaponInto, out long weaponNeeded);
                return new WeaponProgressDto(w.WeaponId, weaponLevel, w.Xp, weaponInto, weaponNeeded, w.Kills);
            })
            .OrderByDescending(w => w.Xp).ToArray();

        var challenges = new List<ChallengeDto>();
        foreach ((string period, bool weekly, DateTimeOffset ends) in new[]
                 {
                     (Challenges.DayKey(now), false, Challenges.DayEnds(now)), (Challenges.WeekKey(now), true, Challenges.WeekEnds(now)),
                 })
        {
            var rows = await db.Challenges.Where(c => c.AccountId == accountId && c.Period == period).ToDictionaryAsync(c => c.ChallengeId);
            foreach (Challenge challenge in Challenges.For(accountId, period, weekly))
            {
                rows.TryGetValue(challenge.Id, out ChallengeRow? row);
                challenges.Add(new ChallengeDto(challenge.Id, challenge.Text, row?.Progress ?? 0, challenge.Goal, challenge.RewardXp,
                    row?.Completed ?? false, weekly, ends.ToUnixTimeSeconds()));
            }
        }

        var ranks = new List<RankDto>();
        var rated = await db.Ratings.Where(r => r.AccountId == accountId).ToDictionaryAsync(r => r.Playlist);
        bool rolled = false;
        foreach (RatingRow row in rated.Values)
        {
            if (row.Season >= seasons.Current) continue;
            AddResult(seasons.Roll(row));
            rolled = true;
        }
        if (rolled) await db.SaveChangesAsync();
        foreach (Playlist playlist in Playlists.All.Where(p => p.Ranked))
        {
            rated.TryGetValue(playlist.Id, out RatingRow? row);
            int rating = row?.Rating ?? Ranks.StartRating, matches = row?.Matches ?? 0, best = Math.Max(row?.BestRating ?? Ranks.StartRating, rating);
            ranks.Add(new RankDto(playlist.Id, playlist.Title, rating, matches, row?.Wins ?? 0, row?.Losses ?? 0, row?.Draws ?? 0,
                Ranks.Name(rating, matches), Ranks.TierFor(rating).ToString(), Ranks.DivisionFor(rating),
                Math.Max(0, Ranks.PlacementMatches - matches), best, matches >= Ranks.PlacementMatches ? Ranks.Name(best, matches) : Ranks.Name(rating, matches)));
        }

        var past = (await db.SeasonResults.Where(r => r.AccountId == accountId).OrderByDescending(r => r.Season).Take(20).ToListAsync())
            .Select(r => new SeasonResultDto(r.Season, r.Playlist, Playlists.Find(r.Playlist)?.Title ?? r.Playlist, r.FinalRating, r.BestRating,
                Ranks.Name(r.BestRating, r.Matches), r.Matches, r.Wins)).ToArray();

        MatchPlayerRow? last = await db.MatchPlayers.Where(p => p.AccountId == accountId).OrderByDescending(p => p.EndedAt).FirstOrDefaultAsync();
        MatchRow? lastMatch = last != null ? await db.Matches.FindAsync(last.MatchId) : null;
        LastMatchDto lastDto = last == null || lastMatch == null
            ? new LastMatchDto("", "", false, 0, 0, level, level, 0, 0, [], 0)
            : new LastMatchDto(last.MatchId, lastMatch.Playlist, lastMatch.Ranked, last.Outcome, last.XpGained, last.LevelBefore, last.LevelAfter,
                last.RatingBefore ?? 0, last.RatingAfter ?? 0,
                last.ChallengesCompleted.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => Challenges.Find(id)?.Text ?? id).ToArray(),
                last.EndedAt.ToUnixTimeSeconds());

        return new ProgressionDto(new LevelDto(level, profile.Xp, into, needed, Levels.MaxLevel), weapons, challenges.ToArray(), ranks.ToArray(), lastDto,
            seasons.Now(), past);
    }

    public async Task<HistoryEntryDto[]> HistoryAsync(Guid accountId, int limit)
    {
        var rows = await (from player in db.MatchPlayers
                          join match in db.Matches on player.MatchId equals match.MatchId
                          where player.AccountId == accountId
                          orderby player.EndedAt descending
                          select new { player, match }).Take(Math.Clamp(limit, 1, 50)).ToListAsync();
        return rows.Select(r => new HistoryEntryDto(r.match.MatchId, r.match.Playlist, r.match.Ranked, r.player.EndedAt.ToUnixTimeSeconds(),
            r.player.Outcome, r.player.Completed, r.player.Kills, r.player.Deaths, r.player.Score, r.player.XpGained,
            (r.player.RatingAfter ?? 0) - (r.player.RatingBefore ?? 0))).ToArray();
    }

    /// <summary>The best players in a ranked playlist this season who have finished their placement matches.</summary>
    public async Task<LeaderboardEntryDto[]> LeaderboardAsync(string playlist, int limit)
    {
        int season = seasons.Current;
        var rows = await (from rating in db.Ratings
                          join account in db.Accounts on rating.AccountId equals account.Id
                          where rating.Playlist == playlist && rating.Season == season && rating.Matches >= Ranks.PlacementMatches && !account.Disabled
                          orderby rating.Rating descending, rating.Matches descending
                          select new { account.Username, rating.Rating, rating.Matches, rating.Wins }).Take(Math.Clamp(limit, 1, 100)).ToListAsync();
        return rows.Select((r, i) => new LeaderboardEntryDto(i + 1, r.Username, r.Rating, Ranks.Name(r.Rating, r.Matches), r.Matches, r.Wins)).ToArray();
    }

    /// <summary>What each player in a match has unlocked, for the game server to hold their loadouts to. Guests are level 1.</summary>
    public async Task<AssignedPlayer[]> UnlocksAsync(IEnumerable<(string Key, int Team)> players)
    {
        var list = players.ToList();
        var ids = list.Select(p => Guid.TryParse(p.Key, out Guid id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        Dictionary<Guid, int> xp = await db.Profiles.Where(p => ids.Contains(p.AccountId)).ToDictionaryAsync(p => p.AccountId, p => p.Xp);
        var weapons = (await db.WeaponProgress.Where(w => ids.Contains(w.AccountId)).ToListAsync()).GroupBy(w => w.AccountId)
            .ToDictionary(g => g.Key, g => g.Select(w => new WeaponLevel { weaponId = w.WeaponId, level = Levels.WeaponLevelFor(w.Xp) })
                .Where(w => w.level > 1).ToArray());

        return list.Select(p =>
        {
            var assigned = new AssignedPlayer { accountId = p.Key, team = p.Team };
            if (!Guid.TryParse(p.Key, out Guid id)) return assigned;
            assigned.level = xp.TryGetValue(id, out int total) ? Levels.LevelFor(total) : 1;
            assigned.weapons = weapons.TryGetValue(id, out WeaponLevel[]? levels) ? levels : [];
            return assigned;
        }).ToArray();
    }

    /// <summary>A player's rating in a playlist this season, for matchmaking.</summary>
    public async Task<int> RatingAsync(Guid accountId, string playlist) =>
        seasons.RatingNow(await db.Ratings.FirstOrDefaultAsync(r => r.AccountId == accountId && r.Playlist == playlist));

    void AddResult(SeasonResultRow? result)
    {
        if (result != null) db.SeasonResults.Add(result);
    }
}
