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
                if (hit.Killed) sfx.Play(Sfx.Kill, 0.9f);
                else if (hit.Zone == HitZone.Head) sfx.Play(Sfx.Headshot, 0.9f);
                else sfx.Play(Sfx.Hit, 0.8f, 0.03f);
            };
            player.ActiveWeaponChanged += _ => sfx.Play(Sfx.Swap, 0.7f, 0.05f);
            player.ReloadStarted += () => sfx.Play(Sfx.ReloadStart, 0.8f);
            player.RoundInserted += () => sfx.Play(Sfx.RoundIn, 0.7f, 0.04f);
            player.ReloadFinished += () => sfx.Play(Sfx.ReloadEnd, 0.8f);
            player.DryFired += () => sfx.Play(Sfx.DryFire, 0.7f);

            projectiles.Happened += projectileEvent =>
            {
                fx.OnProjectileEvent(projectileEvent);
                if (projectileEvent.Kind == ProjectileEventKind.Explosion) sfx.Play(Sfx.Explosion, 1f, 0.06f);
                else if (projectileEvent.Kind == ProjectileEventKind.Bounce) sfx.Play(Sfx.Bounce, 0.5f, 0.1f);
            };

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
}
