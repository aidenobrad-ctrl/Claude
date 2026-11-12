using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>A built first-person weapon model and how to hold it.</summary>
    public struct ViewModelLayout
    {
        public Transform Root;
        public Transform Muzzle;
        public ViewModelKind Kind;
        /// <summary>Resting pose, in camera space.</summary>
        public Vector3 HipPosition;
        public Vector3 HipEuler;
        /// <summary>Aimed pose: puts the sight line through the centre of the screen.</summary>
        public Vector3 AdsPosition;
    }

    /// <summary>
    /// Builds a placeholder first-person model for any weapon out of boxes, from the
    /// proportions in its <see cref="WeaponViewProfile"/> and the attachments fitted to it. So
    /// every weapon looks different in the hands, and fitting a suppressor or a drum
    /// magazine is visible, before any real art exists.
    /// </summary>
    public static class ViewModelBuilder
    {
        // The skin of the model being built. Building happens all at once on the main thread,
        // so it is set for the length of one Build call.
        static CosmeticItem _skin;

        /// <param name="skin">The weapon skin to wear, or null (or the factory skin) for the weapon's own colours.</param>
        public static ViewModelLayout Build(Transform parent, WeaponBuild build, CosmeticItem skin = null)
        {
            _skin = skin;
            try
            {
                return BuildModel(parent, build);
            }
            finally
            {
                _skin = null;
            }
        }

        static Material BodyMaterial(WeaponViewProfile p) => LookMaterials.SkinBody(_skin) ?? GreyboxMaterials.Flat(p.bodyColor);
        static Material DarkMaterial(WeaponViewProfile p) => LookMaterials.SkinDark(_skin) ?? GreyboxMaterials.Flat(Darken(p.bodyColor));
        static Material AccentMaterial(WeaponViewProfile p) => LookMaterials.SkinAccent(_skin) ?? GreyboxMaterials.Unlit(p.accentColor);

        static ViewModelLayout BuildModel(Transform parent, WeaponBuild build)
        {
            var root = new GameObject("Model " + build.Weapon.weaponId).transform;
            root.SetParent(parent, false);

            WeaponViewProfile profile = build.Weapon.view;
            var layout = new ViewModelLayout { Root = root, Kind = profile.kind };

            switch (profile.kind)
            {
                case ViewModelKind.Launcher: BuildLauncher(ref layout, profile); break;
                case ViewModelKind.Blade: BuildBlade(ref layout, profile); break;
                case ViewModelKind.Thrown: BuildThrown(ref layout, profile); break;
                default: BuildGun(ref layout, profile, build); break;
            }
            return layout;
        }

        // ------------------------------------------------------------------ guns

        static void BuildGun(ref ViewModelLayout layout, WeaponViewProfile p, WeaponBuild build)
        {
            Transform root = layout.Root;
            float b = p.bulk, length = p.bodyLength;
            Material body = BodyMaterial(p);
            Material dark = DarkMaterial(p);
            Material accent = AccentMaterial(p);

            AttachmentVisual barrel = VisualOf(build, AttachmentSlot.Barrel);
            AttachmentVisual magazine = VisualOf(build, AttachmentSlot.Magazine);
            AttachmentVisual grip = VisualOf(build, AttachmentSlot.Grip);
            AttachmentVisual stock = VisualOf(build, AttachmentSlot.Stock);
            AttachmentVisual sight = VisualOf(build, AttachmentSlot.Sight);
            bool laser = VisualOf(build, AttachmentSlot.Laser) == AttachmentVisual.Laser;

            float top = 0.0375f * b;      // top surface of the receiver
            float front = length * 0.5f;  // front face of the receiver
            Part(root, "Receiver", Vector3.zero, new Vector3(0.055f * b, 0.075f * b, length), body);

            // Barrel: a handguard, then exposed barrel, then whatever is screwed on the end.
            float handguard = p.barrelLength * 0.55f;
            float exposed = p.barrelLength * 0.45f + (barrel == AttachmentVisual.LongBarrel ? 0.09f : 0f);
            float barrelY = 0.008f * b;
            if (handguard > 0.01f)
            {
                Part(root, "Handguard", new Vector3(0f, 0.004f * b, front + handguard * 0.5f),
                    new Vector3(0.046f * b, 0.052f * b, handguard), dark);
                Part(root, "Accent", new Vector3(0f, 0.004f * b + 0.027f * b, front + handguard * 0.5f),
                    new Vector3(0.03f * b, 0.004f, handguard * 0.6f), accent);
            }
            else
            {
                Part(root, "Accent", new Vector3(0f, top * 0.3f, 0f), new Vector3(0.057f * b, 0.008f, length * 0.4f), accent);
            }
            if (exposed > 0.01f)
            {
                Part(root, "Barrel", new Vector3(0f, barrelY, front + handguard + exposed * 0.5f),
                    new Vector3(0.022f * b, 0.022f * b, exposed), dark);
            }

            float muzzleZ = front + handguard + exposed;
            if (barrel == AttachmentVisual.Suppressor)
            {
                Part(root, "Suppressor", new Vector3(0f, barrelY, muzzleZ + 0.07f), new Vector3(0.04f, 0.04f, 0.14f), dark);
                muzzleZ += 0.14f;
            }
            else if (barrel == AttachmentVisual.MuzzleDevice)
            {
                Part(root, "MuzzleDevice", new Vector3(0f, barrelY, muzzleZ + 0.02f), new Vector3(0.034f, 0.034f, 0.04f), dark);
                muzzleZ += 0.04f;
            }

            // Stock.
            if (p.stockLength > 0.01f && stock != AttachmentVisual.NoStock)
            {
                Vector3 size = new Vector3(0.045f * b, 0.065f * b, p.stockLength);
                if (stock == AttachmentVisual.LightStock) size = new Vector3(0.02f * b, 0.045f * b, p.stockLength);
                else if (stock == AttachmentVisual.HeavyStock) size = new Vector3(0.056f * b, 0.082f * b, p.stockLength * 1.1f);
                Part(root, "Stock", new Vector3(0f, -0.012f * b, -front - size.z * 0.5f), size, body);
            }

            // Grip and magazine.
            Part(root, "Grip", new Vector3(0f, -0.075f * b, -length * 0.28f), new Vector3(0.034f * b, 0.09f, 0.045f), dark)
                .localRotation = Quaternion.Euler(18f, 0f, 0f);

            if (p.magazineLength > 0.01f)
            {
                float magazineZ = length * 0.12f;
                if (magazine == AttachmentVisual.DrumMagazine)
                {
                    Part(root, "Drum", new Vector3(0f, -top - 0.05f, magazineZ), new Vector3(0.085f, 0.09f, 0.075f), dark);
                }
                else
                {
                    float magazineLength = p.magazineLength * (magazine == AttachmentVisual.ExtendedMagazine ? 1.45f : 1f);
                    Part(root, "Magazine", new Vector3(0f, -top - magazineLength * 0.5f, magazineZ),
                            new Vector3(0.034f * b, magazineLength, 0.06f), dark)
                        .localRotation = Quaternion.Euler(-8f, 0f, 0f);
                }
            }

            // Things hung under and beside the handguard.
            float underY = 0.004f * b - 0.026f * b;
            float underZ = front + Mathf.Max(handguard, 0.04f) * 0.5f;
            if (grip == AttachmentVisual.VerticalGrip)
            {
                Part(root, "VerticalGrip", new Vector3(0f, underY - 0.04f, underZ), new Vector3(0.028f, 0.08f, 0.03f), dark);
            }
            else if (grip == AttachmentVisual.AngledGrip)
            {
                Part(root, "AngledGrip", new Vector3(0f, underY - 0.018f, underZ), new Vector3(0.03f, 0.03f, 0.09f), dark)
                    .localRotation = Quaternion.Euler(-22f, 0f, 0f);
            }
            if (laser)
            {
                float side = 0.023f * b + 0.011f;
                Part(root, "Laser", new Vector3(side, 0.004f * b, underZ), new Vector3(0.022f, 0.022f, 0.055f), dark);
                Part(root, "LaserLens", new Vector3(side, 0.004f * b, underZ + 0.03f), new Vector3(0.01f, 0.01f, 0.004f),
                    GreyboxMaterials.Unlit(new Color(1f, 0.15f, 0.1f)));
            }

            // Sights decide where the eye lines up when aiming.
            float sightZ = -length * 0.05f;
            float sightLine;
            switch (sight)
            {
                case AttachmentVisual.RedDot:
                    sightLine = WindowSight(root, top, sightZ, 0.034f, 0.028f, 0.05f, dark);
                    break;
                case AttachmentVisual.Holo:
                    sightLine = WindowSight(root, top, sightZ, 0.046f, 0.034f, 0.07f, dark);
                    break;
                case AttachmentVisual.Scope:
                    sightLine = WindowSight(root, top, sightZ, 0.04f, 0.04f, 0.17f, dark);
                    break;
                default:
                    if (build.Weapon.reticle == ReticleStyle.Scope)
                    {
                        // A built-in scope. It is swapped for the full-screen scope view when aiming.
                        Part(root, "ScopeMount", new Vector3(0f, top + 0.006f, sightZ), new Vector3(0.02f, 0.012f, 0.12f), dark);
                        Part(root, "Scope", new Vector3(0f, top + 0.037f, sightZ), new Vector3(0.05f, 0.05f, 0.26f), dark);
                        sightLine = top + 0.037f;
                    }
                    else
                    {
                        float rearZ = -length * 0.3f;
                        float frontZ = handguard > 0.01f ? front + handguard - 0.02f : front - 0.02f;
                        Part(root, "RearSightL", new Vector3(-0.012f, top + 0.01f, rearZ), new Vector3(0.007f, 0.02f, 0.012f), dark);
                        Part(root, "RearSightR", new Vector3(0.012f, top + 0.01f, rearZ), new Vector3(0.007f, 0.02f, 0.012f), dark);
                        Part(root, "FrontSight", new Vector3(0f, top + 0.01f, frontZ), new Vector3(0.005f, 0.02f, 0.005f), dark);
                        sightLine = top + 0.022f;
                    }
                    break;
            }

            layout.Muzzle = Anchor(root, "Muzzle", new Vector3(0f, barrelY, muzzleZ));

            // Handguns are held out at arm's length; long guns sit against the shoulder.
            bool handgun = p.stockLength <= 0.01f && length < 0.22f;
            layout.HipPosition = handgun
                ? new Vector3(0.15f, -0.15f, 0.44f)
                : new Vector3(0.17f, -0.17f, 0.3f + length * 0.2f);
            layout.AdsPosition = new Vector3(0f, -sightLine - 0.0015f, handgun ? 0.4f : 0.18f + length * 0.2f);
        }

        /// <summary>An open frame you look through. Returns the height of its centre above the weapon's origin.</summary>
        static float WindowSight(Transform root, float top, float z, float width, float height, float depth, Material material)
        {
            const float baseHeight = 0.012f, wall = 0.005f;
            float bottom = top + baseHeight;
            float pillarX = width * 0.5f + wall * 0.5f;

            Part(root, "SightBase", new Vector3(0f, top + baseHeight * 0.5f, z), new Vector3(width + wall * 2f, baseHeight, depth), material);
            Part(root, "SightL", new Vector3(-pillarX, bottom + height * 0.5f, z), new Vector3(wall, height, depth), material);
            Part(root, "SightR", new Vector3(pillarX, bottom + height * 0.5f, z), new Vector3(wall, height, depth), material);
            Part(root, "SightTop", new Vector3(0f, bottom + height + wall * 0.5f, z), new Vector3(width + wall * 2f, wall, depth), material);
            return bottom + height * 0.5f;
        }

        // ------------------------------------------------------------------ everything else

        static void BuildLauncher(ref ViewModelLayout layout, WeaponViewProfile p)
        {
            Transform root = layout.Root;
            float b = p.bulk, length = p.bodyLength;
            Material body = BodyMaterial(p);
            Material dark = DarkMaterial(p);
            Material accent = AccentMaterial(p);

            Part(root, "Tube", Vector3.zero, new Vector3(0.11f * b, 0.11f * b, length), body);
            Part(root, "Mouth", new Vector3(0f, 0f, length * 0.5f), new Vector3(0.135f * b, 0.135f * b, 0.05f), dark);
            Part(root, "Tail", new Vector3(0f, 0f, -length * 0.5f), new Vector3(0.125f * b, 0.125f * b, 0.06f), dark);
            Part(root, "Band", new Vector3(0f, 0f, length * 0.2f), new Vector3(0.115f * b, 0.115f * b, 0.02f), accent);
            Part(root, "Grip", new Vector3(0f, -0.1f * b, -length * 0.05f), new Vector3(0.035f, 0.1f, 0.045f), dark)
                .localRotation = Quaternion.Euler(14f, 0f, 0f);
            Part(root, "Sight", new Vector3(-0.07f * b, 0.07f * b, 0f), new Vector3(0.008f, 0.05f, 0.06f), dark);

            layout.Muzzle = Anchor(root, "Muzzle", new Vector3(0f, 0f, length * 0.5f + 0.03f));
            layout.HipPosition = new Vector3(0.2f, -0.15f, 0.28f + length * 0.15f);
            // A launcher stays on the shoulder when aimed; the crosshair does the aiming.
            layout.AdsPosition = new Vector3(0.12f, -0.11f, 0.24f + length * 0.15f);
        }

        static void BuildBlade(ref ViewModelLayout layout, WeaponViewProfile p)
        {
            Transform root = layout.Root;
            float length = p.bodyLength;

            Part(root, "Handle", Vector3.zero, new Vector3(0.028f, 0.032f, 0.15f), DarkMaterial(p));
            Part(root, "Guard", new Vector3(0f, 0f, 0.08f), new Vector3(0.012f, 0.075f, 0.012f), AccentMaterial(p));
            Part(root, "Blade", new Vector3(0f, 0f, 0.085f + length * 0.5f), new Vector3(0.006f, 0.036f * p.bulk, length), BodyMaterial(p));

            layout.Muzzle = Anchor(root, "Tip", new Vector3(0f, 0f, 0.085f + length));
            layout.HipPosition = new Vector3(0.2f, -0.2f, 0.3f);
            layout.HipEuler = new Vector3(-38f, -14f, 22f);
            layout.AdsPosition = layout.HipPosition;
        }

        static void BuildThrown(ref ViewModelLayout layout, WeaponViewProfile p)
        {
            Transform root = layout.Root;
            float length = p.bodyLength;

            Part(root, "Handle", Vector3.zero, new Vector3(0.018f, 0.024f, 0.09f), DarkMaterial(p));
            Part(root, "Blade", new Vector3(0f, 0f, 0.045f + length * 0.5f), new Vector3(0.005f, 0.03f, length), BodyMaterial(p));
            Part(root, "Ring", new Vector3(0f, 0f, -0.05f), new Vector3(0.006f, 0.022f, 0.012f), AccentMaterial(p));

            layout.Muzzle = Anchor(root, "Tip", new Vector3(0f, 0f, 0.045f + length));
            layout.HipPosition = new Vector3(0.19f, -0.13f, 0.36f);
            layout.HipEuler = new Vector3(-58f, -6f, 8f);
            layout.AdsPosition = new Vector3(0.15f, -0.1f, 0.36f);
        }

        // ------------------------------------------------------------------ helpers

        static AttachmentVisual VisualOf(WeaponBuild build, AttachmentSlot slot)
        {
            AttachmentDefinition attachment = build.Get(slot);
            return attachment != null ? attachment.visual : AttachmentVisual.None;
        }

        static Color Darken(Color color) => new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 1f);

        static Transform Part(Transform root, string name, Vector3 position, Vector3 size, Material material) =>
            GreyboxKit.Visual(root, name, PrimitiveType.Cube, position, size, material, castShadows: false).transform;

        static Transform Anchor(Transform root, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(root, false);
            anchor.localPosition = position;
            return anchor;
        }
    }
}
