using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Loadouts
{
    /// <summary>
    /// Where a player's loadouts are kept. Today that is a file on this machine
    /// (<see cref="LocalFileLoadoutStore"/>); once accounts exist, a store that talks to the
    /// backend takes its place and nothing else has to change.
    /// </summary>
    public interface ILoadoutStore
    {
        /// <summary>What is saved, or null if nothing has been saved yet or it cannot be read.</summary>
        LoadoutCollection Load();

        void Save(LoadoutCollection collection);
    }
}
