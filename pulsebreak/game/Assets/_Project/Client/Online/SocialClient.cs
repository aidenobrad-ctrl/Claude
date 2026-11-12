using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// The signed-in player's friends, party and party chat. The game checks in every few
    /// seconds (less often in a match), which also tells friends where the player is; each
    /// action is sent straight away and followed by a fresh check-in.
    ///
    /// New things raise events: a party chat line, an invite, the party leader starting a
    /// search that this player should follow into a match.
    /// </summary>
    public sealed class SocialClient
    {
        public const string Menu = "menu", Match = "match", Range = "range";
        const double MenuInterval = 3.0, BusyInterval = 8.0;

        readonly AccountClient _account;
        readonly HashSet<string> _seenInvites = new HashSet<string>();
        long _cursor;
        double _next;
        bool _polling;
        string _following = "";

        /// <summary>Where the player is, as friends see it: <see cref="Menu"/>, <see cref="Match"/> or <see cref="Range"/>.</summary>
        public string Status = Menu;

        /// <summary>The last check-in's answer. Null when signed out or before the first one.</summary>
        public SocialData Current { get; private set; }

        public event Action Changed;
        public event Action<ChatLineData> PartyChat;
        public event Action<InviteData> Invited;
        /// <summary>
        /// The party leader started a search: everyone in the party follows its ticket into the
        /// match. Raised for members only (the leader started it), and once per search.
        /// </summary>
        public event Action<PartySearchData> PartySearchStarted;

        public SocialClient(AccountClient account)
        {
            _account = account;
            _account.Changed += () =>
            {
                Current = null;
                _cursor = 0;
                _seenInvites.Clear();
                _following = "";
                _next = 0;
                Changed?.Invoke();
            };
        }

        public string MyId => _account.SignedIn && _account.Account != null ? _account.Account.id : "";

        public PartyData Party => Current != null && Current.party != null && !string.IsNullOrEmpty(Current.party.id) ? Current.party : null;
        public bool InParty => Party != null && Party.members.Length > 1;
        public bool IsLeader => Party == null || Party.leaderId == MyId;

        public bool IsFriend(string accountId) => Find(Current?.friends, accountId) != null;
        public bool IsBlocked(string accountId) => Find(Current?.blocked, accountId) != null;
        public bool HasAsked(string accountId) => Find(Current?.outgoing, accountId) != null || Find(Current?.incoming, accountId) != null;

        /// <summary>Checks in when it is time. Call every frame.</summary>
        public void Tick(double now)
        {
            if (!_account.SignedIn || _polling || now < _next) return;
            _next = now + (Status == Menu ? MenuInterval : BusyInterval);
            _ = PollAsync();
        }

        /// <summary>Checks in now.</summary>
        public async Task<AccountResult<SocialData>> PollAsync()
        {
            if (!_account.SignedIn) return new AccountResult<SocialData> { Ok = false, Code = "unauthorized", Message = "Sign in to see your friends." };
            _polling = true;
            AccountResult<SocialData> result;
            try
            {
                result = await _account.CallAsync<SocialData>("POST", "/v1/social/poll", new PollBody { status = Status, chatAfter = _cursor });
            }
            finally
            {
                _polling = false;
            }
            if (!result.Ok || result.Value == null || !_account.SignedIn) return result;

            SocialData data = result.Value;
            Current = data;
            _cursor = Math.Max(_cursor, data.chatCursor);
            Changed?.Invoke();

            if (data.chat != null)
            {
                foreach (ChatLineData line in data.chat) PartyChat?.Invoke(line);
            }
            if (data.invites != null)
            {
                foreach (InviteData invite in data.invites)
                {
                    if (_seenInvites.Add(invite.partyId + "|" + invite.sentAt)) Invited?.Invoke(invite);
                }
            }

            PartyData party = Party;
            string search = party != null && party.search != null ? party.search.ticketId ?? "" : "";
            // A search still running, or one that found its match a moment ago: join in. One
            // that finished long ago (the game was restarted since) is left alone.
            bool fresh = search.Length > 0 && (party.search.status == "searching" ||
                                               DateTimeOffset.UtcNow.ToUnixTimeSeconds() - party.search.startedAt < 90);
            if (fresh && search != _following && !IsLeader)
            {
                _following = search;
                PartySearchStarted?.Invoke(party.search);
            }
            else if (search.Length == 0)
            {
                _following = "";
            }
            return result;
        }

        // ------------------------------------------------------------------ actions

        public Task<AccountResult> AddFriendAsync(string username) => Act("POST", "/v1/friends", new UsernameBody { username = (username ?? "").Trim() });
        public Task<AccountResult> AcceptFriendAsync(string accountId) => Act("POST", "/v1/friends/" + Esc(accountId) + "/accept", new EmptyBody());
        /// <summary>Unfriends, declines a request, or takes one back.</summary>
        public Task<AccountResult> RemoveFriendAsync(string accountId) => Act("DELETE", "/v1/friends/" + Esc(accountId), null);
        public Task<AccountResult> BlockAsync(string accountId) => Act("POST", "/v1/blocks/" + Esc(accountId), new EmptyBody());
        public Task<AccountResult> UnblockAsync(string accountId) => Act("DELETE", "/v1/blocks/" + Esc(accountId), null);
        public Task<AccountResult> ReportAsync(ReportBody report) => Act("POST", "/v1/reports", report, poll: false);
        public Task<AccountResult> InviteAsync(string accountId) => Act("POST", "/v1/party/invites", new AccountIdBody { accountId = accountId });
        public Task<AccountResult> JoinPartyAsync(string partyId) => Act("POST", "/v1/party/invites/" + Esc(partyId) + "/accept", new EmptyBody());
        public Task<AccountResult> DeclineInviteAsync(string partyId) => Act("DELETE", "/v1/party/invites/" + Esc(partyId), null);
        public Task<AccountResult> LeavePartyAsync() => Act("POST", "/v1/party/leave", new EmptyBody());
        public Task<AccountResult> KickAsync(string accountId) => Act("POST", "/v1/party/members/" + Esc(accountId) + "/kick", new EmptyBody());
        public Task<AccountResult> SayAsync(string text) => Act("POST", "/v1/party/chat", new TextBody { text = text });

        async Task<AccountResult> Act(string method, string path, object body, bool poll = true)
        {
            AccountResult<EmptyBody> result = await _account.CallAsync<EmptyBody>(method, path, body);
            if (poll && result.Ok) await PollAsync();
            return result;
        }

        static string Esc(string id) => Uri.EscapeDataString(id ?? "");

        static PersonData Find(PersonData[] list, string accountId)
        {
            if (list == null || string.IsNullOrEmpty(accountId)) return null;
            foreach (PersonData person in list)
            {
                if (person.accountId == accountId) return person;
            }
            return null;
        }
    }
}
