using System;
using System.Collections.Generic;

// This file is also compiled into the backend (backend/src/Pulsebreak.Api), so both ends
// merge loadouts by exactly the same rule. Keep it free of UnityEngine.
#nullable disable

namespace Pulsebreak.Shared.Loadouts
{
    /// <summary>
    /// How two copies of a player's loadouts become one. The copies may come from two
    /// devices that were each changed while the other was out of reach.
    ///
    /// The rule is "the newest change wins", decided separately for every loadout and for
    /// the choice of which one is equipped. Deleting a loadout is a change like any other:
    /// it leaves a small record behind, so an older copy of that loadout on another device
    /// loses to the deletion instead of bringing the loadout back.
    ///
    /// Merging is safe to repeat and gives the same result whichever copy is given first,
    /// except that on an exact tie the first copy wins.
    /// </summary>
    public static class LoadoutSync
    {
        /// <summary>The most custom loadouts one player keeps.</summary>
        public const int MaxLoadouts = 10;
        /// <summary>How many deletions are remembered. Older ones are forgotten.</summary>
        public const int MaxDeleted = 64;

        /// <param name="first">Wins ties. The server passes what it has stored here.</param>
        /// <param name="now">Seconds since 1970 (UTC). Stamps any loadout dropped for being over the limit.</param>
        public static LoadoutCollection Merge(LoadoutCollection first, LoadoutCollection second, long now)
        {
            first = first ?? new LoadoutCollection();
            second = second ?? new LoadoutCollection();

            // For every id, the newest thing known about it: a loadout, or its deletion.
            var order = new List<string>();
            var live = new Dictionary<string, LoadoutData>();
            var gone = new Dictionary<string, long>();

            Take(first, order, live, gone);
            Take(second, order, live, gone);

            // Over the limit: the least recently changed go. They are recorded as deleted so
            // every device ends up with the same ten.
            var kept = new List<LoadoutData>();
            foreach (string id in order)
            {
                if (live.TryGetValue(id, out LoadoutData data)) kept.Add(data);
            }
            while (kept.Count > MaxLoadouts)
            {
                int oldest = 0;
                for (int i = 1; i < kept.Count; i++)
                {
                    if (kept[i].updatedAt < kept[oldest].updatedAt) oldest = i;
                }
                gone[kept[oldest].id] = Math.Max(now, kept[oldest].updatedAt + 1);
                kept.RemoveAt(oldest);
            }

            var deleted = new List<DeletedLoadout>();
            foreach (KeyValuePair<string, long> entry in gone)
                deleted.Add(new DeletedLoadout { id = entry.Key, deletedAt = entry.Value });
            deleted.Sort((a, b) => b.deletedAt != a.deletedAt ? b.deletedAt.CompareTo(a.deletedAt) : string.CompareOrdinal(a.id, b.id));
            if (deleted.Count > MaxDeleted) deleted.RemoveRange(MaxDeleted, deleted.Count - MaxDeleted);

            bool secondEquip = second.equippedUpdatedAt > first.equippedUpdatedAt;
            return new LoadoutCollection
            {
                version = LoadoutCollection.CurrentVersion,
                equippedId = (secondEquip ? second.equippedId : first.equippedId) ?? "",
                equippedUpdatedAt = secondEquip ? second.equippedUpdatedAt : first.equippedUpdatedAt,
                loadouts = kept.ToArray(),
                deleted = deleted.ToArray(),
            };
        }

        static void Take(LoadoutCollection from, List<string> order, Dictionary<string, LoadoutData> live, Dictionary<string, long> gone)
        {
            if (from.loadouts != null)
            {
                foreach (LoadoutData data in from.loadouts)
                {
                    if (data == null || string.IsNullOrEmpty(data.id)) continue;
                    long known = Newest(data.id, live, gone, out bool seen);
                    if (seen && data.updatedAt <= known) continue;      // what we already have is as new or newer

                    if (!order.Contains(data.id)) order.Add(data.id);
                    live[data.id] = data;
                    gone.Remove(data.id);
                }
            }

            if (from.deleted != null)
            {
                foreach (DeletedLoadout record in from.deleted)
                {
                    if (record == null || string.IsNullOrEmpty(record.id)) continue;
                    long known = Newest(record.id, live, gone, out bool seen);
                    // A deletion at the very same moment as an edit wins: the player's last word was "delete".
                    bool overLive = live.ContainsKey(record.id) && record.deletedAt >= known;
                    if (seen && !overLive && record.deletedAt <= known) continue;

                    live.Remove(record.id);
                    gone[record.id] = record.deletedAt;
                }
            }
        }

        static long Newest(string id, Dictionary<string, LoadoutData> live, Dictionary<string, long> gone, out bool seen)
        {
            if (live.TryGetValue(id, out LoadoutData data))
            {
                seen = true;
                return data.updatedAt;
            }
            seen = gone.TryGetValue(id, out long deletedAt);
            return deletedAt;
        }
    }
}
