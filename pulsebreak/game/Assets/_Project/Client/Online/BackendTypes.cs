using System;
using System.Threading.Tasks;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Online
{
    /// <summary>What came back from one HTTP request.</summary>
    public struct HttpResult
    {
        /// <summary>The HTTP status, or 0 if no answer arrived at all.</summary>
        public int Status;
        public string Body;
        /// <summary>Why nothing arrived (no network, server down, timed out). Null when there was an answer.</summary>
        public string NetworkError;
    }

    /// <summary>
    /// Sends HTTP requests. The game uses Unity's web requests; tests use .NET's own client.
    /// Everything above this is the same either way.
    /// </summary>
    public interface IHttpTransport
    {
        /// <param name="jsonBody">Null for a request with no body.</param>
        /// <param name="bearerToken">Null for a request made without signing in.</param>
        Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearerToken);
    }

    // ------------------------------------------------------------------ what the backend sends and expects
    // Field names match the backend's JSON exactly, which is what Unity's JSON reader needs.

    [Serializable]
    public sealed class ProfileData
    {
        public int level;
        public int xp;
        public int currency;
        public int rating;
    }

    [Serializable]
    public sealed class AccountData
    {
        public string id = "";
        public string username = "";
        public string email = "";
        public bool emailVerified;
        public long createdAt;
        public ProfileData profile = new ProfileData();
    }

    [Serializable]
    public sealed class SessionData
    {
        public string accessToken = "";
        public int expiresIn;
        public string refreshToken = "";
        public AccountData account = new AccountData();
    }

    [Serializable]
    public sealed class FieldErrorData
    {
        public string field = "";
        public string message = "";
    }

    [Serializable]
    public sealed class ApiErrorData
    {
        public string code = "";
        public string message = "";
        public FieldErrorData[] fields = new FieldErrorData[0];
    }

    [Serializable]
    sealed class ErrorEnvelope
    {
        public ApiErrorData error;
    }

    [Serializable]
    public sealed class SettingsValues
    {
        public float sensitivity;
        public float adsSensitivityMultiplier;
        public float fieldOfView;
        public bool showDamageNumbers;
        public float masterVolume;
    }

    [Serializable]
    public sealed class SettingsDocument
    {
        /// <summary>May come back empty or default when nothing has been saved; check <see cref="updatedAt"/>.</summary>
        public SettingsValues data;
        /// <summary>When these settings were last changed, seconds since 1970. 0 means nothing has been saved.</summary>
        public long updatedAt;
    }

    [Serializable]
    sealed class TicketData
    {
        public string ticket = "";
        public int expiresIn;
    }

    [Serializable] sealed class RegisterBody { public string email, username, password; }
    [Serializable] sealed class LoginBody { public string login, password; }
    [Serializable] sealed class RefreshBody { public string refreshToken; }
    [Serializable] sealed class CodeBody { public string code; }
    [Serializable] sealed class EmailBody { public string email; }
    [Serializable] sealed class ResetBody { public string email, code, newPassword; }
    [Serializable] sealed class ChangePasswordBody { public string currentPassword, newPassword; }
    [Serializable] sealed class TicketBody { public string mode; }
    [Serializable] sealed class EmptyBody { }

    /// <summary>How a request to the backend went.</summary>
    public class AccountResult
    {
        public bool Ok;
        /// <summary>The HTTP status, or 0 when the backend could not be reached.</summary>
        public int Status;
        /// <summary>The backend's error code, for example "invalid_credentials". "offline" when it could not be reached.</summary>
        public string Code = "";
        /// <summary>A sentence for the player.</summary>
        public string Message = "";
        public FieldErrorData[] Fields = new FieldErrorData[0];

        public bool Offline => Status == 0;

        /// <summary>What the backend said about one field of the form, or null.</summary>
        public string FieldMessage(string field)
        {
            foreach (FieldErrorData error in Fields)
            {
                if (error != null && error.field == field) return error.message;
            }
            return null;
        }

        /// <summary>The first problem worth showing: a field's message if there is one, otherwise the general one.</summary>
        public string FirstMessage => Fields.Length > 0 && Fields[0] != null && !string.IsNullOrEmpty(Fields[0].message) ? Fields[0].message : Message;
    }

    public sealed class AccountResult<T> : AccountResult
    {
        public T Value;
    }

    /// <summary>Where a signed-in player's session is remembered between launches.</summary>
    public interface ICredentialStore
    {
        /// <summary>The saved refresh token, or null.</summary>
        string Load();
        void Save(string refreshToken);
        void Clear();
    }

    public sealed class MemoryCredentialStore : ICredentialStore
    {
        public string Token;
        public string Load() => Token;
        public void Save(string refreshToken) => Token = refreshToken;
        public void Clear() => Token = null;
    }

    /// <summary>A loadout store that can be pointed at a different place: one file for playing without an account, another per account.</summary>
    public sealed class SwitchableLoadoutStore : Loadouts.ILoadoutStore
    {
        public Loadouts.ILoadoutStore Target;

        public SwitchableLoadoutStore(Loadouts.ILoadoutStore target)
        {
            Target = target;
        }

        public LoadoutCollection Load() => Target.Load();
        public void Save(LoadoutCollection collection) => Target.Save(collection);
    }
}
