using UnityEngine;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Net;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Combat;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Simulation;
using Pulsebreak.Shared.Weapons;

namespace Pulsebreak.Client.Player
{
    /// <summary>The objects that make up a spawned local player.</summary>
    public sealed class PlayerRig
    {
        public LocalPlayerController Player;
        public FirstPersonCamera Camera;
        public WeaponViewModel ViewModel;
        public WeaponFx Fx;
        public SfxPlayer Sfx;
    }

    /// <summary>
    /// Assembles the local player in code: the simulated body, the camera rig that follows it,
    /// and the cosmetic layer (weapon model, effects, audio) wired to simulation events.
    ///
    /// The body and the camera are deliberately separate objects. The body moves in fixed
    /// ticks; the camera is placed every frame at a blend between ticks.
    /// </summary>
    public static class PlayerRigFactory
    {
        /// <summary>The colour of hits on practice targets.</summary>
        static readonly Color TargetColor = new Color(1f, 0.55f, 0.15f);

        /// <param name="loadout">What the player carries: weapons, utility and perk set.</param>
        /// <param name="projectiles">The world's projectile simulation, which this player's weapons fire into.</param>
        /// <param name="session">The connection to play through, or null to play offline.</param>
        public static PlayerRig Create(Vector3 position, float yaw, MovementConfig movement,
            Loadout loadout, ProjectileSystem projectiles, ClientSession session = null)
        {
            var body = new GameObject("Player") { layer = GameLayers.Character };
            body.transform.position = position;
            body.AddComponent<CharacterController>();
            var player = body.AddComponent<LocalPlayerController>();

            var cameraObject = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraObject.transform.position = position + Vector3.up * movement.standEyeHeight;
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 600f;
            cameraObject.AddComponent<AudioListener>();
            var playerCamera = cameraObject.AddComponent<FirstPersonCamera>();
            var sfx = cameraObject.AddComponent<SfxPlayer>();

            var viewModelObject = new GameObject("ViewModel");
            viewModelObject.transform.SetParent(cameraObject.transform, false);
            var viewModel = viewModelObject.AddComponent<WeaponViewModel>();

            player.Initialize(movement, loadout, projectiles, playerCamera, viewModel, yaw, session);

            var fx = cameraObject.AddComponent<WeaponFx>();
            fx.Initialize(viewModel, player);

            // Presentation listens to the simulation; the simulation never listens back.
            player.WeaponFired += build =>
            {
                viewModel.OnFired(build);
                fx.PlayMuzzle(build);
                sfx.PlayShot(build);
            };
            player.ShotFired += shot => fx.PlayTracer(shot);
            player.ProjectileLaunched += fx.TrackProjectile;
            player.UtilityThrowStarted += _ => sfx.Play(Sfx.Throw, 0.7f, 0.06f);
            player.HitConfirmed += hit =>
            {
                if (hit.Killed)
                {
                    sfx.Play(Sfx.Kill, 0.9f);
                    if (hit.Zone == HitZone.Head) sfx.Play(Sfx.KillHeadshot, 0.8f);
                }
                else if (hit.Zone == HitZone.Head) sfx.Play(Sfx.Headshot, 0.9f);
                else sfx.Play(Sfx.Hit, 0.8f, 0.03f);

                // Online, the match draws the burst in the colour of whoever was hit.
                if (session == null) fx.PlayBodyHit(hit.Point, hit.Point - cameraObject.transform.position, TargetColor, hit.Zone == HitZone.Head);
            };
            player.ActiveWeaponChanged += _ => sfx.Play(Sfx.Swap, 0.7f, 0.05f);
            player.ReloadStarted += () => sfx.Play(Sfx.ReloadStart, 0.8f);
            player.RoundInserted += () => sfx.Play(Sfx.RoundIn, 0.7f, 0.04f);
            player.ReloadFinished += () => sfx.Play(Sfx.ReloadEnd, 0.8f);
            player.DryFired += () => sfx.Play(Sfx.DryFire, 0.7f);

            projectiles.Happened += projectileEvent =>
            {
                fx.OnProjectileEvent(projectileEvent);
                if (projectileEvent.Kind == ProjectileEventKind.Explosion) sfx.PlayAt(Sfx.Explosion, projectileEvent.Point, 1f, 0.06f, 6f);
                else if (projectileEvent.Kind == ProjectileEventKind.Bounce) sfx.PlayAt(Sfx.Bounce, projectileEvent.Point, 0.5f, 0.1f, 1.5f, 25f);
            };

            // An explosion shakes the view, more the closer it is.
            fx.Exploded += (point, radius) =>
            {
                float distance = Vector3.Distance(cameraObject.transform.position, point);
                playerCamera.AddShake(Mathf.Clamp01(1f - distance / (radius * 4f)) * 0.85f);
            };

            var steps = cameraObject.AddComponent<OwnFootsteps>();
            steps.Begin(player, sfx);

            player.Jumped += () => sfx.Play(Sfx.Jump, 0.5f, 0.05f);
            player.Landed += impact =>
            {
                playerCamera.AddLandingImpulse(impact);
                sfx.Play(Sfx.Land, Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(impact / 12f)), 0.05f);
            };
            player.SlideStarted += () => sfx.Play(Sfx.Slide, 0.7f, 0.05f);
            player.MantleStarted += () => sfx.Play(Sfx.Mantle, 0.7f, 0.05f);

            return new PlayerRig
            {
                Player = player,
                Camera = playerCamera,
                ViewModel = viewModel,
                Fx = fx,
                Sfx = sfx,
            };
        }
    }

    /// <summary>Our own footsteps: quiet, so we hear other people's over them.</summary>
    public sealed class OwnFootsteps : MonoBehaviour
    {
        readonly FootstepTimer _timer = new FootstepTimer();
        LocalPlayerController _player;
        SfxPlayer _sfx;

        public void Begin(LocalPlayerController player, SfxPlayer sfx)
        {
            _player = player;
            _sfx = sfx;
        }

        void Update()
        {
            if (_player == null || _sfx == null) return;
            MotorState motor = _player.MotorState;
            float loudness = _timer.Advance(motor.Velocity, motor.Grounded, motor.Stance, Time.deltaTime);
            if (loudness > 0f) _sfx.Play(Sfx.Footstep, loudness * 0.4f, 0.06f);
        }
    }
}
