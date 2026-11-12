using System.Collections;
using System.Collections.Generic;

namespace Pulsebreak.Server
{
    /// <summary>
    /// Everyone in the match, by id; empty places are null. A foreach over it allocates
    /// nothing, which matters in code that runs every tick for every bot.
    /// </summary>
    public readonly struct PlayerList : IReadOnlyList<ServerPlayer>
    {
        readonly ServerPlayer[] _players;

        public PlayerList(ServerPlayer[] players)
        {
            _players = players;
        }

        public int Count => _players.Length;
        public ServerPlayer this[int index] => _players[index];

        public Enumerator GetEnumerator() => new Enumerator(_players);
        IEnumerator<ServerPlayer> IEnumerable<ServerPlayer>.GetEnumerator() => ((IEnumerable<ServerPlayer>)_players).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _players.GetEnumerator();

        public struct Enumerator
        {
            readonly ServerPlayer[] _players;
            int _index;

            public Enumerator(ServerPlayer[] players)
            {
                _players = players;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _players.Length;
            public ServerPlayer Current => _players[_index];
        }
    }
}
