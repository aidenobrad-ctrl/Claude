using System.Collections.Generic;
using UnityEngine;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Cosmetics;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Client.Visuals
{
    /// <summary>
    /// Dresses a character body in a <see cref="CharacterLook"/>: the suit's colour and pattern
    /// on the torso and legs, the trim colour on the arms, a visor across the face, and
    /// headgear and a back item built from simple shapes.
    ///
    /// Everything added here is for looking at only. It has no colliders, so it can never be
    /// hit or block a shot, and nobody is harder or easier to hit for what they wear. Team
    /// shoulder pads (blue for a teammate, red for anyone else) sit on top of any outfit, so a
    /// player can always be told apart from the other side.
    /// </summary>
    public sealed class CharacterLookView
    {
        readonly Transform _headAnchor, _backAnchor, _padsAnchor;
        readonly Transform _headPart, _torsoPart;
        readonly List<Material> _owned = new List<Material>();
        readonly List<MeshRenderer> _suitRenderers = new List<MeshRenderer>();
        readonly List<MeshRenderer> _trimRenderers = new List<MeshRenderer>();
        MeshRenderer _headRenderer;
        readonly Material _pads;
        Material _suit, _trim, _helmet;
        Color _suitTint = Color.white, _trimBase = Color.white, _helmetBase = Color.white;
        GameObject _headItems, _backItems;
        CharacterLook _shown;

        public CharacterLookView(UnityCharacterBody body)
        {
            foreach (UnityCharacterBody.Part part in body.Parts)
            {
                GameObject go = part.Transform.gameObject;
                go.AddComponent<MeshFilter>().sharedMesh = GreyboxKit.PrimitiveMesh(part.Round ? PrimitiveType.Sphere : PrimitiveType.Cube);
                var renderer = go.AddComponent<MeshRenderer>();
                if (part.Zone == HitZone.Head)
                {
                    _headRenderer = renderer;
                    _headPart = part.Transform;
                }
                else if (go.name.StartsWith("Arm")) _trimRenderers.Add(renderer);
                else _suitRenderers.Add(renderer);
                if (go.name == "Torso") _torsoPart = part.Transform;
            }

            // Accessories hang from anchors that follow the head and torso, not from the parts
            // themselves: the parts are scaled to their size and would stretch anything on them.
            _headAnchor = new GameObject("Head Gear").transform;
            _headAnchor.SetParent(body.Root, false);
            _backAnchor = new GameObject("Back Gear").transform;
            _backAnchor.SetParent(body.Root, false);
            _padsAnchor = new GameObject("Team Pads").transform;
            _padsAnchor.SetParent(body.Root, false);

            _pads = new Material(GreyboxMaterials.Unlit(Color.red));
            GreyboxKit.Visual(_padsAnchor, "Pad L", PrimitiveType.Cube, new Vector3(-0.3f, 0.3f, 0f), new Vector3(0.16f, 0.06f, 0.18f), _pads, castShadows: false);
            GreyboxKit.Visual(_padsAnchor, "Pad R", PrimitiveType.Cube, new Vector3(0.3f, 0.3f, 0f), new Vector3(0.16f, 0.06f, 0.18f), _pads, castShadows: false);
            _padsAnchor.gameObject.SetActive(false);

            Apply(new CharacterLook());
        }

        /// <summary>Dresses the body. Asking again for the look already shown does nothing.</summary>
        public void Apply(CharacterLook look)
        {
            look = look ?? new CharacterLook();
            if (ReferenceEquals(look, _shown)) return;
            _shown = look;

            CosmeticItem suit = look.Item(CosmeticSlot.SuitColor), trim = look.Item(CosmeticSlot.TrimColor);
            CosmeticItem pattern = look.Item(CosmeticSlot.Pattern), visor = look.Item(CosmeticSlot.Visor);
            _suitTint = pattern.Pattern == PatternKind.Plain ? suit.Color : Color.white;
            _trimBase = trim.Color;
            _helmetBase = Color.Lerp(suit.Color, Color.black, 0.35f);

            // Each view has its own copies, so a hit flash on one player does not light up another.
            Release();
            _suit = Own(LookMaterials.Patterned(pattern.Pattern, suit.Color, Shade(suit.Color, trim.Color)));
            _trim = Own(LookMaterials.Patterned(PatternKind.Plain, trim.Color, trim.Color));
            _helmet = Own(LookMaterials.Patterned(PatternKind.Plain, _helmetBase, _helmetBase));
            Material glow = GreyboxMaterials.Unlit(visor.Color);
            Material trimShared = LookMaterials.Patterned(PatternKind.Plain, trim.Color, trim.Color);
            Material suitShared = LookMaterials.Patterned(PatternKind.Plain, Color.Lerp(suit.Color, Color.black, 0.2f), suit.Color);

            foreach (MeshRenderer renderer in _suitRenderers) renderer.sharedMaterial = _suit;
            foreach (MeshRenderer renderer in _trimRenderers) renderer.sharedMaterial = _trim;
            if (_headRenderer != null) _headRenderer.sharedMaterial = _helmet;

            if (_headItems != null) Object.Destroy(_headItems);
            if (_backItems != null) Object.Destroy(_backItems);
            _headItems = new GameObject("Head Items");
            _headItems.transform.SetParent(_headAnchor, false);
            _backItems = new GameObject("Back Items");
            _backItems.transform.SetParent(_backAnchor, false);

            var headgear = (HeadgearKind)look.Item(CosmeticSlot.Headgear).Shape;
            BuildVisor(_headItems.transform, headgear, glow);
            BuildHeadgear(_headItems.transform, headgear, trimShared, suitShared, glow);
            BuildBack(_backItems.transform, (BackKind)look.Item(CosmeticSlot.Back).Shape, trimShared, suitShared, glow);
        }

        /// <summary>Team pads: blue for a teammate, red for an opponent, or none (the preview, or nobody to tell apart).</summary>
        public void SetSide(bool show, bool friendly)
        {
            if (_padsAnchor.gameObject.activeSelf != show) _padsAnchor.gameObject.SetActive(show);
            if (show) GreyboxMaterials.SetColor(_pads, friendly ? new Color(0.25f, 0.6f, 1f) : new Color(1f, 0.25f, 0.2f));
        }

        /// <summary>0 for normal colours, 1 for white: the flash when a shot lands.</summary>
        public void Flash(float amount)
        {
            // A patterned suit is tinted white already; brightening it means lifting the pattern towards white.
            GreyboxMaterials.SetColor(_suit, Color.Lerp(_suitTint, Color.white * 2f, amount));
            GreyboxMaterials.SetColor(_trim, Color.Lerp(_trimBase, Color.white, amount));
            GreyboxMaterials.SetColor(_helmet, Color.Lerp(_helmetBase, Color.white, amount));
        }

        /// <summary>Keeps the accessories on the head and back as the body crouches and stands. Call after posing the body.</summary>
        public void Follow()
        {
            if (_headPart != null) _headAnchor.localPosition = _headPart.localPosition;
            if (_torsoPart == null) return;
            _backAnchor.localPosition = _torsoPart.localPosition;
            _padsAnchor.localPosition = _torsoPart.localPosition;
        }

        public void Destroy()
        {
            Release();
            if (_pads != null) Object.Destroy(_pads);
        }

        // ------------------------------------------------------------------ the parts

        void BuildVisor(Transform head, HeadgearKind headgear, Material glow)
        {
            if (headgear == HeadgearKind.Wraparound)
            {
                // A band all the way round, instead of across the front.
                GreyboxKit.Visual(head, "Visor", PrimitiveType.Cube, new Vector3(0f, 0.015f, 0f), new Vector3(0.27f, 0.055f, 0.27f), glow, castShadows: false);
                return;
            }
            // In front of whatever covers the head: the bucket, a helmet shell, or the head itself.
            float front = headgear == HeadgearKind.BucketHelm ? 0.158f
                : headgear == HeadgearKind.CrestHelm || headgear == HeadgearKind.HornedHelm ? 0.15f
                : 0.112f;
            GreyboxKit.Visual(head, "Visor", PrimitiveType.Cube, new Vector3(0f, 0.015f, front), new Vector3(0.19f, 0.05f, 0.04f), glow, castShadows: false);
        }

        static void BuildHeadgear(Transform head, HeadgearKind kind, Material trim, Material dark, Material glow)
        {
            switch (kind)
            {
                case HeadgearKind.FieldCap:
                    GreyboxKit.Visual(head, "Crown", PrimitiveType.Cylinder, new Vector3(0f, 0.09f, -0.005f), new Vector3(0.27f, 0.035f, 0.27f), trim);
                    GreyboxKit.Visual(head, "Brim", PrimitiveType.Cube, new Vector3(0f, 0.075f, 0.15f), new Vector3(0.21f, 0.015f, 0.12f), trim);
                    break;

                case HeadgearKind.CrestHelm:
                    GreyboxKit.Visual(head, "Shell", PrimitiveType.Sphere, new Vector3(0f, 0.035f, -0.01f), new Vector3(0.29f, 0.26f, 0.3f), dark);
                    GreyboxKit.Visual(head, "Crest", PrimitiveType.Cube, new Vector3(0f, 0.16f, -0.02f), new Vector3(0.035f, 0.11f, 0.3f), trim);
                    break;

                case HeadgearKind.Halo:
                    for (int i = 0; i < 14; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 14f;
                        Transform bit = GreyboxKit.Visual(head, "Halo", PrimitiveType.Cube,
                            new Vector3(Mathf.Sin(angle) * 0.15f, 0.24f, Mathf.Cos(angle) * 0.15f), new Vector3(0.07f, 0.018f, 0.025f), glow, castShadows: false).transform;
                        bit.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 90f, 0f);
                    }
                    break;

                case HeadgearKind.AntennaRig:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Transform rod = GreyboxKit.Visual(head, "Antenna", PrimitiveType.Cylinder, new Vector3(side * 0.085f, 0.19f, -0.03f),
                            new Vector3(0.014f, 0.09f, 0.014f), dark).transform;
                        rod.localRotation = Quaternion.Euler(0f, 0f, -side * 12f);
                        GreyboxKit.Visual(head, "Tip", PrimitiveType.Sphere, new Vector3(side * 0.105f, 0.285f, -0.03f), Vector3.one * 0.035f, glow, castShadows: false);
                    }
                    break;

                case HeadgearKind.HornedHelm:
                    GreyboxKit.Visual(head, "Shell", PrimitiveType.Sphere, new Vector3(0f, 0.035f, -0.01f), new Vector3(0.29f, 0.26f, 0.3f), dark);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Transform horn = GreyboxKit.Visual(head, "Horn", PrimitiveType.Cube, new Vector3(side * 0.15f, 0.12f, 0f),
                            new Vector3(0.035f, 0.15f, 0.035f), trim).transform;
                        horn.localRotation = Quaternion.Euler(0f, 0f, -side * 38f);
                    }
                    break;

                case HeadgearKind.BucketHelm:
                    GreyboxKit.Visual(head, "Bucket", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.31f, 0.3f), dark);
                    GreyboxKit.Visual(head, "Rim", PrimitiveType.Cube, new Vector3(0f, 0.17f, 0f), new Vector3(0.32f, 0.02f, 0.32f), trim);
                    break;
            }
        }

        static void BuildBack(Transform back, BackKind kind, Material trim, Material dark, Material glow)
        {
            switch (kind)
            {
                case BackKind.FieldPack:
                    GreyboxKit.Visual(back, "Pack", PrimitiveType.Cube, new Vector3(0f, 0.02f, -0.21f), new Vector3(0.3f, 0.36f, 0.15f), trim);
                    GreyboxKit.Visual(back, "Flap", PrimitiveType.Cube, new Vector3(0f, 0.15f, -0.29f), new Vector3(0.26f, 0.12f, 0.02f), dark);
                    break;

                case BackKind.TwinTanks:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        GreyboxKit.Visual(back, "Tank", PrimitiveType.Cylinder, new Vector3(side * 0.08f, 0.02f, -0.2f), new Vector3(0.11f, 0.2f, 0.11f), trim);
                        GreyboxKit.Visual(back, "Valve", PrimitiveType.Sphere, new Vector3(side * 0.08f, 0.24f, -0.2f), Vector3.one * 0.05f, glow, castShadows: false);
                    }
                    break;

                case BackKind.DorsalFin:
                    Transform fin = GreyboxKit.Visual(back, "Fin", PrimitiveType.Cube, new Vector3(0f, 0.08f, -0.2f), new Vector3(0.03f, 0.42f, 0.2f), trim).transform;
                    fin.localRotation = Quaternion.Euler(-14f, 0f, 0f);
                    break;

                case BackKind.SignalMast:
                    GreyboxKit.Visual(back, "Base", PrimitiveType.Cube, new Vector3(0.1f, 0.12f, -0.17f), new Vector3(0.09f, 0.12f, 0.08f), dark);
                    GreyboxKit.Visual(back, "Mast", PrimitiveType.Cylinder, new Vector3(0.1f, 0.5f, -0.17f), new Vector3(0.018f, 0.32f, 0.018f), dark);
                    GreyboxKit.Visual(back, "Light", PrimitiveType.Sphere, new Vector3(0.1f, 0.84f, -0.17f), Vector3.one * 0.045f, glow, castShadows: false);
                    break;

                case BackKind.JetPack:
                    GreyboxKit.Visual(back, "Frame", PrimitiveType.Cube, new Vector3(0f, 0.04f, -0.18f), new Vector3(0.26f, 0.3f, 0.08f), dark);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        GreyboxKit.Visual(back, "Thruster", PrimitiveType.Cylinder, new Vector3(side * 0.1f, 0f, -0.25f), new Vector3(0.1f, 0.17f, 0.1f), trim);
                        GreyboxKit.Visual(back, "Nozzle", PrimitiveType.Cylinder, new Vector3(side * 0.1f, -0.2f, -0.25f), new Vector3(0.07f, 0.03f, 0.07f), glow, castShadows: false);
                    }
                    break;

                case BackKind.LongScarf:
                    GreyboxKit.Visual(back, "Wrap", PrimitiveType.Cube, new Vector3(0f, 0.33f, 0f), new Vector3(0.3f, 0.07f, 0.3f), trim);
                    Transform tail = GreyboxKit.Visual(back, "Tail", PrimitiveType.Cube, new Vector3(0.09f, 0.06f, -0.17f), new Vector3(0.11f, 0.52f, 0.02f), trim).transform;
                    tail.localRotation = Quaternion.Euler(12f, 0f, -6f);
                    break;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The pattern's second colour: a shade of the suit, leaning a little towards the trim.</summary>
        static Color Shade(Color suit, Color trim)
        {
            Color.RGBToHSV(suit, out float h, out float s, out float v);
            Color darker = Color.HSVToRGB(h, s, v > 0.5f ? v - 0.28f : v + 0.22f);
            return Color.Lerp(darker, trim, 0.25f);
        }

        Material Own(Material shared)
        {
            var copy = new Material(shared);
            _owned.Add(copy);
            return copy;
        }

        void Release()
        {
            foreach (Material material in _owned)
            {
                if (material != null) Object.Destroy(material);
            }
            _owned.Clear();
        }
    }
}
