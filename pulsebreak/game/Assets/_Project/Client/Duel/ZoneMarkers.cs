using UnityEngine;
using Pulsebreak.Client.Visuals;
using Pulsebreak.Shared.Match;

namespace Pulsebreak.Client.Duel
{
    /// <summary>
    /// The capture zones drawn in the world: a ring on the ground and a pole with a flag in
    /// the middle, in the colour of whoever holds the zone (ours, theirs, or nobody's). A zone
    /// being fought over flashes. In Hardpoint only the zone in play is shown.
    ///
    /// Only for looking at: the server alone decides who is standing in a zone.
    /// </summary>
    public sealed class ZoneMarkers : MonoBehaviour
    {
        const int Segments = 28;
        static readonly Color Neutral = new Color(0.92f, 0.94f, 0.97f);

        ArenaLayout _arena;
        ArenaZone[] _areas;
        bool _hardpoint;
        GameObject[] _roots;
        Material[] _materials;

        public ArenaLayout Arena => _arena;

        public static ZoneMarkers Create(ArenaLayout arena, bool hardpoint)
        {
            var markers = new GameObject("Zone Markers").AddComponent<ZoneMarkers>();
            markers.Build(arena, hardpoint);
            return markers;
        }

        void Build(ArenaLayout arena, bool hardpoint)
        {
            _arena = arena;
            _hardpoint = hardpoint;
            _areas = hardpoint ? arena.Hardpoints : arena.Zones;
            _roots = new GameObject[_areas.Length];
            _materials = new Material[_areas.Length];

            for (int z = 0; z < _areas.Length; z++)
            {
                ArenaZone area = _areas[z];
                var root = new GameObject("Zone " + area.Name).transform;
                root.SetParent(transform, false);
                root.localPosition = area.Center;
                _roots[z] = root.gameObject;

                Material material = new Material(GreyboxMaterials.Unlit(Neutral));
                _materials[z] = material;

                // The edge: short bars laid end to end around the circle.
                float segment = 2f * Mathf.PI * area.Radius / Segments;
                for (int i = 0; i < Segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / Segments;
                    var at = new Vector3(Mathf.Sin(angle) * area.Radius, 0.03f, Mathf.Cos(angle) * area.Radius);
                    GameObject bar = GreyboxKit.Visual(root, "Edge", PrimitiveType.Cube, at, new Vector3(segment * 0.8f, 0.04f, 0.14f), material, castShadows: false);
                    bar.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + 90f, 0f);
                }

                // A pole and a flag, seen from across the map.
                GreyboxKit.Visual(root, "Pole", PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f), new Vector3(0.08f, 3.2f, 0.08f), material, castShadows: false);
                GreyboxKit.Visual(root, "Flag", PrimitiveType.Cube, new Vector3(0.35f, 2.9f, 0f), new Vector3(0.6f, 0.4f, 0.04f), material, castShadows: false);
            }
        }

        /// <summary>Colours each zone for this frame, and in Hardpoint shows only the active one.</summary>
        public void Show(ZoneState[] zones, int count, int activeZone, int ourTeam)
        {
            bool blink = Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.25f;
            for (int z = 0; z < _roots.Length; z++)
            {
                bool visible = _hardpoint ? z == activeZone : z < count;
                if (_roots[z].activeSelf != visible) _roots[z].SetActive(visible);
                if (!visible || z >= count) continue;

                ZoneState zone = zones[z];
                Color color = DuelUi.TeamColor(zone.Owner, ourTeam, 1f, Neutral);
                if (zone.Contested && blink) color = Color.white;
                else if (!_hardpoint && zone.Capturer != Teams.None)
                    color = Color.Lerp(color, DuelUi.TeamColor(zone.Capturer, ourTeam, 1f, Neutral), 0.35f + 0.3f * Mathf.PingPong(Time.unscaledTime * 2f, 1f));
                GreyboxMaterials.SetColor(_materials[z], color);
            }
        }

        void OnDestroy()
        {
            if (_materials == null) return;
            foreach (Material material in _materials)
            {
                if (material != null) Destroy(material);
            }
        }
    }
}
