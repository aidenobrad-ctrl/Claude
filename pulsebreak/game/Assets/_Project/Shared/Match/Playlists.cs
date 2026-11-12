using System;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api): its matchmaker
// fills matches from these playlists, so it and the game agree on what each mode needs.
// Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Match
{
    public enum GameModeKind : byte
    {
        /// <summary>One against one, in rounds, no respawns. First to a number of rounds.</summary>
        Duel,
        /// <summary>Team against team, respawning. First team to the score limit, or ahead when time runs out.</summary>
        TeamDeathmatch,
        /// <summary>Everyone for themselves, respawning. First player to the score limit.</summary>
        FreeForAll,
        /// <summary>Team against team, in rounds, no respawns within a round.</summary>
        Elimination,
        /// <summary>Teams capture and hold three zones; every zone held earns points.</summary>
        Domination,
        /// <summary>One zone at a time, moving every minute; the team holding it earns points.</summary>
        Hardpoint,
        /// <summary>Everyone for themselves. Each kill moves you to the next weapon; a kill with the last one wins.</summary>
        GunGame,
    }

    /// <summary>What matchmaking offers for one mode: how many people a match is for, and the fewest it will start with.</summary>
    public sealed class Playlist
    {
        /// <summary>The id used in requests to the backend, such as "tdm".</summary>
        public string Id;
        public GameModeKind Mode;
        public string Title;
        /// <summary>People in a full match.</summary>
        public int Players;
        /// <summary>Per team, in team modes. 0 in modes without teams.</summary>
        public int TeamSize;
        /// <summary>
        /// The fewest people a match starts with once the oldest in the queue has waited long
        /// enough. Bots take the empty places, so for most modes one person is enough.
        /// </summary>
        public int MinPlayers;
    }

    /// <summary>Every mode matchmaking offers. Ranked play is for later, once accounts are switched on.</summary>
    public static class Playlists
    {
        public static readonly Playlist[] All =
        {
            new Playlist { Id = "duel", Mode = GameModeKind.Duel, Title = "Duel", Players = 2, TeamSize = 1, MinPlayers = 2 },
            new Playlist { Id = "tdm", Mode = GameModeKind.TeamDeathmatch, Title = "Team Deathmatch", Players = 10, TeamSize = 5, MinPlayers = 1 },
            new Playlist { Id = "ffa", Mode = GameModeKind.FreeForAll, Title = "Free-for-All", Players = 8, TeamSize = 0, MinPlayers = 1 },
            new Playlist { Id = "elimination", Mode = GameModeKind.Elimination, Title = "Elimination", Players = 6, TeamSize = 3, MinPlayers = 1 },
            new Playlist { Id = "domination", Mode = GameModeKind.Domination, Title = "Domination", Players = 10, TeamSize = 5, MinPlayers = 1 },
            new Playlist { Id = "hardpoint", Mode = GameModeKind.Hardpoint, Title = "Hardpoint", Players = 10, TeamSize = 5, MinPlayers = 1 },
            new Playlist { Id = "gungame", Mode = GameModeKind.GunGame, Title = "Gun Game", Players = 8, TeamSize = 0, MinPlayers = 1 },
        };

        /// <summary>The playlist with this id, or null.</summary>
        public static Playlist Find(string id)
        {
            foreach (Playlist playlist in All)
            {
                if (string.Equals(playlist.Id, id, StringComparison.Ordinal)) return playlist;
            }
            return null;
        }

        /// <summary>The playlist for a mode. Every mode has one.</summary>
        public static Playlist For(GameModeKind mode)
        {
            foreach (Playlist playlist in All)
            {
                if (playlist.Mode == mode) return playlist;
            }
            return All[0];
        }
    }
}
