using System;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Movement;
using Pulsebreak.Shared.Net;
using Pulsebreak.Shared.Simulation;

namespace Pulsebreak.Server
{
    /// <summary>
    /// Runs a <see cref="GameServer"/> inside Unity: opens the socket, gives the server
    /// Unity's physics to play in, and steps it at a fixed rate from the frame loop.
    ///
    /// The same component serves both ways of running a server: on its own in a headless
    /// build (a dedicated server), or alongside a client in the same process when a player
    /// hosts a match from the menu.
    /// </summary>
    public sealed class ServerHost : MonoBehaviour
    {
        GameServer _server;
        double _accumulator;
        double _lastTime;

        public GameServer Server => _server;
        public bool Running => _server != null;
        public int Port { get; private set; }

        /// <summary>Opens the port and starts a server. Throws if the port cannot be opened.</summary>
        public void Begin(int port, MovementConfig movement, LoadoutCatalog catalog, ServerConfig config)
        {
            if (_server != null) return;

            GameLayers.ConfigureCollisionMatrix();
            var socket = new UdpSocket(port);
            uint secret = (uint)Guid.NewGuid().GetHashCode();
            _server = new GameServer(socket, new UnityGameWorld(server: true), movement, catalog, config, secret);
            _server.Logged += message => Debug.Log("[Server] " + message);
            Port = port;
            _lastTime = Time.realtimeSinceStartupAsDouble;
            _accumulator = 0.0;
            Debug.Log("[Server] Listening on UDP port " + port + ": " + MatchSettings.Title(_server.Settings.mode) + " on " + _server.Arena.DisplayName + ".");
        }

        public void End()
        {
            if (_server == null) return;
            _server.Dispose();
            _server = null;
            Debug.Log("[Server] Stopped.");
        }

        void Update()
        {
            if (_server == null) return;

            // Real time, not game time: the server must keep its pace even if the game is paused or slowed.
            double now = Time.realtimeSinceStartupAsDouble;
            _accumulator += Math.Min(now - _lastTime, 0.25);
            _lastTime = now;

            int steps = 0;
            while (_accumulator >= SimConstants.TickDelta)
            {
                _accumulator -= SimConstants.TickDelta;
                if (++steps > SimConstants.MaxTicksPerFrame)
                {
                    // Too far behind to catch up; drop the backlog rather than spiral.
                    _accumulator = 0.0;
                    break;
                }
                _server.Step(now);
            }
        }

        void OnDestroy() => End();
    }
}
