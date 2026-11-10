using UnityEngine;
using Pulsebreak.Client.Audio;
using Pulsebreak.Client.Weapons;
using Pulsebreak.Shared.Combat;
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
    /// and the cosmetic layer (viewmodel, effects, audio) wired to simulation events.
    ///
    /// The body and the camera are deliberately separate objects. The body moves in fixed
    /// ticks; the camera is placed every frame at a blend between ticks.
    /// </summary>
    public static class PlayerRigFactory
    {
        public static PlayerRig Create(Vector3 position, float yaw, MovementConfig movement, WeaponDefinition weapon)
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
            viewModel.Build();

            var fx = cameraObject.AddComponent<WeaponFx>();
            fx.Initialize(viewModel);

            player.Initialize(movement, weapon, playerCamera, viewModel, yaw);

            // Presentation listens to the simulation; the simulation never listens back.
            player.ShotFired += shot =>
            {
                viewModel.OnShot();
                fx.PlayShot(shot);
                sfx.Play(Sfx.Shot, 0.8f, 0.04f);
            };
            player.HitConfirmed += hit =>
            {
                if (hit.Killed) sfx.Play(Sfx.Kill, 0.9f);
                else if (hit.Zone == HitZone.Head) sfx.Play(Sfx.Headshot, 0.9f);
                else sfx.Play(Sfx.Hit, 0.8f, 0.03f);
            };

            player.Motor.Jumped += () => sfx.Play(Sfx.Jump, 0.5f, 0.05f);
            player.Motor.Landed += impact =>
            {
                playerCamera.AddLandingImpulse(impact);
                sfx.Play(Sfx.Land, Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(impact / 12f)), 0.05f);
            };
            player.Motor.SlideStarted += () => sfx.Play(Sfx.Slide, 0.7f, 0.05f);
            player.Motor.MantleStarted += () => sfx.Play(Sfx.Mantle, 0.7f, 0.05f);

            player.Weapon.ReloadStarted += () => sfx.Play(Sfx.ReloadStart, 0.8f);
            player.Weapon.ReloadFinished += () => sfx.Play(Sfx.ReloadEnd, 0.8f);
            player.Weapon.DryFired += () => sfx.Play(Sfx.DryFire, 0.7f);

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
