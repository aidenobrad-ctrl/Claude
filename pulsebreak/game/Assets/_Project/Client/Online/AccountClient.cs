using System;
using System.Threading.Tasks;
using UnityEngine;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// The game's side of the accounts backend: signing up and in, staying signed in, and
    /// everything that is stored on the account.
    ///
    /// It keeps two tokens. The access token goes with every request and lasts minutes. The
    /// refresh token is swapped for a new pair when that runs out, and is saved so the player
    /// is still signed in next time. Because the backend lets each refresh token be used once,
    /// only one swap is ever in flight: requests that need one at the same moment share it.
    /// </summary>
    public sealed class AccountClient
    {
        const double RefreshMargin = 30.0;

        readonly IHttpTransport _http;
        readonly ICredentialStore _credentials;
        readonly Func<double> _clock;
        string _accessToken;
        string _refreshToken;
        double _accessExpires;
        Task<bool> _refreshing;

        /// <summary>The signed-in account, or null.</summary>
        public AccountData Account { get; private set; }
        public bool SignedIn => Account != null;
        public string BaseUrl { get; }

        /// <summary>Signed in, signed out, or the account's details changed.</summary>
        public event Action Changed;
        /// <summary>Signed out without asking, for example because the session was ended elsewhere. Argument: why, for the player.</summary>
        public event Action<string> SignedOutUnexpectedly;

        /// <param name="baseUrl">Where the backend is, for example "https://api.example.com".</param>
        /// <param name="clock">Seconds from any steady clock.</param>
        public AccountClient(IHttpTransport http, string baseUrl, ICredentialStore credentials, Func<double> clock)
        {
            _http = http;
            BaseUrl = (baseUrl ?? "").TrimEnd('/');
            _credentials = credentials;
            _clock = clock;
        }

        public bool HasSavedSession => !string.IsNullOrEmpty(_credentials.Load());

        // ------------------------------------------------------------------ signing in and out

        public async Task<AccountResult<AccountData>> RegisterAsync(string email, string username, string password)
        {
            var result = await SendAsync<SessionData>("POST", "/v1/auth/register", new RegisterBody { email = email, username = username, password = password }, false);
            return Adopt(result);
        }

        /// <param name="login">An email address or a username.</param>
        public async Task<AccountResult<AccountData>> SignInAsync(string login, string password)
        {
            var result = await SendAsync<SessionData>("POST", "/v1/auth/login", new LoginBody { login = login, password = password }, false);
            return Adopt(result);
        }

        /// <summary>Signs back in with the session saved last time, if there is one.</summary>
        public async Task<AccountResult<AccountData>> ResumeAsync()
        {
            string saved = _credentials.Load();
            if (string.IsNullOrEmpty(saved)) return Fail<AccountData>(401, "no_session", "Not signed in.");

            _refreshToken = saved;
            AccountResult<SessionData> result = await RefreshAsync();
            if (!result.Ok && !result.Offline) Forget();
            return result.Ok ? Adopt(result) : Copy<AccountData, SessionData>(result);
        }

        /// <summary>Signs out on this device. The session ends on the backend too, if it can be reached.</summary>
        public async Task SignOutAsync()
        {
            string token = _refreshToken;
            Forget();
            Changed?.Invoke();
            if (!string.IsNullOrEmpty(token)) await SendAsync<EmptyBody>("POST", "/v1/auth/logout", new RefreshBody { refreshToken = token }, false);
        }

        // ------------------------------------------------------------------ email and password

        public async Task<AccountResult> VerifyEmailAsync(string code)
        {
            var result = await SendAsync<EmptyBody>("POST", "/v1/auth/verify-email", new CodeBody { code = code }, true);
            if (result.Ok) await RefreshAccountAsync();
            return result;
        }

        public async Task<AccountResult> ResendVerificationAsync() =>
            await SendAsync<EmptyBody>("POST", "/v1/auth/resend-verification", new EmptyBody(), true);

        /// <summary>Emails a reset code, if the address has an account. The answer is the same either way.</summary>
        public async Task<AccountResult> ForgotPasswordAsync(string email) =>
            await SendAsync<EmptyBody>("POST", "/v1/auth/forgot-password", new EmailBody { email = email }, false);

        public async Task<AccountResult> ResetPasswordAsync(string email, string code, string newPassword) =>
            await SendAsync<EmptyBody>("POST", "/v1/auth/reset-password", new ResetBody { email = email, code = code, newPassword = newPassword }, false);

        public async Task<AccountResult<AccountData>> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            var result = await SendAsync<SessionData>("POST", "/v1/auth/change-password",
                new ChangePasswordBody { currentPassword = currentPassword, newPassword = newPassword }, true);
            return result.Ok ? Adopt(result) : Copy<AccountData, SessionData>(result);
        }

        /// <summary>Fetches the account again, for example to see whether the email has been verified from a link.</summary>
        public async Task<AccountResult<AccountData>> RefreshAccountAsync()
        {
            var result = await SendAsync<AccountData>("GET", "/v1/me", null, true);
            if (result.Ok && result.Value != null && SignedIn)
            {
                Account = result.Value;
                Changed?.Invoke();
            }
            return result;
        }

        // ------------------------------------------------------------------ what is stored on the account

        /// <summary>Sends every loadout held here and gets back the account's merged set.</summary>
        public Task<AccountResult<LoadoutCollection>> SyncLoadoutsAsync(LoadoutCollection mine) =>
            SendAsync<LoadoutCollection>("POST", "/v1/loadouts/sync", mine, true);

        public Task<AccountResult<SettingsDocument>> GetSettingsAsync() =>
            SendAsync<SettingsDocument>("GET", "/v1/settings", null, true);

        public Task<AccountResult<SettingsDocument>> PutSettingsAsync(SettingsDocument settings) =>
            SendAsync<SettingsDocument>("PUT", "/v1/settings", settings, true);

        /// <summary>A current access token for other parts of the game that call the backend as this account, or null if not signed in.</summary>
        public async Task<string> AccessTokenAsync()
        {
            if (!SignedIn) return null;
            if (_clock() > _accessExpires - RefreshMargin && !await EnsureFreshAsync()) return null;
            return _accessToken;
        }

        /// <summary>A ticket to show a game server so it knows who is joining.</summary>
        /// <param name="ranked">Ask for one good for ranked play. The backend refuses unless the email is verified.</param>
        public async Task<AccountResult<string>> GetTicketAsync(bool ranked)
        {
            var result = await SendAsync<TicketData>("POST", "/v1/game/tickets", new TicketBody { mode = ranked ? "ranked" : "casual" }, true);
            var ticket = Copy<string, TicketData>(result);
            ticket.Value = result.Ok && result.Value != null ? result.Value.ticket : "";
            return ticket;
        }

        // ------------------------------------------------------------------ progression

        /// <summary>Level, weapon levels, challenges, ranks and how the last match went.</summary>
        public Task<AccountResult<ProgressionData>> GetProgressionAsync() => SendAsync<ProgressionData>("GET", "/v1/progression", null, true);

        /// <summary>The most recent matchmade matches, newest first.</summary>
        public Task<AccountResult<HistoryList>> GetHistoryAsync(int count = 10) =>
            SendAsync<HistoryList>("GET", "/v1/matches?limit=" + count, null, true, list: true);

        /// <summary>The best players in a ranked playlist. Needs no sign-in.</summary>
        public Task<AccountResult<LeaderboardList>> GetLeaderboardAsync(string playlist, int count = 20) =>
            BackendRequest.SendAsync<LeaderboardList>(_http, "GET", BaseUrl + "/v1/ranked/" + Uri.EscapeDataString(playlist) + "/leaderboard?limit=" + count,
                null, null, "account server", list: true);

        // ------------------------------------------------------------------ the plumbing

        async Task<AccountResult<T>> SendAsync<T>(string method, string path, object body, bool authorized, bool list = false) where T : class
        {
            if (authorized)
            {
                if (!SignedIn) return Fail<T>(401, "unauthorized", "Sign in to do that.");
                if (_clock() > _accessExpires - RefreshMargin && !await EnsureFreshAsync()) return SignedOutResult<T>();
            }

            AccountResult<T> result = await SendOnceAsync<T>(method, path, body, authorized ? _accessToken : null, list);

            // Turned away with a token that looked fine: it may have been cut off early. One fresh try.
            if (authorized && result.Status == 401 && SignedIn)
            {
                _accessExpires = 0;
                if (!await EnsureFreshAsync()) return SignedOutResult<T>();
                result = await SendOnceAsync<T>(method, path, body, _accessToken, list);
            }
            return result;
        }

        Task<AccountResult<T>> SendOnceAsync<T>(string method, string path, object body, string bearer, bool list = false) where T : class =>
            BackendRequest.SendAsync<T>(_http, method, BaseUrl + path, body, bearer, "account server", list);

        /// <summary>Gets a new access token. Calls that arrive while one is already being fetched wait for that one.</summary>
        Task<bool> EnsureFreshAsync()
        {
            if (_refreshing != null) return _refreshing;
            _refreshing = RefreshAndAdoptAsync();
            return _refreshing;
        }

        async Task<bool> RefreshAndAdoptAsync()
        {
            try
            {
                AccountResult<SessionData> result = await RefreshAsync();
                if (result.Ok)
                {
                    Adopt(result);
                    return true;
                }
                if (result.Offline) return false;

                // The backend says this session is over: expired, ended elsewhere, or the password changed.
                Forget();
                Changed?.Invoke();
                SignedOutUnexpectedly?.Invoke(result.Code == "session_revoked"
                    ? "You were signed out because your session was used from somewhere else. Sign in again."
                    : "Your session has ended. Sign in again.");
                return false;
            }
            finally
            {
                _refreshing = null;
            }
        }

        Task<AccountResult<SessionData>> RefreshAsync() =>
            SendOnceAsync<SessionData>("POST", "/v1/auth/refresh", new RefreshBody { refreshToken = _refreshToken }, null);

        AccountResult<AccountData> Adopt(AccountResult<SessionData> result)
        {
            if (result.Ok && result.Value != null)
            {
                SessionData session = result.Value;
                _accessToken = session.accessToken;
                _refreshToken = session.refreshToken;
                _accessExpires = _clock() + Math.Max(60, session.expiresIn);
                _credentials.Save(session.refreshToken);

                bool changed = Account == null || Account.id != session.account.id || Account.emailVerified != session.account.emailVerified ||
                               Account.username != session.account.username;
                Account = session.account;
                if (changed) Changed?.Invoke();
            }

            var adopted = Copy<AccountData, SessionData>(result);
            adopted.Value = result.Value?.account;
            return adopted;
        }

        void Forget()
        {
            Account = null;
            _accessToken = null;
            _refreshToken = null;
            _accessExpires = 0;
            _credentials.Clear();
        }

        AccountResult<T> SignedOutResult<T>() => SignedIn
            ? Fail<T>(0, "offline", "Could not reach the account server. Check your connection and try again.")
            : Fail<T>(401, "unauthorized", "Your session has ended. Sign in again.");

        static AccountResult<T> Fail<T>(int status, string code, string message) =>
            new AccountResult<T> { Ok = false, Status = status, Code = code, Message = message };

        static AccountResult<TOut> Copy<TOut, TIn>(AccountResult<TIn> from) => new AccountResult<TOut>
        {
            Ok = from.Ok,
            Status = from.Status,
            Code = from.Code,
            Message = from.Message,
            Fields = from.Fields,
        };
    }
}
