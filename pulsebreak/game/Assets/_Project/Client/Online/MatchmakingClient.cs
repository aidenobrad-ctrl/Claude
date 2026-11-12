using System;
using System.Threading.Tasks;
using Pulsebreak.Shared.Match;
using Pulsebreak.Shared.Net;

namespace Pulsebreak.Client.Online
{
    /// <summary>Where a place in the matchmaking queue stands.</summary>
    [Serializable]
    public sealed class MatchTicketData
    {
        public string id = "";
        /// <summary>"searching", "matched", "cancelled" or "expired".</summary>
        public string status = "";
        /// <summary>The playlist, such as "tdm".</summary>
        public string playlist = "";
        public int waitedSeconds;
        /// <summary>People looking for the same playlist right now, this one included.</summary>
        public int searching;
        /// <summary>Once matched: the game server to join, as host:port.</summary>
        public string address = "";
        /// <summary>Once matched: the ticket to show that server.</summary>
        public string joinTicket = "";
        public string matchId = "";

        public bool Searching => status == "searching";
        public bool Matched => status == "matched";
    }

    [Serializable]
    sealed class GuestData
    {
        public string token = "";
        public string guestId = "";
        public string name = "";
        public int expiresIn;
    }

    [Serializable] sealed class GuestBody { public string name; }
    [Serializable] sealed class QueueBody { public string playlist; public uint version; }

    /// <summary>
    /// Finding a match through the backend: join a playlist's queue, ask now and then how it
    /// is going, and once a server has been found for the match, get its address and a ticket
    /// to show it.
    ///
    /// Signed in, the account is used, and only then can the player queue for ranked. Signed
    /// out, a player queues as a guest: the backend hands out a short-lived guest pass that
    /// stands in for an account for casual playlists.
    /// </summary>
    public sealed class MatchmakingClient
    {
        const double RenewMargin = 60.0;

        readonly IHttpTransport _http;
        readonly Func<double> _clock;
        readonly Func<Task<string>> _accountToken;
        string _guestToken, _guestName;
        double _guestExpires;

        public string BaseUrl { get; }

        /// <param name="clock">Seconds from any steady clock.</param>
        /// <param name="accountToken">The signed-in account's access token, or null when nobody is signed in. Null to always queue as a guest.</param>
        public MatchmakingClient(IHttpTransport http, string baseUrl, Func<double> clock, Func<Task<string>> accountToken = null)
        {
            _http = http;
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            _clock = clock;
            _accountToken = accountToken;
        }

        /// <summary>Joins the queue for a mode.</summary>
        /// <param name="name">What to be called, when queuing as a guest.</param>
        /// <param name="ranked">Join the mode's ranked queue. Needs a signed-in account with a verified email.</param>
        public async Task<AccountResult<MatchTicketData>> JoinQueueAsync(GameModeKind mode, string name, bool ranked = false)
        {
            AccountResult<string> pass = await PassAsync(name);
            if (!pass.Ok) return Copy<MatchTicketData>(pass);
            Playlist playlist = Playlists.For(mode, ranked) ?? Playlists.For(mode);
            return await SendAsync<MatchTicketData>("POST", "/v1/matchmaking/tickets", new QueueBody { playlist = playlist.Id, version = NetProtocol.Version }, pass.Value);
        }

        /// <summary>How a place in the queue is doing.</summary>
        public async Task<AccountResult<MatchTicketData>> CheckAsync(string ticketId, string name)
        {
            AccountResult<string> pass = await PassAsync(name);
            if (!pass.Ok) return Copy<MatchTicketData>(pass);
            return await SendAsync<MatchTicketData>("GET", "/v1/matchmaking/tickets/" + Uri.EscapeDataString(ticketId), null, pass.Value);
        }

        /// <summary>Gives up a place in the queue.</summary>
        public async Task<AccountResult> LeaveQueueAsync(string ticketId, string name)
        {
            AccountResult<string> pass = await PassAsync(name);
            if (!pass.Ok) return pass;
            return await SendAsync<EmptyBody>("DELETE", "/v1/matchmaking/tickets/" + Uri.EscapeDataString(ticketId), null, pass.Value);
        }

        /// <summary>The token to queue with: the account's if signed in, otherwise a guest pass, fetched or renewed as needed.</summary>
        async Task<AccountResult<string>> PassAsync(string name)
        {
            if (_accountToken != null)
            {
                string token = await _accountToken();
                if (!string.IsNullOrEmpty(token)) return new AccountResult<string> { Ok = true, Status = 200, Value = token };
            }

            name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();
            if (_guestToken != null && _guestName == name && _clock() < _guestExpires - RenewMargin)
                return new AccountResult<string> { Ok = true, Status = 200, Value = _guestToken };

            AccountResult<GuestData> guest = await SendAsync<GuestData>("POST", "/v1/matchmaking/guest", new GuestBody { name = name }, null);
            if (!guest.Ok || guest.Value == null || string.IsNullOrEmpty(guest.Value.token)) return Copy<string>(guest);

            _guestToken = guest.Value.token;
            _guestName = name;
            _guestExpires = _clock() + Math.Max(120, guest.Value.expiresIn);
            return new AccountResult<string> { Ok = true, Status = guest.Status, Value = _guestToken };
        }

        Task<AccountResult<T>> SendAsync<T>(string method, string path, object body, string bearer) where T : class =>
            BackendRequest.SendAsync<T>(_http, method, BaseUrl + path, body, bearer, "matchmaking server");

        static AccountResult<T> Copy<T>(AccountResult from) => new AccountResult<T>
        {
            Ok = false,
            Status = from.Status,
            Code = from.Code,
            Message = from.Message,
            Fields = from.Fields,
        };

        /// <summary>A line for the player about a search under way.</summary>
        public static string Describe(MatchTicketData ticket, GameModeKind mode)
        {
            Playlist playlist = Playlists.Find(ticket.playlist) ?? Playlists.For(mode);
            int seconds = Math.Max(0, ticket.waitedSeconds);
            string waited = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            string others = ticket.searching <= 1 ? "Nobody else is searching yet; bots will fill any empty places soon."
                : ticket.searching + " players searching.";
            if (playlist.MinPlayers > 1 && ticket.searching <= 1) others = "Waiting for an opponent.";
            if (playlist.Ranked) others = ticket.searching + " searching. Ranked waits for a full match of players near your rating.";
            return "Finding a " + playlist.Title + " match   " + waited + "\n" + others;
        }
    }
}
