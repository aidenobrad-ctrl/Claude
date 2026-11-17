using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Pulsebreak.Client.Online
{
    /// <summary>Sends the account client's requests with Unity's own web requests.</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        const int TimeoutSeconds = 15;

        public Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearerToken)
        {
            var done = new TaskCompletionSource<HttpResult>();
            var request = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(bearerToken)) request.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            // Finishes on the main thread, so whoever awaits this carries on there too.
            request.SendWebRequest().completed += _ =>
            {
                bool answered = request.result == UnityWebRequest.Result.Success || request.result == UnityWebRequest.Result.ProtocolError;
                var result = new HttpResult
                {
                    Status = answered ? (int)request.responseCode : 0,
                    Body = request.downloadHandler != null ? request.downloadHandler.text : "",
                    NetworkError = answered ? null : request.error,
                };
                request.Dispose();
                done.SetResult(result);
            };
            return done.Task;
        }
    }
}
