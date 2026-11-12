using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// One request to the backend, and its answer turned into a result the game can show:
    /// the reply read from JSON on success, the backend's own error message on failure, and a
    /// plain sentence when the backend could not be reached at all.
    /// </summary>
    static class BackendRequest
    {
        /// <param name="what">What the backend is called in messages to the player, such as "account server".</param>
        public static async Task<AccountResult<T>> SendAsync<T>(IHttpTransport http, string method, string url, object body, string bearer,
            string what) where T : class
        {
            HttpResult reply;
            try
            {
                reply = await http.SendAsync(method, url, body != null ? JsonUtility.ToJson(body) : null, bearer);
            }
            catch (Exception failure)
            {
                reply = new HttpResult { NetworkError = failure.Message };
            }

            if (reply.Status == 0)
                return Fail<T>(0, "offline", "Could not reach the " + what + ". Check your connection and try again.");

            if (reply.Status >= 200 && reply.Status < 300)
            {
                var ok = new AccountResult<T> { Ok = true, Status = reply.Status };
                if (!string.IsNullOrEmpty(reply.Body) && typeof(T) != typeof(EmptyBody))
                {
                    try
                    {
                        ok.Value = JsonUtility.FromJson<T>(reply.Body);
                    }
                    catch (Exception)
                    {
                        return Fail<T>(reply.Status, "bad_reply", "The " + what + " sent something the game could not read.");
                    }
                }
                return ok;
            }

            ApiErrorData error = null;
            try
            {
                if (!string.IsNullOrEmpty(reply.Body)) error = JsonUtility.FromJson<ErrorEnvelope>(reply.Body)?.error;
            }
            catch (Exception)
            {
                // Not one of ours: a proxy's error page, perhaps. The status says enough.
            }
            var failed = Fail<T>(reply.Status, error?.code ?? "http_" + reply.Status,
                !string.IsNullOrEmpty(error?.message) ? error.message : "The " + what + " answered with an error (" + reply.Status + ").");
            if (error?.fields != null) failed.Fields = error.fields;
            return failed;
        }

        public static AccountResult<T> Fail<T>(int status, string code, string message) =>
            new AccountResult<T> { Ok = false, Status = status, Code = code, Message = message };
    }
}
