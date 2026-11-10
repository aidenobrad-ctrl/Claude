using UnityEngine;
using UnityEngine.Rendering;
using Pulsebreak.Client.Player;
using Pulsebreak.Client.Visuals;

namespace Pulsebreak.Client.Weapons
{
    /// <summary>
    /// Shot visuals: tracers, muzzle flash and impact marks. Everything is pooled up front, so
    /// firing allocates nothing.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        const int TracerCount = 16;
        const int ImpactCount = 48;
        const float TracerSpeed = 320f;
        const float TracerLength = 7f;
        const float MinTracerDistance = 2.5f;
        const float FlashTime = 0.035f;

        struct Tracer
        {
            public LineRenderer Line;
            public Vector3 From;
            public Vector3 Direction;
            public float Length;
            public float Travelled;
            public bool Active;
        }

        readonly Tracer[] _tracers = new Tracer[TracerCount];
        readonly Transform[] _impacts = new Transform[ImpactCount];
        int _nextTracer, _nextImpact;

        WeaponViewModel _viewModel;
        GameObject _flash;
        Light _flashLight;
        float _flashTimer;

        public void Initialize(WeaponViewModel viewModel)
        {
            _viewModel = viewModel;

            var pool = new GameObject("WeaponFx Pool").transform;
            Material tracerMaterial = GreyboxMaterials.Unlit(new Color(1f, 0.86f, 0.45f));
            Material impactMaterial = GreyboxMaterials.Flat(new Color(0.07f, 0.07f, 0.08f));

            for (int i = 0; i < TracerCount; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(pool, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.012f;
                line.endWidth = 0.03f;
                line.sharedMaterial = tracerMaterial;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                _tracers[i].Line = line;
            }

            for (int i = 0; i < ImpactCount; i++)
            {
                GameObject mark = GreyboxKit.Visual(pool, "Impact", PrimitiveType.Sphere,
                    Vector3.zero, new Vector3(0.07f, 0.07f, 0.012f), impactMaterial, castShadows: false);
                mark.SetActive(false);
                _impacts[i] = mark.transform;
            }

            _flash = GreyboxKit.Visual(viewModel.Muzzle, "MuzzleFlash", PrimitiveType.Sphere,
                new Vector3(0f, 0f, 0.04f), new Vector3(0.05f, 0.05f, 0.11f),
                GreyboxMaterials.Unlit(new Color(1f, 0.8f, 0.35f)), castShadows: false);
            _flash.SetActive(false);

            var lightObject = new GameObject("MuzzleLight");
            lightObject.transform.SetParent(viewModel.Muzzle, false);
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.75f, 0.4f);
            _flashLight.range = 6f;
            _flashLight.intensity = 3f;
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;
        }

        public void PlayShot(in ShotPresentation shot)
        {
            // Muzzle flash, slightly different every shot.
            _flashTimer = FlashTime;
            _flash.SetActive(true);
            _flash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.8f, 1.25f);
            _flash.transform.localScale = new Vector3(0.05f * size, 0.05f * size, 0.11f * size);
            _flashLight.enabled = true;

            // The tracer leaves the visible muzzle but ends where the bullet actually went.
            Vector3 from = _viewModel.Muzzle.position;
            Vector3 toEnd = shot.End - from;
            float distance = toEnd.magnitude;
            if (distance > MinTracerDistance)
            {
                ref Tracer tracer = ref _tracers[_nextTracer];
                _nextTracer = (_nextTracer + 1) % TracerCount;
                tracer.From = from;
                tracer.Direction = toEnd / distance;
                tracer.Length = distance;
                tracer.Travelled = 0f;
                tracer.Active = true;
                tracer.Line.enabled = true;
                UpdateTracer(ref tracer, 0f);
            }

            if (shot.HitWorld)
            {
                Transform mark = _impacts[_nextImpact];
                _nextImpact = (_nextImpact + 1) % ImpactCount;
                mark.SetPositionAndRotation(shot.End + shot.Normal * 0.004f, Quaternion.LookRotation(shot.Normal));
                mark.gameObject.SetActive(true);
            }
        }

        void Update()
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    _flash.SetActive(false);
                    _flashLight.enabled = false;
                }
            }

            for (int i = 0; i < TracerCount; i++)
            {
                if (_tracers[i].Active) UpdateTracer(ref _tracers[i], Time.deltaTime);
            }
        }

        static void UpdateTracer(ref Tracer tracer, float dt)
        {
            tracer.Travelled += TracerSpeed * dt;
            float tail = Mathf.Max(0f, tracer.Travelled - TracerLength);
            if (tail >= tracer.Length)
            {
                tracer.Active = false;
                tracer.Line.enabled = false;
                return;
            }

            float head = Mathf.Min(tracer.Travelled, tracer.Length);
            tracer.Line.SetPosition(0, tracer.From + tracer.Direction * tail);
            tracer.Line.SetPosition(1, tracer.From + tracer.Direction * head);
        }
    }
}
