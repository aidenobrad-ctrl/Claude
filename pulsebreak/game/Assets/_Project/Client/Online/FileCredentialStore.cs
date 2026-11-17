using System;
using System.IO;
using UnityEngine;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// Keeps the refresh token in a file, so a player stays signed in between launches.
    ///
    /// The file is a credential: anyone who copies it can sign in as that player until the
    /// session is ended (signing out, "sign out everywhere", or a password change). It lives
    /// in the user's own application data folder, like a browser's cookies do.
    /// </summary>
    public sealed class FileCredentialStore : ICredentialStore
    {
        public string FilePath { get; }

        public FileCredentialStore(string filePath)
        {
            FilePath = filePath;
        }

        public string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string token = File.ReadAllText(FilePath).Trim();
                return token.Length > 0 && token.Length <= 128 ? token : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Save(string refreshToken)
        {
            try
            {
                string folder = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, refreshToken);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
            }
            catch (Exception error)
            {
                Debug.LogWarning("Pulsebreak: could not remember the sign-in (" + error.Message + "). You will need to sign in again next time.");
            }
        }

        public void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception)
            {
                // Nothing more to do; the session has been ended on the backend anyway.
            }
        }
    }
}
