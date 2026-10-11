// The sky over the island, from the simulation's clock and weather
// (Halcyon.Core):
//   - one directional light that is the sun by day and the moon by night,
//     its colour and strength from its height and the cloud, and a flash
//     light for lightning;
//   - the procedural sky (redder at sunset, grey under cloud, dark at night
//     with the moon's disc), a cloud layer drifting with the wind, a
//     trilight ambient and fog that follow the sky;
//   - a sky-only reflection probe re-rendered every few seconds, so paint
//     and wet roads reflect the sky that is there;
//   - rain around the camera, lightning, thunder that arrives at the speed
//     of sound, and the rain's hiss;
//   - wet roads (darker and glossier as they soak) and lights that come on
//     at dusk: lit windows, the festival, headlights (CarRig asks
//     LightsNeeded).
// Presentation only: nothing here feeds back into the simulation.
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyon.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyon
{
    public sealed class SkyDirector : MonoBehaviour
    {
        public static SkyDirector Instance { get; private set; }

        struct GlowMat
        {
            public Material m;
            public Color day, night;
        }

        struct WetMat
        {
            public Material m;
            public Color dry;
            public float drySmooth, wetSmooth, darken;
        }

        static readonly List<GlowMat> glow = new List<GlowMat>();
        static readonly List<WetMat> wet = new List<WetMat>();

        /// <summary>A material that glows more at night (emission from day to night colour).</summary>
        public static void Glow(Material m, Color day, Color night) => glow.Add(new GlowMat { m = m, day = day, night = night });

        /// <summary>A paved or earthen surface that darkens and shines when the roads are wet.</summary>
        public static void Wettable(Material m, float wetSmoothness, float darken) =>
            wet.Add(new WetMat { m = m, dry = m.GetColor("_BaseColor"), drySmooth = m.GetFloat("_Smoothness"), wetSmooth = wetSmoothness, darken = darken });

        /// <summary>Forget registered materials (a new island is being built).</summary>
        public static void ClearRegistry()
        {
            glow.Clear();
            wet.Clear();
        }

        /// <summary>0 by day, 1 at night (twilight in between).</summary>
        public float Night { get; private set; }
        /// <summary>Headlights on: dusk to dawn, fog, or heavy rain.</summary>
        public bool LightsNeeded { get; private set; }

        Simulation sim;
        Camera cam;
        Light sun, flashLight;
        Material sky;
        ReflectionProbe probe;
        float probeTimer;

        // The cloud layer: a flattened dome that follows the camera, its texture
        // regenerated off the main thread when the cover changes.
        const float CloudUv = 5200f;
        Transform dome;
        Material cloudMat;
        Texture2D cloudTex;
        Task<Color32[]> cloudJob;
        float cloudBuilt = -1, cloudPending;
        Vector2 cloudDrift;

        ParticleSystem rain;
        ParticleSystem.EmissionModule rainEmission;
        ParticleSystem.VelocityOverLifetimeModule rainVelocity;
        Material rainMat;
        Vector3 lastCam;

        AudioSource rainAudio, thunderAudio;
        AudioClip[] thunderNear, thunderFar;
        readonly List<(float at, float gain, bool near)> thunder = new List<(float, float, bool)>();
        float flash;
        readonly List<(float at, float level)> flashes = new List<(float, float)>();
        Vector3 flashDir = Vector3.down;

        static readonly Color SkyTintClear = new Color(0.52f, 0.62f, 0.78f), SkyTintGrey = new Color(0.55f, 0.56f, 0.58f);

        public static SkyDirector Create(Transform parent, Simulation sim, Camera cam, Light sun)
        {
            var go = new GameObject("Sky");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<SkyDirector>();
            d.Init(sim, cam, sun);
            return d;
        }

        void Init(Simulation s, Camera c, Light l)
        {
            Instance = this;
            sim = s;
            cam = c;
            sun = l;
            sky = RenderSettings.skybox;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            // Lightning lights the land from the strike's side.
            var fgo = new GameObject("Lightning");
            fgo.transform.SetParent(transform, false);
            flashLight = fgo.AddComponent<Light>();
            flashLight.type = LightType.Directional;
            flashLight.color = new Color(0.78f, 0.84f, 1f);
            flashLight.shadows = LightShadows.None;
            flashLight.intensity = 0;
            flashLight.enabled = false;

            // Reflections of the sky only (layer TransparentFX holds the clouds).
            var pgo = new GameObject("SkyReflections");
            pgo.transform.SetParent(transform, false);
            probe = pgo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.size = new Vector3(30000, 6000, 30000);
            probe.resolution = 128;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 1 << 1;
            probe.importance = 0;
            probe.nearClipPlane = 1;
            probe.farClipPlane = 10000;

            BuildClouds();
            BuildRain();

            rainAudio = gameObject.AddComponent<AudioSource>();
            rainAudio.clip = ProceduralAudio.RainLoop();
            rainAudio.loop = true;
            rainAudio.spatialBlend = 0;
            rainAudio.volume = 0;
            rainAudio.Play();
            thunderAudio = gameObject.AddComponent<AudioSource>();
            thunderAudio.spatialBlend = 0;
            thunderNear = new[] { ProceduralAudio.Thunder(11, true), ProceduralAudio.Thunder(12, true) };
            thunderFar = new[] { ProceduralAudio.Thunder(21, false), ProceduralAudio.Thunder(22, false), ProceduralAudio.Thunder(23, false) };
            Update();
            probe.RenderProbe();
        }

        void BuildClouds()
        {
            // A flattened dome: 7.5 km out, 1.6 km up at the top, centred on the camera.
            const int rings = 12, segs = 48;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                float rad = 7500f * t;
                float y = 1600f - 1750f * t * t; // dips below the horizon at the rim
                for (int k = 0; k <= segs; k++)
                {
                    float a = k * Mathf.PI * 2 / segs;
                    var p = new Vector3(Mathf.Cos(a) * rad, y, Mathf.Sin(a) * rad);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x / CloudUv, p.z / CloudUv));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < segs; k++)
                {
                    int a = r * (segs + 1) + k, b = a + segs + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "CloudDome" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(20000, 4000, 20000));
            var go = new GameObject("Clouds") { layer = 1 };
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            cloudTex = new Texture2D(256, 256, TextureFormat.RGBA32, true, false) { name = "Clouds", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            cloudMat = Mats.UnlitTransparent("Clouds", cloudTex, Color.white);
            cloudMat.renderQueue = (int)RenderQueue.Transparent - 50; // behind the rain
            cloudMat.SetFloat("_Cull", (float)CullMode.Off);
            mr.sharedMaterial = cloudMat;
            dome = go.transform;
            // The first cover now, the rest on a worker thread as the sky changes.
            float cover = (float)sim.Weather.Cloud;
            cloudTex.SetPixels32(ProceduralTextures.CloudPixels(256, cover, 501));
            cloudTex.Apply(true, false);
            cloudBuilt = cover;
        }

        void BuildRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            rain = go.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = rain.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5;
            main.startLifetime = 1.3f;
            main.startSpeed = 0;
            main.startSize = 0.022f;
            main.maxParticles = 14000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(0.78f, 0.82f, 0.88f, 0.42f);
            var shape = rain.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(64, 1, 64);
            rainVelocity = rain.velocityOverLifetime;
            rainVelocity.enabled = true;
            rainVelocity.space = ParticleSystemSimulationSpace.World;
            rainVelocity.x = 0;
            rainVelocity.y = -14;
            rainVelocity.z = 0;
            rainEmission = rain.emission;
            rainEmission.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 1f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            rainMat = Mats.Particle("Rain", ProceduralTextures.RainStreak(), Color.white);
            r.sharedMaterial = rainMat;
            rain.Play();
        }

        /// <summary>A strike from the simulation (Core axes): a flash now, thunder when the sound arrives.</summary>
        public void Lightning(double x, double z, double brightness)
        {
            var at = Frame.Pos(x, 0, z);
            var me = cam.transform.position;
            var flat = new Vector3(at.x - me.x, 0, at.z - me.z);
            float d = Mathf.Max(50, flat.magnitude);
            float b = (float)brightness * Mathf.Clamp(1600f / d, 0.25f, 1.4f);
            float now = Time.time;
            // A strike flickers: two or three pulses in a quarter of a second.
            flashes.Add((now, b));
            flashes.Add((now + 0.07f, b * 0.5f));
            if (brightness > 0.75) flashes.Add((now + 0.19f, b * 0.8f));
            flashDir = Quaternion.AngleAxis(-35, Vector3.Cross(flat.normalized, Vector3.up)) * -flat.normalized;
            thunder.Add((now + d / 343f, Mathf.Clamp(1800f / d, 0.15f, 1f) * (float)brightness, d < 1100));
        }

        void Update()
        {
            if (sim == null || cam == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            var clock = sim.Clock;
            var w = sim.Weather;
            float el = (float)(clock.SunElevation * Mathf.Rad2Deg);
            float daylight = (float)clock.Daylight;
            float cloud = (float)w.Cloud, rainK = (float)w.Rain, fog = (float)w.Fog;
            float wetness = Mathf.Max((float)sim.Wetness, (float)w.Puddles);
            Night = 1 - daylight;
            LightsNeeded = daylight < 0.55f || fog > 0.45f || rainK > 0.35f;

            // Lightning pulses decay in a tenth of a second.
            float now = Time.time;
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                if (flashes[i].at > now) continue;
                flash = Mathf.Max(flash, flashes[i].level);
                flashes.RemoveAt(i);
            }
            flash = Mathf.Max(0, flash - dt * 7);
            for (int i = thunder.Count - 1; i >= 0; i--)
            {
                if (thunder[i].at > now) continue;
                var set = thunder[i].near ? thunderNear : thunderFar;
                thunderAudio.PlayOneShot(set[Random.Range(0, set.Length)], thunder[i].gain);
                thunder.RemoveAt(i);
            }

            // Sun by day, the moon by night: one light, switching while both are dim (sun 3 degrees down).
            bool isSun = el > -3;
            double x, y, z;
            if (isSun) clock.SunDirection(out x, out y, out z);
            else clock.MoonDirection(out x, out y, out z);
            var toBody = Frame.Dir(x, y, z);
            sun.transform.rotation = Quaternion.LookRotation(-toBody);
            float sunset = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-4, 2, el)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(4, 18, el)));
            if (isSun)
            {
                float up = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-3, 3, el));
                sun.color = Color.Lerp(new Color(1f, 0.95f, 0.88f), new Color(1f, 0.58f, 0.32f), sunset);
                sun.intensity = 1.7f * up * (1 - 0.8f * cloud);
                sun.shadowStrength = 0.9f * (1 - 0.7f * cloud);
            }
            else
            {
                float up = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-9, -3, el));
                sun.color = new Color(0.62f, 0.72f, 1f);
                sun.intensity = 0.2f * up * (1 - 0.85f * cloud);
                sun.shadowStrength = 0.55f * (1 - 0.8f * cloud);
            }
            sun.shadows = sun.intensity > 0.03f ? LightShadows.Soft : LightShadows.None;
            flashLight.enabled = flash > 0.01f;
            flashLight.intensity = flash * 2.4f;
            flashLight.transform.rotation = Quaternion.LookRotation(flashDir);

            // The procedural sky (its sun disc is the moon at night).
            if (sky != null)
            {
                sky.SetFloat("_Exposure", Mathf.Lerp(0.06f, 1.2f, daylight) * (1 - 0.5f * cloud) + flash * 1.6f);
                sky.SetFloat("_AtmosphereThickness", 1.0f + 0.85f * sunset + 0.45f * cloud);
                sky.SetColor("_SkyTint", Color.Lerp(SkyTintClear, SkyTintGrey, cloud));
                sky.SetFloat("_SunSize", isSun ? 0.035f * (1 - 0.92f * cloud) : 0.022f * (1 - 0.95f * cloud));
                sky.SetColor("_GroundColor", Color.Lerp(new Color(0.03f, 0.035f, 0.045f), new Color(0.36f, 0.38f, 0.40f), daylight));
            }

            // Ambient: zenith, horizon and ground, from night through sunset to day, greyed by cloud.
            Color Tri(Color night, Color sunsetC, Color day)
            {
                var c = Color.Lerp(night, day, daylight);
                c = Color.Lerp(c, sunsetC, sunset * 0.8f);
                float grey = c.grayscale;
                c = Color.Lerp(c, new Color(grey, grey, grey * 1.04f), cloud * 0.7f) * (1 - 0.32f * cloud);
                return c + new Color(0.55f, 0.6f, 0.8f) * flash;
            }
            RenderSettings.ambientSkyColor = Tri(new Color(0.03f, 0.042f, 0.08f), new Color(0.3f, 0.33f, 0.48f), new Color(0.42f, 0.55f, 0.75f));
            var horizon = Tri(new Color(0.032f, 0.037f, 0.055f), new Color(0.86f, 0.55f, 0.38f), new Color(0.62f, 0.68f, 0.74f));
            RenderSettings.ambientEquatorColor = horizon;
            RenderSettings.ambientGroundColor = Tri(new Color(0.012f, 0.012f, 0.016f), new Color(0.22f, 0.16f, 0.13f), new Color(0.27f, 0.25f, 0.22f));
            RenderSettings.reflectionIntensity = Mathf.Lerp(0.25f, 1f, daylight);

            // Fog: haze by day, thick in fog and rain, the colour of the horizon.
            RenderSettings.fogColor = Color.Lerp(horizon, new Color(0.62f, 0.64f, 0.66f) * Mathf.Lerp(0.08f, 1f, daylight), Mathf.Clamp01(fog * 1.2f));
            RenderSettings.fogDensity = 0.00022f + 0.0065f * Mathf.Pow(fog, 1.6f) + 0.0007f * rainK;

            UpdateClouds(dt, daylight, sunset, cloud);
            UpdateRain(dt, rainK, daylight, (float)w.Wind, (float)w.WindDir);
            rainAudio.volume = Mathf.Clamp01(rainK) * 0.55f;

            // Wet roads: darker and glossier as they soak.
            float k = Mathf.Pow(Mathf.Clamp01(wetness), 0.7f);
            for (int i = wet.Count - 1; i >= 0; i--)
            {
                var m = wet[i];
                if (m.m == null) { wet.RemoveAt(i); continue; }
                m.m.SetColor("_BaseColor", m.dry * Mathf.Lerp(1, m.darken, k));
                m.m.SetFloat("_Smoothness", Mathf.Lerp(m.drySmooth, m.wetSmooth, k));
            }
            // Night lights: windows and the festival, coming on through dusk (and a little on dark, stormy days).
            float lit = Mathf.Clamp01(Night * 1.25f + cloud * rainK * 0.25f);
            for (int i = glow.Count - 1; i >= 0; i--)
            {
                var g = glow[i];
                if (g.m == null) { glow.RemoveAt(i); continue; }
                Mats.SetEmission(g.m, Color.Lerp(g.day, g.night, lit));
            }

            // The sky reflection: a face at a time, a full cube every two seconds.
            probeTimer -= dt;
            if (probeTimer <= 0)
            {
                probe.transform.position = cam.transform.position;
                probe.RenderProbe();
                probeTimer = 2f;
            }
        }

        void UpdateClouds(float dt, float daylight, float sunset, float cloud)
        {
            var cp = cam.transform.position;
            dome.position = new Vector3(cp.x, 0, cp.z);
            // World-fixed clouds that drift with the wind (Core wind direction: from north toward east).
            double wd = sim.Weather.WindDir;
            float wind = (float)sim.Weather.Wind * 2.2f; // clouds move faster than the air at the ground
            cloudDrift += new Vector2(Mathf.Sin((float)wd), Mathf.Cos((float)wd)) * wind * dt / CloudUv;
            cloudMat.SetTextureOffset("_BaseMap", new Vector2(cp.x / CloudUv, cp.z / CloudUv) - cloudDrift);
            cloudMat.SetTextureOffset("_MainTex", new Vector2(cp.x / CloudUv, cp.z / CloudUv) - cloudDrift);
            // Lit clouds: white by day, pink-gold at sunset, dark grey under a storm, faint blue at night.
            var lit = Color.Lerp(new Color(0.07f, 0.08f, 0.11f), new Color(0.97f, 0.97f, 0.99f), daylight);
            lit = Color.Lerp(lit, new Color(1f, 0.72f, 0.55f), sunset * 0.7f);
            lit *= 1 - 0.6f * cloud * cloud;
            lit += new Color(0.8f, 0.85f, 1f) * flash;
            lit.a = Mathf.Lerp(0.55f, 1f, cloud);
            cloudMat.SetColor("_BaseColor", lit);
            cloudMat.SetColor("_Color", lit);
            // A new cover texture off the main thread when the sky has changed enough.
            if (cloudJob == null && Mathf.Abs(cloud - cloudBuilt) > 0.04f)
            {
                cloudPending = cloud;
                float c = cloud;
                cloudJob = Task.Run(() => ProceduralTextures.CloudPixels(256, c, 501));
            }
            if (cloudJob != null && cloudJob.IsCompleted)
            {
                if (!cloudJob.IsFaulted)
                {
                    cloudTex.SetPixels32(cloudJob.Result);
                    cloudTex.Apply(true, false);
                    cloudBuilt = cloudPending;
                }
                cloudJob = null;
            }
        }

        void UpdateRain(float dt, float rainK, float daylight, float wind, float windDir)
        {
            // Emit ahead of a moving camera so the rain is all around it at any speed.
            var cp = cam.transform.position;
            var vel = dt > 0 ? (cp - lastCam) / dt : Vector3.zero;
            if (vel.sqrMagnitude > 90 * 90) vel = Vector3.zero; // a teleport
            lastCam = cp;
            rain.transform.position = cp + new Vector3(vel.x, 0, vel.z) * 0.7f + Vector3.up * 16;
            rainEmission.rateOverTime = rainK * 9000f;
            var wv = Frame.Dir(Mathf.Sin(windDir) * wind, 0, -Mathf.Cos(windDir) * wind) * 0.6f;
            rainVelocity.x = wv.x;
            rainVelocity.z = wv.z;
            var tint = Color.Lerp(new Color(0.3f, 0.33f, 0.4f, 0.35f), new Color(0.8f, 0.84f, 0.9f, 0.42f), daylight) + new Color(0.5f, 0.5f, 0.6f, 0) * flash;
            rainMat.SetColor("_BaseColor", tint);
            rainMat.SetColor("_TintColor", tint);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
