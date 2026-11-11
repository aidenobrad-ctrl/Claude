using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Client.Settings;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// What lives for as long as the game runs, whichever scene is open: the player's account,
    /// their loadouts, and keeping both in step with the backend.
    ///
    /// Created by whichever scene asks first, and kept across scene loads.
    ///
    /// Sign-ins and saved account data are kept per profile. The Editor uses its own profile,
    /// so the Editor and a build on the same computer can be signed in as two different
    /// players, which is what testing a duel on one machine needs. Start a build with
    /// -profile NAME to give it another one.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class GameServices : MonoBehaviour
    {
        public const string DefaultBackendUrl = "http://localhost:5080";

        static GameServices _instance;

        public AccountClient Account { get; private set; }
        public LoadoutLibrary Library { get; private set; }
        public LoadoutAccountSync LoadoutSync { get; private set; }
        public string Profile { get; private set; }

        /// <summary>True until the first attempt to resume a saved session has finished.</summary>
        public bool Resuming { get; private set; }

        /// <summary>A line about the account worth showing once, for example why the player was signed out.</summary>
        public event Action<string> Notice;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        /// <param name="backendUrl">Used only by whichever scene creates the services first. -backend URL on the command line wins over it.</param>
        public static GameServices Get(LoadoutCatalog catalog, string backendUrl = DefaultBackendUrl)
        {
            if (_instance != null) return _instance;

            var go = new GameObject("Game Services");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameServices>();
            _instance.Build(catalog, CommandLine("-backend") ?? backendUrl);
            return _instance;
        }

        void Build(LoadoutCatalog catalog, string backendUrl)
        {
            Profile = Application.isEditor ? "editor" : (CommandLine("-profile") ?? "default");
            string root = Application.persistentDataPath;
            string profileFolder = Path.Combine(root, "profiles", Clean(Profile));

            // Playing without an account keeps the file earlier versions used.
            var guest = new LocalFileLoadoutStore();
            var store = new SwitchableLoadoutStore(guest);
            Library = new LoadoutLibrary(catalog, store);

            Account = new AccountClient(new UnityHttpTransport(), backendUrl,
                new FileCredentialStore(Path.Combine(profileFolder, "session.txt")), () => Time.realtimeSinceStartupAsDouble);
            Account.SignedOutUnexpectedly += reason => Notice?.Invoke(reason);

            LoadoutSync = new LoadoutAccountSync(Account, Library, store, guest,
                accountId => new LocalFileLoadoutStore(Path.Combine(root, "accounts", Clean(accountId), LocalFileLoadoutStore.FileName)));

            GameSettings.Saved += () => _ = PushSettingsAsync();
            Account.Changed += () =>
            {
                if (Account.SignedIn) _ = PullSettingsAsync();
            };

            if (Account.HasSavedSession) _ = ResumeAsync();
        }

        async Task ResumeAsync()
        {
            Resuming = true;
            try
            {
                AccountResult<AccountData> result = await Account.ResumeAsync();
                if (!result.Ok && result.Offline) Notice?.Invoke("Could not reach the account server, so you are playing without your account for now.");
            }
            finally
            {
                Resuming = false;
            }
        }

        void Update()
        {
            LoadoutSync.Tick(Time.realtimeSinceStartupAsDouble);
        }

        // ------------------------------------------------------------------ settings

        /// <summary>On signing in: whichever copy of the settings is newer wins, here or on the account.</summary>
        async Task PullSettingsAsync()
        {
            AccountResult<SettingsDocument> result = await Account.GetSettingsAsync();
            if (!result.Ok || result.Value == null) return;

            SettingsDocument remote = result.Value;
            if (remote.data != null && remote.updatedAt > GameSettings.UpdatedAt)
            {
                GameSettings.Adopt(remote.data.sensitivity, remote.data.adsSensitivityMultiplier, remote.data.fieldOfView,
                    remote.data.showDamageNumbers, remote.data.masterVolume, remote.updatedAt);
            }
            else if (GameSettings.UpdatedAt > remote.updatedAt)
            {
                await PushSettingsAsync();
            }
        }

        async Task PushSettingsAsync()
        {
            if (!Account.SignedIn || GameSettings.UpdatedAt == 0) return;
            await Account.PutSettingsAsync(new SettingsDocument
            {
                updatedAt = GameSettings.UpdatedAt,
                data = new SettingsValues
                {
                    sensitivity = GameSettings.Sensitivity,
                    adsSensitivityMultiplier = GameSettings.AdsSensitivityMultiplier,
                    fieldOfView = GameSettings.FieldOfView,
                    showDamageNumbers = GameSettings.ShowDamageNumbers,
                    masterVolume = GameSettings.MasterVolume,
                },
            });
        }

        // ------------------------------------------------------------------ helpers

        static string CommandLine(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        /// <summary>Keeps a name safe to use as a folder name.</summary>
        static string Clean(string name)
        {
            var clean = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean.Append(c);
            }
            return clean.Length > 0 ? clean.ToString() : "default";
        }
    }
}
