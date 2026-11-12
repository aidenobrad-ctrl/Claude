using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Progression;

namespace Pulsebreak.Server
{
    /// <summary>
    /// Offers a dedicated server to the backend's matchmaking. It registers the server, then
    /// checks in every few seconds to say whether anyone is playing on it. While it is empty
    /// the backend may hand it a match: the rules to play, and the id that the players' join
    /// tickets will carry. The server switches to those rules and admits only those players.
    ///
    /// The fleet key (PULSEBREAK_FLEET_KEY in the environment) proves to the backend that this
    /// is one of our servers. Players' tickets are checked with the separate ticket key.
    /// </summary>
    public sealed class FleetAgent : MonoBehaviour
    {
        const float RetrySeconds = 5f;
        /// <summary>A match nobody turns up for is given up after this long.</summary>
        const float NoShowSeconds = 45f;

        [Serializable]
        sealed class RegisterBody
        {
            public string address;
            public int port;
            public uint version;
        }

        [Serializable]
        sealed class RegisterReply
        {
            public string id = "";
            public float heartbeatSeconds = 3f;
        }

        [Serializable]
        sealed class HeartbeatBody
        {
            public string state;
            public string matchId;
            public int players;
        }

        [Serializable]
        sealed class ResultReply
        {
            public string outcome = "";
        }

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        ServerHost _host;
        string _baseUrl, _key, _address, _serverId;
        int _port;
        float _interval = 3f, _timer;
        float _assignedAt;
        bool _busy;

        public void Begin(ServerHost host, string backendUrl, string fleetKey, string publicAddress, int port)
        {
            _host = host;
            _baseUrl = (backendUrl ?? "").TrimEnd('/');
            _key = fleetKey ?? "";
            _address = publicAddress;
            _port = port;
            if (_key.Length == 0) Debug.LogWarning("[Fleet] No fleet key (PULSEBREAK_FLEET_KEY): the backend will not accept this server.");
            if (!_host.Server.RequiresTicket) Debug.LogWarning("[Fleet] No ticket key (PULSEBREAK_TICKET_KEY): matchmade players cannot be checked, so anyone could join.");
            _host.Server.MatchReported += report => _ = ReportAsync(report);
            Debug.Log("[Fleet] Offering " + _address + ":" + _port + " to matchmaking at " + _baseUrl + ".");
        }

        void Update()
        {
            if (_host == null || !_host.Running || _busy) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = _interval;
            _ = _serverId == null ? RegisterAsync() : HeartbeatAsync();
        }

        async Task RegisterAsync()
        {
            _busy = true;
            try
            {
                (RegisterReply reply, _) = await PostAsync<RegisterReply>("/v1/fleet/servers",
                    new RegisterBody { address = _address, port = _port, version = Shared.Net.NetProtocol.Version });
                if (reply == null || string.IsNullOrEmpty(reply.id))
                {
                    _timer = RetrySeconds;
                    return;
                }
                _serverId = reply.id;
                _interval = Mathf.Clamp(reply.heartbeatSeconds, 1f, 30f);
                Debug.Log("[Fleet] Registered as " + _serverId + ".");
            }
            finally
            {
                _busy = false;
            }
        }

        async Task HeartbeatAsync()
        {
            _busy = true;
            try
            {
                GameServer server = _host.Server;
                int humans = server.HumanCount;
                bool assigned = server.MatchId.Length > 0;

                // Free when nobody is here, unless a match was just handed over and its
                // players may still be on their way.
                bool waitingForPlayers = assigned && humans == 0 && Time.unscaledTime - _assignedAt < NoShowSeconds;
                string state = humans > 0 || waitingForPlayers ? "busy" : "idle";

                (MatchAssignment reply, int status) = await PostAsync<MatchAssignment>("/v1/fleet/servers/" + Uri.EscapeDataString(_serverId) + "/heartbeat",
                    new HeartbeatBody { state = state, matchId = server.MatchId, players = humans });
                if (status == 404)
                {
                    // The backend has forgotten us (it restarted): register again.
                    _serverId = null;
                    return;
                }
                if (reply == null) return;
                if (string.IsNullOrEmpty(reply.matchId) || reply.matchId == server.MatchId) return;

                Playlist playlist = Playlists.Find(reply.playlist);
                if (playlist == null)
                {
                    Debug.LogWarning("[Fleet] Was handed a match in an unknown playlist \"" + reply.playlist + "\".");
                    return;
                }
                MatchSettings settings = MatchSettings.For(playlist.Mode);
                if (playlist.TeamSize > 0) settings.teamSize = playlist.TeamSize;
                else settings.maxPlayers = playlist.Players;
                // Casual: whoever does not turn up, or leaves, is replaced by a bot. Ranked is people only.
                settings.fillWithBots = !playlist.Ranked;
                if (server.Configure(settings, reply.matchId, reply))
                {
                    _assignedAt = Time.unscaledTime;
                    Debug.Log("[Fleet] Now hosting match " + reply.matchId + " (" + playlist.Title + ", " + reply.players.Length + " player(s) coming).");
                }
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Hands a finished match to the backend, trying a few times if it cannot be reached.</summary>
        async Task ReportAsync(MatchReport report)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (_serverId != null)
                {
                    (ResultReply reply, int status) = await PostAsync<ResultReply>("/v1/fleet/servers/" + Uri.EscapeDataString(_serverId) + "/results", report);
                    if (reply != null)
                    {
                        Debug.Log("[Fleet] Match " + report.matchId + ": " + reply.outcome + ".");
                        return;
                    }
                    if (status >= 400 && status < 500)
                    {
                        // Refused (not a match this server was given): trying again will not help.
                        Debug.LogWarning("[Fleet] The backend would not take the report of match " + report.matchId + " (" + status + ").");
                        return;
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt));
            }
            Debug.LogWarning("[Fleet] Could not report match " + report.matchId + "; its results are lost.");
        }

        /// <summary>A POST with the fleet key. The reply's body (null if none could be read) and its status (0 if unreachable).</summary>
        async Task<(T Body, int Status)> PostAsync<T>(string path, object body) where T : class
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path))
                {
                    request.Headers.Add("X-Fleet-Key", _key);
                    request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await Http.SendAsync(request))
                    {
                        int status = (int)response.StatusCode;
                        if (!response.IsSuccessStatusCode)
                        {
                            if (status != 404) Debug.LogWarning("[Fleet] " + path + " answered " + status + ".");
                            return (null, status);
                        }
                        string text = await response.Content.ReadAsStringAsync();
                        return (string.IsNullOrEmpty(text) ? Activator.CreateInstance<T>() : JsonUtility.FromJson<T>(text), status);
                    }
                }
            }
            catch (Exception failure)
            {
                Debug.LogWarning("[Fleet] Could not reach the backend: " + failure.Message);
                return (null, 0);
            }
        }

        void OnDestroy()
        {
            if (_serverId == null) return;
            // Tell the backend we are going, so it stops sending players here. Best effort.
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Delete, _baseUrl + "/v1/fleet/servers/" + Uri.EscapeDataString(_serverId)))
                {
                    request.Headers.Add("X-Fleet-Key", _key);
                    Http.SendAsync(request).Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception)
            {
                // Going anyway; the backend notices missed check-ins.
            }
        }
    }
}
