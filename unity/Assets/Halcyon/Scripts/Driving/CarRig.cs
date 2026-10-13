// A car's visible body: the Blender model (Resources/Cars/<id>), posed from
// the simulation each frame. The root follows the physics body (centre of
// mass) interpolated between 240 Hz steps; wheels move with the suspension,
// steer and spin; callipers steer but do not spin; brake and reverse lights
// glow from the driver's inputs. Headlights come on with the sky (dusk to
// dawn, fog, heavy rain) unless the driver chooses, with a real spotlight
// near the camera; on wet roads the tyres throw spray.
using Halcyon.Core;
using System.Collections.Generic;
using UnityEngine;

namespace Halcyon
{
    public sealed class CarRig : MonoBehaviour
    {
        public Vehicle Vehicle { get; private set; }
        public CarVisual Visual { get; private set; }
        public string SpecId { get; private set; }
        Transform model;
        readonly Transform[] wheels = new Transform[4];
        readonly Transform[] calipers = new Transform[4];
        Vector3 prevPos, curPos;
        Quaternion prevRot, curRot;
        readonly double[] prevLen = new double[4], curLen = new double[4];
        readonly double[] prevSpin = new double[4], curSpin = new double[4];
        /// <summary>Null: the headlights follow the sky (SkyDirector.LightsNeeded); true or false: the driver's choice.</summary>
        public bool? LightsOverride;
        public bool Headlights => LightsOverride ?? (SkyDirector.Instance != null && SkyDirector.Instance.LightsNeeded);
        Light lamp;
        ParticleSystem spray;
        ParticleSystem.EmissionModule sprayEmission;
        static Material sprayMat;
        static Camera viewer;

        /// <summary>The L key: from automatic to the opposite of what the lights are doing now, and back to automatic.</summary>
        public void ToggleLights() => LightsOverride = LightsOverride.HasValue ? (bool?)null : !Headlights;

        /// <summary>Every car rig alive (the player, racers, traffic, ghosts): the sound gives the nearest a voice.</summary>
        public static readonly List<CarRig> All = new List<CarRig>();
        /// <summary>Makes no engine sound (a ghost).</summary>
        public bool Silent;

        void OnDestroy() => All.Remove(this);

        public static CarRig Create(Vehicle v, string specId, Color? paint = null)
        {
            var go = new GameObject("Car_" + specId);
            var rig = go.AddComponent<CarRig>();
            All.Add(rig);
            rig.Vehicle = v;
            rig.SpecId = specId;
            rig.Build(specId, paint);
            rig.Snap();
            return rig;
        }

        void Build(string specId, Color? paint)
        {
            var p = Vehicle.Params;
            Visual = CarVisual.Create(specId, p, paint);
            model = Visual.Root.transform;
            model.SetParent(transform, false);
            // The model's origin is the ground at mid-wheelbase; the root is the centre of mass.
            model.localPosition = new Vector3(0, (float)-p.cgHeight, (float)((p.a - p.b) / 2));
            model.localRotation = Quaternion.identity;
            // Wheels and callipers move in the root's frame from the physics.
            for (int i = 0; i < 4; i++)
            {
                var wp = p.wheels[i];
                string tag = (wp.front ? "F" : "R") + (wp.left ? "L" : "R");
                int k = (wp.front ? 0 : 2) + (wp.left ? 0 : 1);
                wheels[i] = Pivot(Visual.Wheels[k], "WheelPivot_" + tag);
                calipers[i] = Pivot(Visual.Calipers[k], "CaliperPivot_" + tag);
            }
            var spec = p.spec;
            var ground = model.localPosition;
            // One headlamp beam from the middle of the nose, aimed a little down the road.
            var lgo = new GameObject("Headlamp");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = ground + new Vector3(0, (float)System.Math.Min(0.75, spec.height * 0.5), (float)spec.length / 2 - 0.25f);
            lgo.transform.localRotation = Quaternion.Euler(4f, 0, 0);
            lamp = lgo.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.range = 85;
            lamp.spotAngle = 80;
            lamp.innerSpotAngle = 44;
            lamp.intensity = 24;
            lamp.color = new Color(1f, 0.95f, 0.86f);
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            // Spray from the rear tyres on a wet road.
            var sgo = new GameObject("Spray");
            sgo.transform.SetParent(transform, false);
            sgo.transform.localPosition = ground + new Vector3(0, 0.25f, -(float)spec.wheelbase / 2 - 0.4f);
            spray = sgo.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = spray.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startColor = new Color(0.86f, 0.88f, 0.92f, 0.16f);
            main.gravityModifier = 0.15f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3((float)spec.width * 0.9f, 0.1f, 0.3f);
            var size = spray.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.6f));
            var inherit = spray.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = 0.45f;
            sprayEmission = spray.emission;
            sprayEmission.rateOverTime = 0;
            var r = sgo.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (sprayMat == null) sprayMat = Mats.Particle("Spray", ProceduralTextures.SoftDot(), Color.white);
            r.sharedMaterial = sprayMat;
            spray.Play();
        }

        /// <summary>
        /// A pivot at the part's origin (the hub) aligned with the car, holding the
        /// part; the pivot is what moves, so the import's own axes never matter.
        /// </summary>
        Transform Pivot(Transform part, string name)
        {
            if (part == null) return null;
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            pivot.position = part.position;
            pivot.rotation = transform.rotation;
            part.SetParent(pivot, true);
            return pivot;
        }

        /// <summary>Reuse this rig for another vehicle of the same model (pooled traffic).</summary>
        public void Rebind(Vehicle v, Color paint)
        {
            Vehicle = v;
            Visual.SetPaint(paint);
            LightsOverride = null;
        }

        /// <summary>A time trial's ghost: see-through, no lamp beam, no spray, no shadow.</summary>
        public bool Ghost { get; private set; }

        /// <summary>Make this rig a ghost: every material turned into a see-through copy tinted towards the colour.</summary>
        public void MakeGhost(Color tint, float alpha = 0.35f)
        {
            Ghost = true;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                {
                    r.enabled = false;
                    continue;
                }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + " (ghost)" };
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    c = Color.Lerp(c, tint, 0.6f);
                    c.a = alpha;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                    Mats.MakeTransparent(m);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (lamp != null) lamp.enabled = false;
        }

        /// <summary>Record the simulation state after a step (call once per fixed step).</summary>
        public void Capture()
        {
            prevPos = curPos;
            prevRot = curRot;
            var v = Vehicle;
            curPos = Frame.Pos(v.pos.x, v.pos.y, v.pos.z);
            curRot = Frame.Rot(v.quat);
            for (int i = 0; i < 4; i++)
            {
                prevLen[i] = curLen[i];
                prevSpin[i] = curSpin[i];
                curLen[i] = v.wheels[i].length;
                curSpin[i] = v.wheels[i].spin;
            }
        }

        /// <summary>Jump straight to the current state (after spawns, resets, teleports).</summary>
        public void Snap()
        {
            Capture();
            Capture();
            Apply(1f);
        }

        /// <summary>Pose the model between the previous and current physics states.</summary>
        public void Apply(float alpha)
        {
            transform.SetPositionAndRotation(Vector3.LerpUnclamped(prevPos, curPos, alpha), Quaternion.Slerp(prevRot, curRot, alpha));
            var v = Vehicle;
            var p = v.Params;
            for (int i = 0; i < 4; i++)
            {
                var h = p.wheels[i].hardpoint;
                double len = prevLen[i] + (curLen[i] - prevLen[i]) * alpha;
                double spin = prevSpin[i] + (curSpin[i] - prevSpin[i]) * alpha;
                var local = Frame.Pos(h.x, h.y - len, h.z);
                var steer = Quaternion.Euler(0, (float)(-v.wheels[i].steer * Mathf.Rad2Deg), 0);
                if (wheels[i] != null)
                {
                    wheels[i].localPosition = local;
                    wheels[i].localRotation = steer * Quaternion.Euler((float)(spin * Mathf.Rad2Deg), 0, 0);
                }
                if (calipers[i] != null)
                {
                    calipers[i].localPosition = local;
                    calipers[i].localRotation = steer;
                }
            }
            bool braking = v.gear == -1 ? v.input.throttle > 0.05 : v.input.brake > 0.05;
            bool on = Headlights;
            Visual.SetLights(on, braking);
            // A real beam only near the camera (lights are costly); the lenses glow at any distance.
            if (viewer == null) viewer = Camera.main;
            float far = viewer != null ? (viewer.transform.position - transform.position).sqrMagnitude : 0;
            lamp.enabled = on && far < 180 * 180 && !Ghost;
            // Spray: from 30 km/h on a wet road, more with speed.
            double wet = v.wetness;
            double speed = System.Math.Abs(v.vLong);
            bool grounded = v.wheels[2].grounded || v.wheels[3].grounded;
            sprayEmission.rateOverTime = wet > 0.2 && speed > 8 && grounded && far < 250 * 250 ? (float)((speed - 8) * wet * 7) : 0f;
        }
    }
}
