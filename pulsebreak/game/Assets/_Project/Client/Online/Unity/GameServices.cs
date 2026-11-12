using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Pulsebreak.Client.Cosmetics;
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

        /// <summary>
        /// Accounts are on: the menu offers sign-in, and a signed-in player's loadouts,
        /// settings, progression and ranks follow their account. Playing without an account
        /// still works for casual matches (as a guest) and for everything offline. Set this to
        /// false to keep everything on this computer, as during Phases 5 to 7.
        /// </summary>
        public static readonly bool AccountsEnabled = true;

        static GameServices _instance;

        public AccountClient Account { get; private set; }
        /// <summary>Finding matches through the backend. Works signed in or not.</summary>
        public MatchmakingClient Matchmaking { get; private set; }
        public LoadoutLibrary Library { get; private set; }
        /// <summary>How the player looks: suit, headgear, weapon skins.</summary>
        public CosmeticsLibrary Cosmetics { get; private set; }
        /// <summary>Null when accounts are switched off (<see cref="AccountsEnabled"/>).</summary>
        public LoadoutAccountSync LoadoutSync { get; private set; }
        public string Profile { get; private set; }

        /// <summary>True until the first attempt to resume a saved session has finished.</summary>
        public bool Resuming { get; private set; }

        /// <summary>A line about the account worth showing once, for example why the player was signed out.</summary>
        public event Action<string> Notice;
        /// <summary>The attempt to resume a saved session has finished, whether or not it worked.</summary>
        public event Action ResumeFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
            CosmeticsLibrary.Local = null;
        }

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
            // The game lists only the player's own loadouts; a first run starts with one.
            Library.EnsureOwnLoadout();
            Cosmetics = new CosmeticsLibrary();
            CosmeticsLibrary.Local = Cosmetics;

            var http = new UnityHttpTransport();
            Account = new AccountClient(http, backendUrl,
                new FileCredentialStore(Path.Combine(profileFolder, "session.txt")), () => Time.realtimeSinceStartupAsDouble);
            Matchmaking = new MatchmakingClient(http, backendUrl, () => Time.realtimeSinceStartupAsDouble,
                AccountsEnabled ? (Func<Task<string>>)Account.AccessTokenAsync : null);
            if (!AccountsEnabled)
            {
                // Never signed in, never syncs. Loadouts that went onto an account while accounts
                // were on come back to this computer, so nothing made then is lost.
                RecoverAccountLoadouts(root, guest);
                return;
            }
            Account.SignedOutUnexpectedly += reason => Notice?.Invoke(reason);

            LoadoutSync = new LoadoutAccountSync(Account, Library, store, guest,
                accountId => new LocalFileLoadoutStore(Path.Combine(root, "accounts", Clean(accountId), LocalFileLoadoutStore.FileName)));

            GameSettings.Saved += () => _ = PushSettingsAsync();
            Account.Changed += () =>
            {
                if (Account.SignedIn) _ = RefreshProgressionAsync();
                else ProgressionCache.Set(null);
                if (Account.SignedIn) _ = PullSettingsAsync();
            };

            if (Account.HasSavedSession) _ = ResumeAsync();
        }

        /// <summary>Fetches the player's level, challenges and ranks into <see cref="ProgressionCache"/>. Does nothing signed out.</summary>
        public async Task<ProgressionData> RefreshProgressionAsync()
        {
            if (!Account.SignedIn) return null;
            try
            {
                AccountResult<ProgressionData> result = await Account.GetProgressionAsync();
                if (result.Ok && result.Value != null && Account.SignedIn) ProgressionCache.Set(result.Value);
                return result.Ok ? result.Value : null;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Pulsebreak: could not fetch progression: " + error.Message);
                return null;
            }
        }

        async Task ResumeAsync()
        {
            Resuming = true;
            AccountResult<AccountData> result;
            try
            {
                result = await Account.ResumeAsync();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                result = new AccountResult<AccountData>();
            }
            finally
            {
                Resuming = false;
            }

            ResumeFinished?.Invoke();
            if (!result.Ok && result.Offline) Notice?.Invoke("Could not reach the account server, so you are playing without your account for now.");
            else if (!result.Ok) Notice?.Invoke("Your saved sign-in has ended. Sign in again.");
        }

        void Update()
        {
            LoadoutSync?.Tick(Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>
        /// Signing in moved the loadouts made without an account onto that account. With
        /// accounts switched off (as they were in Phase 5), the newest copy kept on this computer of any account's
        /// loadouts is merged back into the ones used without an account.
        /// </summary>
        void RecoverAccountLoadouts(string root, LocalFileLoadoutStore guest)
        {
            string folder = Path.Combine(root, "accounts");
            if (!Directory.Exists(folder)) return;
            try
            {
                foreach (string file in Directory.GetFiles(folder, LocalFileLoadoutStore.FileName, SearchOption.AllDirectories))
                {
                    LoadoutCollection kept = new LocalFileLoadoutStore(file).Load();
                    if (kept != null && kept.loadouts != null && kept.loadouts.Length > 0) Library.MergeFrom(kept);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("Pulsebreak: could not bring back loadouts kept for an account (" + error.Message + ").");
            }
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
