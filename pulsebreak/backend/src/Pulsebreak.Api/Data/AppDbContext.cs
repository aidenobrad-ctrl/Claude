using Microsoft.EntityFrameworkCore;

namespace Pulsebreak.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<LoadoutRow> Loadouts => Set<LoadoutRow>();
    public DbSet<LoadoutStateRow> LoadoutStates => Set<LoadoutStateRow>();
    public DbSet<SettingsRow> Settings => Set<SettingsRow>();

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
    }
}
