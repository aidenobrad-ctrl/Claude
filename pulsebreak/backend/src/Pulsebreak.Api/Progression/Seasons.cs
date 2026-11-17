using Microsoft.Extensions.Options;
using Pulsebreak.Api.Data;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Api.Progression;

public sealed record SeasonDto(int Number, long StartedAt, long EndsAt);

public sealed record SeasonResultDto(int Season, string Playlist, string Title, int FinalRating, int BestRating, string RankName, int Matches, int Wins);

/// <summary>
/// Ranked seasons. They follow one another from a fixed first start, each the same length.
/// At the turn of a season a player's rating moves part of the way back to the start rating
/// (a soft reset), their placement matches start again, and where they finished is kept.
///
/// Nothing runs at the turn itself: each rating is rolled over the next time it is used.
/// </summary>
public sealed class Seasons(IOptions<SeasonOptions> options, TimeProvider clock)
{
    SeasonOptions Options => options.Value;
    TimeSpan Length => TimeSpan.FromDays(Math.Max(1, Options.LengthDays));

    public int Current => NumberAt(clock.GetUtcNow());

    public int NumberAt(DateTimeOffset when) =>
        when <= Options.FirstStart ? 1 : (int)((when - Options.FirstStart).Ticks / Length.Ticks) + 1;

    public DateTimeOffset StartOf(int season) => Options.FirstStart + Length * (Math.Max(1, season) - 1);

    public SeasonDto Now()
    {
        int season = Current;
        return new SeasonDto(season, StartOf(season).ToUnixTimeSeconds(), StartOf(season + 1).ToUnixTimeSeconds());
    }

    /// <summary>A rating carried into a new season: its distance from the start rating shrunk by the configured share.</summary>
    public int Reset(int rating) => Ranks.StartRating + (int)Math.Round((rating - Ranks.StartRating) * Math.Clamp(Options.Keep, 0, 1));

    /// <summary>What a rating is this season, without changing anything: soft-reset if it belongs to an earlier one.</summary>
    public int RatingNow(RatingRow? row) => row == null ? Ranks.StartRating : row.Season < Current ? Reset(row.Rating) : row.Rating;

    /// <summary>
    /// Brings a rating into the current season, if it is from an earlier one. Returns the record
    /// of where it finished the season it leaves (null if it played no matches then, or was current).
    /// </summary>
    public SeasonResultRow? Roll(RatingRow row)
    {
        int current = Current;
        if (row.Season >= current) return null;
        SeasonResultRow? result = row.Matches == 0 ? null : new SeasonResultRow
        {
            AccountId = row.AccountId, Playlist = row.Playlist, Season = row.Season, FinalRating = row.Rating,
            BestRating = Math.Max(row.BestRating, row.Rating), Matches = row.Matches, Wins = row.Wins,
        };
        row.Rating = Reset(row.Rating);
        row.BestRating = row.Rating;
        row.Matches = row.Wins = row.Losses = row.Draws = 0;
        row.Season = current;
        return result;
    }
}
