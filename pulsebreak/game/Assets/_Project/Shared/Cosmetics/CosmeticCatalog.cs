using System.Collections.Generic;
using UnityEngine;

namespace Pulsebreak.Shared.Cosmetics
{
    /// <summary>The parts of a look a player chooses. Each has its own list of items.</summary>
    public enum CosmeticSlot : byte
    {
        SuitColor,
        TrimColor,
        Pattern,
        Headgear,
        Visor,
        Back,
        WeaponSkin,
    }

    public enum PatternKind : byte
    {
        Plain,
        Stripes,
        Chevrons,
        Honeycomb,
        Splinter,
        Pixel,
        Dots,
        Weave,
        Tiger,
        Circuit,
    }

    public enum HeadgearKind : byte
    {
        None,
        FieldCap,
        CrestHelm,
        Halo,
        AntennaRig,
        HornedHelm,
        BucketHelm,
        Wraparound,
    }

    public enum BackKind : byte
    {
        None,
        FieldPack,
        TwinTanks,
        DorsalFin,
        SignalMast,
        JetPack,
        LongScarf,
    }

    /// <summary>One thing a player can wear, or a skin for their weapons. Purely visual: nothing here changes how anyone plays.</summary>
    public sealed class CosmeticItem
    {
        /// <summary>Permanent, and unique within its slot. Saves refer to items by this.</summary>
        public string Id;
        public string Name;
        public CosmeticSlot Slot;
        /// <summary>The main colour: the swatch, the glow, or a skin's base.</summary>
        public Color Color;
        /// <summary>A pattern's or skin's second colour.</summary>
        public Color Second;
        public PatternKind Pattern;
        /// <summary>Headgear or back item shape, as a <see cref="HeadgearKind"/> or <see cref="BackKind"/>.</summary>
        public byte Shape;
        /// <summary>0 matte to 1 mirror-like. Weapon skins only.</summary>
        public float Shine;
        /// <summary>Weapon skins only: keep the weapon's own colours (the factory finish).</summary>
        public bool Factory;

        /// <summary>Where the item sits in its slot's list. Sent over the network instead of the id.</summary>
        public int Index;
    }

    /// <summary>
    /// Every cosmetic in the game, defined in code so the server can check what a client
    /// asks to wear without loading any content. Every name and design is original.
    ///
    /// Items within a slot are sent over the network by their position in its list, so the
    /// lists are append-only: add at the end, never reorder, and retire an item by hiding it
    /// rather than removing it.
    /// </summary>
    public static class CosmeticCatalog
    {
        static readonly Dictionary<CosmeticSlot, List<CosmeticItem>> BySlot = new Dictionary<CosmeticSlot, List<CosmeticItem>>();

        static CosmeticCatalog()
        {
            // Colours for the suit and the trim: the same palette, chosen separately.
            (string id, string name, Color color)[] palette =
            {
                ("graphite", "Graphite", new Color(0.22f, 0.24f, 0.27f)),
                ("slate", "Slate", new Color(0.36f, 0.42f, 0.5f)),
                ("bone", "Bone", new Color(0.86f, 0.83f, 0.76f)),
                ("glacier", "Glacier", new Color(0.62f, 0.82f, 0.92f)),
                ("cobalt", "Cobalt", new Color(0.16f, 0.32f, 0.72f)),
                ("teal", "Teal", new Color(0.1f, 0.55f, 0.55f)),
                ("moss", "Moss", new Color(0.33f, 0.45f, 0.22f)),
                ("dune", "Dune", new Color(0.76f, 0.64f, 0.42f)),
                ("saffron", "Saffron", new Color(0.95f, 0.7f, 0.12f)),
                ("ember", "Ember", new Color(0.9f, 0.42f, 0.12f)),
                ("crimson", "Crimson", new Color(0.66f, 0.1f, 0.14f)),
                ("orchid", "Orchid", new Color(0.7f, 0.38f, 0.8f)),
                ("violet", "Violet", new Color(0.36f, 0.22f, 0.6f)),
                ("onyx", "Onyx", new Color(0.08f, 0.08f, 0.09f)),
                ("snow", "Snow", new Color(0.95f, 0.96f, 0.97f)),
            };
            foreach (var colour in palette) Add(new CosmeticItem { Id = colour.id, Name = colour.name, Slot = CosmeticSlot.SuitColor, Color = colour.color });
            foreach (var colour in palette) Add(new CosmeticItem { Id = colour.id, Name = colour.name, Slot = CosmeticSlot.TrimColor, Color = colour.color });

            // Patterns printed on the suit, in the suit colour and a shade of it.
            Pattern("plain", "Plain", PatternKind.Plain);
            Pattern("stripes", "Racing Stripes", PatternKind.Stripes);
            Pattern("chevrons", "Chevrons", PatternKind.Chevrons);
            Pattern("honeycomb", "Honeycomb", PatternKind.Honeycomb);
            Pattern("splinter", "Splinter", PatternKind.Splinter);
            Pattern("pixel", "Pixel Camo", PatternKind.Pixel);
            Pattern("dots", "Dotwork", PatternKind.Dots);
            Pattern("weave", "Weave", PatternKind.Weave);
            Pattern("tiger", "Tiger", PatternKind.Tiger);
            Pattern("circuit", "Circuit", PatternKind.Circuit);

            Headgear("none", "Bare", HeadgearKind.None);
            Headgear("field_cap", "Field Cap", HeadgearKind.FieldCap);
            Headgear("crest_helm", "Crest Helm", HeadgearKind.CrestHelm);
            Headgear("halo", "Halo", HeadgearKind.Halo);
            Headgear("antenna_rig", "Antenna Rig", HeadgearKind.AntennaRig);
            Headgear("horned_helm", "Horned Helm", HeadgearKind.HornedHelm);
            Headgear("bucket_helm", "Bucket Helm", HeadgearKind.BucketHelm);
            Headgear("wraparound", "Wraparound", HeadgearKind.Wraparound);

            // The glowing band across the face.
            Visor("arc", "Arc", new Color(0.15f, 0.85f, 1f));
            Visor("lime", "Lime", new Color(0.55f, 1f, 0.2f));
            Visor("amber", "Amber", new Color(1f, 0.68f, 0.1f));
            Visor("magenta", "Magenta", new Color(1f, 0.2f, 0.75f));
            Visor("ice", "Ice", new Color(0.88f, 0.95f, 1f));
            Visor("ultraviolet", "Ultraviolet", new Color(0.55f, 0.3f, 1f));
            Visor("blackout", "Blackout", new Color(0.05f, 0.05f, 0.06f));

            Back("none", "Nothing", BackKind.None);
            Back("field_pack", "Field Pack", BackKind.FieldPack);
            Back("twin_tanks", "Twin Tanks", BackKind.TwinTanks);
            Back("dorsal_fin", "Dorsal Fin", BackKind.DorsalFin);
            Back("signal_mast", "Signal Mast", BackKind.SignalMast);
            Back("jet_pack", "Jet Pack", BackKind.JetPack);
            Back("long_scarf", "Long Scarf", BackKind.LongScarf);

            // Weapon skins: a base, a second colour for the pattern, and how much it shines.
            Skin("factory", "Factory", Color.gray, Color.gray, PatternKind.Plain, 0f, factory: true);
            Skin("carbon", "Carbon", new Color(0.09f, 0.09f, 0.1f), new Color(0.2f, 0.21f, 0.23f), PatternKind.Weave, 0.45f);
            Skin("arctic", "Arctic", new Color(0.92f, 0.94f, 0.96f), new Color(0.6f, 0.66f, 0.72f), PatternKind.Splinter, 0.15f);
            Skin("woodland", "Woodland", new Color(0.28f, 0.36f, 0.2f), new Color(0.14f, 0.18f, 0.1f), PatternKind.Pixel, 0.1f);
            Skin("sandstorm", "Sandstorm", new Color(0.8f, 0.68f, 0.46f), new Color(0.58f, 0.46f, 0.3f), PatternKind.Stripes, 0.1f);
            Skin("tiger", "Tiger", new Color(0.95f, 0.5f, 0.1f), new Color(0.08f, 0.06f, 0.05f), PatternKind.Tiger, 0.2f);
            Skin("gilded", "Gilded", new Color(0.95f, 0.75f, 0.28f), new Color(0.78f, 0.56f, 0.16f), PatternKind.Plain, 0.9f);
            Skin("mirror", "Mirror", new Color(0.86f, 0.88f, 0.92f), new Color(0.7f, 0.72f, 0.76f), PatternKind.Plain, 1f);
            Skin("neon_grid", "Neon Grid", new Color(0.05f, 0.06f, 0.08f), new Color(0.15f, 0.9f, 1f), PatternKind.Circuit, 0.5f);
            Skin("magma", "Magma", new Color(0.1f, 0.06f, 0.05f), new Color(1f, 0.38f, 0.05f), PatternKind.Splinter, 0.3f);
            Skin("nebula", "Nebula", new Color(0.3f, 0.12f, 0.5f), new Color(0.95f, 0.4f, 0.85f), PatternKind.Honeycomb, 0.4f);
            Skin("bubblegum", "Bubblegum", new Color(0.98f, 0.6f, 0.78f), new Color(0.3f, 0.85f, 0.8f), PatternKind.Dots, 0.35f);
            Skin("redline", "Redline", new Color(0.12f, 0.12f, 0.13f), new Color(0.85f, 0.1f, 0.12f), PatternKind.Chevrons, 0.35f);
        }

        static void Add(CosmeticItem item)
        {
            if (!BySlot.TryGetValue(item.Slot, out List<CosmeticItem> list)) BySlot[item.Slot] = list = new List<CosmeticItem>();
            item.Index = list.Count;
            list.Add(item);
        }

        static void Pattern(string id, string name, PatternKind kind) =>
            Add(new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Pattern, Pattern = kind, Color = Color.white });

        static void Headgear(string id, string name, HeadgearKind kind) =>
            Add(new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Headgear, Shape = (byte)kind, Color = Color.white });

        static void Visor(string id, string name, Color glow) =>
            Add(new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Visor, Color = glow });

        static void Back(string id, string name, BackKind kind) =>
            Add(new CosmeticItem { Id = id, Name = name, Slot = CosmeticSlot.Back, Shape = (byte)kind, Color = Color.white });

        static void Skin(string id, string name, Color color, Color second, PatternKind pattern, float shine, bool factory = false) =>
            Add(new CosmeticItem
            {
                Id = id, Name = name, Slot = CosmeticSlot.WeaponSkin, Color = color, Second = second, Pattern = pattern, Shine = shine,
                Factory = factory,
            });

        // ------------------------------------------------------------------ lookups

        /// <summary>Every item for a slot, in the order they are offered.</summary>
        public static IReadOnlyList<CosmeticItem> InSlot(CosmeticSlot slot) => BySlot[slot];

        /// <summary>What a slot holds when nothing else is chosen: the first item in its list.</summary>
        public static CosmeticItem Default(CosmeticSlot slot) => BySlot[slot][0];

        /// <summary>The item with this id, or null.</summary>
        public static CosmeticItem Find(CosmeticSlot slot, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (CosmeticItem item in BySlot[slot])
            {
                if (item.Id == id) return item;
            }
            return null;
        }

        /// <summary>The item with this id, or the slot's default if there is none.</summary>
        public static CosmeticItem FindOrDefault(CosmeticSlot slot, string id) => Find(slot, id) ?? Default(slot);

        /// <summary>The item at a position in its slot's list, or null if there is none.</summary>
        public static CosmeticItem At(CosmeticSlot slot, int index)
        {
            List<CosmeticItem> list = BySlot[slot];
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        public static int Count(CosmeticSlot slot) => BySlot[slot].Count;

        public static readonly CosmeticSlot[] Slots =
        {
            CosmeticSlot.SuitColor, CosmeticSlot.TrimColor, CosmeticSlot.Pattern, CosmeticSlot.Headgear,
            CosmeticSlot.Visor, CosmeticSlot.Back, CosmeticSlot.WeaponSkin,
        };
    }
}
