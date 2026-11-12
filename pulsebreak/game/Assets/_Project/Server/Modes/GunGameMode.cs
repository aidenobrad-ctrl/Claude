using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Server.Modes
{
    /// <summary>
    /// Gun Game: everyone for themselves, everyone on the same ladder of weapons. A kill
    /// moves you up to the next weapon on the spot; a kill with the last one (the knife)
    /// wins. Being knifed, or dying by your own hand, moves you down one.
    /// </summary>
    public sealed class GunGameMode : RespawnMode
    {
        readonly bool[] _rearm = new bool[GameServer.MaxPlayers];

        public GunGameMode(GameServer server, uint seed) : base(server, seed) { }

        static int Top => GunGameLadder.Length - 1;

        protected override SpawnOptions OptionsFor(ServerPlayer player)
        {
            LoadoutData loadout = MatchLoadouts.GunGame(player.GunLevel, Server.Catalog, out byte mask, out int active);
            _rearm[player.Id] = false;
            return new SpawnOptions { UnlimitedAmmo = true, SlotMask = mask, ActiveSlot = active, Loadout = loadout };
        }

        protected override void Scored(ServerPlayer victim, ServerPlayer killer, string weaponId, bool headshot)
        {
            victim.RespawnTick = Tick + Ticks(Settings.respawnSeconds);

            if (killer == null || weaponId == GunGameLadder.Knife) Demote(victim);
            if (killer == null) return;

            if (killer.GunLevel >= Top)
            {
                killer.Score = killer.GunLevel + 1;
                EnterMatchEnd(killer.Id);
                return;
            }
            killer.GunLevel++;
            killer.Score = killer.GunLevel;
            _rearm[killer.Id] = true;
        }

        void Demote(ServerPlayer player)
        {
            if (player.GunLevel == 0) return;
            player.GunLevel--;
            player.Score = player.GunLevel;
        }

        protected override void UpdateLive()
        {
            base.UpdateLive();
            if (_state.Phase != MatchPhase.Live) return;

            // Done here rather than the moment the kill lands, so a shotgun's other pellets
            // and anything else still in flight this tick finish with the weapon that fired them.
            foreach (ServerPlayer player in Players)
            {
                if (player == null || !_rearm[player.Id]) continue;
                _rearm[player.Id] = false;
                if (!player.Alive) continue;      // the next spawn brings the new weapon anyway
                LoadoutData loadout = MatchLoadouts.GunGame(player.GunLevel, Server.Catalog, out byte mask, out int active);
                Server.Rearm(player, loadout, mask, active);
            }
        }
    }
}
