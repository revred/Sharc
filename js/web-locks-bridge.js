// Web Locks API bridge — cross-tab write coordination for Sharc databases.
// Uses navigator.locks.request() for shared/exclusive locking on database names.
// BroadcastChannel notifies other tabs after commits for cache invalidation.

window.webLocksBridge = {
    _channels: new Map(),

    /// Acquires an exclusive write lock on a database. Returns when lock is acquired.
    /// The lock is held until releaseLock() is called.
    /// Uses AbortController to enable lock release from C#.
    acquireWriteLock: async function (dbName) {
        const lockName = `sharc:${dbName}`;
        const controller = new AbortController();
        const lockId = `lock_${Date.now()}_${Math.random().toString(36).slice(2)}`;

        // Store controller for later release
        if (!this._activeLocks) this._activeLocks = new Map();

        const lockPromise = new Promise((resolve, reject) => {
            navigator.locks.request(lockName, { mode: 'exclusive', signal: controller.signal }, (lock) => {
                // Lock acquired — resolve and hold until abort
                resolve(lockId);
                return new Promise((releaseLock) => {
                    this._activeLocks.set(lockId, { releaseLock, controller, dbName });
                });
            }).catch(err => {
                if (err.name === 'AbortError') return; // expected on release
                reject(err);
            });
        });

        return await lockPromise;
    },

    /// Acquires a shared read lock on a database.
    acquireReadLock: async function (dbName) {
        const lockName = `sharc:${dbName}`;
        const controller = new AbortController();
        const lockId = `rlock_${Date.now()}_${Math.random().toString(36).slice(2)}`;

        if (!this._activeLocks) this._activeLocks = new Map();

        const lockPromise = new Promise((resolve, reject) => {
            navigator.locks.request(lockName, { mode: 'shared', signal: controller.signal }, (lock) => {
                resolve(lockId);
                return new Promise((releaseLock) => {
                    this._activeLocks.set(lockId, { releaseLock, controller, dbName });
                });
            }).catch(err => {
                if (err.name === 'AbortError') return;
                reject(err);
            });
        });

        return await lockPromise;
    },

    /// Releases a previously acquired lock.
    releaseLock: function (lockId) {
        if (!this._activeLocks) return;
        const entry = this._activeLocks.get(lockId);
        if (entry) {
            entry.releaseLock();
            this._activeLocks.delete(lockId);
        }
    },

    /// Broadcasts a commit notification to other tabs.
    broadcastCommit: function (dbName, dataVersion) {
        const channel = this._getChannel(dbName);
        channel.postMessage({ type: 'commit', dbName, dataVersion, tabId: this._tabId() });
    },

    /// Registers a callback for commit notifications from other tabs.
    /// Returns a subscription ID for later cleanup.
    onCommit: function (dbName, dotNetObjRef, methodName) {
        const channel = this._getChannel(dbName);
        const handler = (event) => {
            if (event.data.type === 'commit' && event.data.tabId !== this._tabId()) {
                dotNetObjRef.invokeMethodAsync(methodName, event.data.dataVersion);
            }
        };
        channel.addEventListener('message', handler);
        const subId = `sub_${Date.now()}`;
        if (!this._subs) this._subs = new Map();
        this._subs.set(subId, { channel, handler });
        return subId;
    },

    /// Removes a commit notification subscription.
    unsubscribe: function (subId) {
        if (!this._subs) return;
        const entry = this._subs.get(subId);
        if (entry) {
            entry.channel.removeEventListener('message', entry.handler);
            this._subs.delete(subId);
        }
    },

    /// Checks if Web Locks API is available.
    isSupported: function () {
        return typeof navigator.locks !== 'undefined'
            && typeof BroadcastChannel !== 'undefined';
    },

    // ── Internal helpers ──

    _getChannel: function (dbName) {
        const key = `sharc:${dbName}`;
        if (!this._channels.has(key)) {
            this._channels.set(key, new BroadcastChannel(key));
        }
        return this._channels.get(key);
    },

    _tabId: function () {
        if (!this._cachedTabId) {
            this._cachedTabId = `tab_${Date.now()}_${Math.random().toString(36).slice(2)}`;
        }
        return this._cachedTabId;
    }
};
