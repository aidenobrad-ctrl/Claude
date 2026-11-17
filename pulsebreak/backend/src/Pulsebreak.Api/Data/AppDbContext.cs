using Microsoft.EntityFrameworkCore;

namespace Pulsebreak.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<LoadoutRow> Loadouts => Set<LoadoutRow>();
    public DbSet<LoadoutStateRow> LoadoutStates => Set<LoadoutStateRow>();
    public DbSet<SettingsRow> Settings => Set<SettingsRow>();
    public DbSet<WeaponProgressRow> WeaponProgress => Set<WeaponProgressRow>();
    public DbSet<RatingRow> Ratings => Set<RatingRow>();
    public DbSet<ChallengeRow> Challenges => Set<ChallengeRow>();
    public DbSet<MatchRow> Matches => Set<MatchRow>();
    public DbSet<MatchPlayerRow> MatchPlayers => Set<MatchPlayerRow>();
    public DbSet<SeasonResultRow> SeasonResults => Set<SeasonResultRow>();
    public DbSet<FriendshipRow> Friendships => Set<FriendshipRow>();
    public DbSet<BlockRow> Blocks => Set<BlockRow>();
    public DbSet<ReportRow> Reports => Set<ReportRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Email).HasMaxLength(254);
            account.Property(a => a.EmailNormalized).HasMaxLength(254);
            account.Property(a => a.Username).HasMaxLength(20);
            account.Property(a => a.UsernameNormalized).HasMaxLength(20);
            account.Property(a => a.PasswordHash).HasMaxLength(256);
            // The database, not the application, has the last word on uniqueness: two
            // sign-ups racing for the same name cannot both win.
            account.HasIndex(a => a.EmailNormalized).IsUnique();
            account.HasIndex(a => a.UsernameNormalized).IsUnique();
            account.HasOne(a => a.Profile).WithOne().HasForeignKey<Profile>(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Profile>(profile =>
        {
            profile.ToTable("profiles");
            profile.HasKey(p => p.AccountId);
        });

        model.Entity<LoadoutRow>(loadout =>
        {
            loadout.ToTable("loadouts");
            loadout.HasKey(l => new { l.AccountId, l.LoadoutId });
            loadout.Property(l => l.LoadoutId).HasMaxLength(64);
            loadout.Property(l => l.Data).HasColumnType("jsonb");
            loadout.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<LoadoutStateRow>(state =>
        {
            state.ToTable("loadout_states");
            state.HasKey(s => s.AccountId);
            state.Property(s => s.EquippedId).HasMaxLength(64);
            state.HasOne<Account>().WithOne().HasForeignKey<LoadoutStateRow>(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SettingsRow>(settings =>
        {
            settings.ToTable("player_settings");
            settings.HasKey(s => s.AccountId);
            settings.Property(s => s.Data).HasColumnType("jsonb");
            settings.HasOne<Account>().WithOne().HasForeignKey<SettingsRow>(s => s.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<WeaponProgressRow>(weapon =>
        {
            weapon.ToTable("weapon_progress");
            weapon.HasKey(w => new { w.AccountId, w.WeaponId });
            weapon.Property(w => w.WeaponId).HasMaxLength(32);
            weapon.HasOne<Account>().WithMany().HasForeignKey(w => w.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<RatingRow>(rating =>
        {
            rating.ToTable("ratings");
            rating.HasKey(r => new { r.AccountId, r.Playlist });
            rating.Property(r => r.Playlist).HasMaxLength(32);
            // For leaderboards: the best in a playlist this season, quickly.
            rating.HasIndex(r => new { r.Playlist, r.Season, r.Rating });
            rating.Property(r => r.Season).HasDefaultValue(1);
            rating.Property(r => r.BestRating).HasDefaultValue(1000);
            rating.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ChallengeRow>(challenge =>
        {
            challenge.ToTable("challenges");
            challenge.HasKey(c => new { c.AccountId, c.Period, c.ChallengeId });
            challenge.Property(c => c.Period).HasMaxLength(16);
            challenge.Property(c => c.ChallengeId).HasMaxLength(32);
            challenge.HasOne<Account>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<MatchRow>(match =>
        {
            match.ToTable("matches");
            match.HasKey(m => m.MatchId);
            match.Property(m => m.MatchId).HasMaxLength(32);
            match.Property(m => m.Playlist).HasMaxLength(32);
            match.Property(m => m.ServerId).HasMaxLength(32);
        });

        model.Entity<MatchPlayerRow>(player =>
        {
            player.ToTable("match_players");
            player.HasKey(p => new { p.MatchId, p.AccountId });
            player.Property(p => p.MatchId).HasMaxLength(32);
            player.Property(p => p.ChallengesCompleted).HasMaxLength(256);
            // A player's history, newest first.
            player.HasIndex(p => new { p.AccountId, p.EndedAt });
            player.HasOne<MatchRow>().WithMany().HasForeignKey(p => p.MatchId).OnDelete(DeleteBehavior.Cascade);
            player.HasOne<Account>().WithMany().HasForeignKey(p => p.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<SeasonResultRow>(result =>
        {
            result.ToTable("season_results");
            result.HasKey(r => new { r.AccountId, r.Playlist, r.Season });
            result.Property(r => r.Playlist).HasMaxLength(32);
            result.HasOne<Account>().WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<FriendshipRow>(friend =>
        {
            friend.ToTable("friendships");
            friend.HasKey(f => new { f.RequesterId, f.AddresseeId });
            // Requests waiting for a player, and their friends, from either side.
            friend.HasIndex(f => f.AddresseeId);
            friend.HasOne<Account>().WithMany().HasForeignKey(f => f.RequesterId).OnDelete(DeleteBehavior.Cascade);
            friend.HasOne<Account>().WithMany().HasForeignKey(f => f.AddresseeId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<BlockRow>(block =>
        {
            block.ToTable("blocks");
            block.HasKey(b => new { b.OwnerId, b.BlockedId });
            block.HasIndex(b => b.BlockedId);
            block.HasOne<Account>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Cascade);
            block.HasOne<Account>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ReportRow>(report =>
        {
            report.ToTable("reports");
            report.HasKey(r => r.Id);
            report.Property(r => r.Reason).HasMaxLength(32);
            report.Property(r => r.Details).HasMaxLength(500);
            report.Property(r => r.MatchId).HasMaxLength(32);
            report.Property(r => r.Chat).HasMaxLength(2000);
            report.Property(r => r.Status).HasMaxLength(16);
            report.HasIndex(r => new { r.Status, r.CreatedAt });
            report.HasIndex(r => new { r.ReporterId, r.CreatedAt });
            report.HasIndex(r => r.ReportedId);
            report.HasOne<Account>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.Cascade);
            report.HasOne<Account>().WithMany().HasForeignKey(r => r.ReportedId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
