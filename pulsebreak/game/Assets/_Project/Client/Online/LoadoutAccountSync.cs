using System;
using System.Threading.Tasks;
using Pulsebreak.Client.Loadouts;
using Pulsebreak.Shared.Loadouts;

namespace Pulsebreak.Client.Online
{
    /// <summary>
    /// Keeps the loadout library and the player's account in step.
    ///
    /// Without an account, loadouts live in one file on this computer, as before. Signing in
    /// switches the library to that account's loadouts: first the copy kept on this computer
    /// from last time, so they are there at once even offline, then whatever the backend has.
    /// Any loadouts made before having an account move onto it the first time.
    ///
    /// After that, every change is sent a moment later, and what comes back is merged in.
    /// If the backend cannot be reached, changes stay on this computer and go up next time.
    /// </summary>
    public sealed class LoadoutAccountSync
    {
        /// <summary>How long after a change to wait before sending it, so a burst of edits goes as one.</summary>
        public const double Delay = 1.5;
        const double RetryDelay = 20.0;

        readonly AccountClient _account;
        readonly LoadoutLibrary _library;
        readonly SwitchableLoadoutStore _store;
        readonly ILoadoutStore _guestStore;
        readonly Func<string, ILoadoutStore> _accountStore;
        string _accountId;
        bool _dirty, _syncing, _applying;
        double _dueAt = double.MaxValue;
        // The time Tick last passed in; changes between ticks are scheduled from it.
        double _now;

        /// <summary>A line for the loadout screen: where the loadouts are, and whether the last sync worked.</summary>
        public string Status { get; private set; } = "Saved on this computer";
        public event Action StatusChanged;

        /// <param name="store">The library's store, which this switches between the guest file and each account's file.</param>
        /// <param name="guestStore">Where loadouts live without an account.</param>
        /// <param name="accountStore">Where this computer keeps its copy of an account's loadouts, by account id.</param>
        public LoadoutAccountSync(AccountClient account, LoadoutLibrary library, SwitchableLoadoutStore store,
            ILoadoutStore guestStore, Func<string, ILoadoutStore> accountStore)
        {
            _account = account;
            _library = library;
            _store = store;
            _guestStore = guestStore;
            _accountStore = accountStore;

            _account.Changed += OnAccountChanged;
            _library.Changed += OnLibraryChanged;
            OnAccountChanged();
        }

        /// <summary>Call every frame (or often). Sends pending changes when they are due.</summary>
        public void Tick(double now)
        {
            _now = now;
            if (_dirty && !_syncing && now >= _dueAt) _ = SyncAsync(now);
        }

        /// <summary>Sends now, without waiting. Returns when the backend has answered (or not).</summary>
        public Task SyncNowAsync(double now) => SyncAsync(now);

        // ------------------------------------------------------------------ switching accounts

        void OnAccountChanged()
        {
            string id = _account.SignedIn ? _account.Account.id : null;
            if (id == _accountId) return;
            _accountId = id;
            _dirty = false;

            _applying = true;
            try
            {
                if (id == null)
                {
                    _store.Target = _guestStore;
                    _library.MergeFrom(_guestStore.Load() ?? new LoadoutCollection(), replace: true);
                    SetStatus("Saved on this computer");
                    return;
                }

                // This account's own copy first: everything it had is available straight away.
                ILoadoutStore accountStore = _accountStore(id);
                _store.Target = accountStore;
                _library.MergeFrom(accountStore.Load() ?? new LoadoutCollection(), replace: true);

                // Loadouts made without an account move onto the first account to sign in.
                LoadoutCollection guest = _guestStore.Load();
                if (guest != null && guest.loadouts != null && guest.loadouts.Length > 0)
                {
                    _library.MergeFrom(guest);
                    _guestStore.Save(new LoadoutCollection());
                }
            }
            finally
            {
                _applying = false;
            }

            // And then whatever the backend has.
            _dirty = true;
            _dueAt = 0;
            SetStatus("Syncing with your account...");
        }

        void OnLibraryChanged()
        {
            if (_applying || _accountId == null) return;
            _dirty = true;
            _dueAt = Math.Min(_dueAt, _now + Delay);
            if (!_syncing) SetStatus("Saving to your account...");
        }

        async Task SyncAsync(double now)
        {
            _now = now;
            if (_syncing || _accountId == null) return;
            string account = _accountId;
            _syncing = true;
            _dirty = false;
            _dueAt = double.MaxValue;

            AccountResult<LoadoutCollection> result;
            try
            {
                result = await _account.SyncLoadoutsAsync(_library.Snapshot());
            }
            finally
            {
                _syncing = false;
            }

            // Signed out or switched account while this was in flight: the answer belongs to someone else.
            if (account != _accountId) return;

            if (result.Ok && result.Value != null)
            {
                _applying = true;
                try
                {
                    _library.MergeFrom(result.Value);
                }
                finally
                {
                    _applying = false;
                }
                SetStatus(_dirty ? "Saving to your account..." : "Saved to your account");
                if (_dirty) _dueAt = now + Delay;
            }
            else
            {
                // Kept here, tried again later.
                _dirty = true;
                _dueAt = now + RetryDelay;
                SetStatus(result.Offline
                    ? "Offline: changes are kept on this computer"
                    : "Could not save to your account: " + result.Message);
            }
        }

        void SetStatus(string status)
        {
            if (status == Status) return;
            Status = status;
            StatusChanged?.Invoke();
        }
    }
}
