// IndexedDB adapter — browser-native storage API, no download.
// All timing uses performance.now() inside JS (excludes IJSRuntime interop overhead).

window.indexedDbAdapter = {
    _db: null,
    _dbName: 'sharc-arena',

    // Open (or create) the IndexedDB database
    init: async function () {
        const t0 = performance.now();
        if (this._db) {
            this._db.close();
            this._db = null;
        }
        await this._deleteIfExists();
        this._db = await this._openDb();
        const t1 = performance.now();
        return { ms: t1 - t0 };
    },

    // Create object stores and populate with data
    // storeConfigs: [{ name, keyPath, data: [...rows] }]
    createStores: async function (storeConfigs) {
        // Close current DB to modify schema
        if (this._db) this._db.close();

        const t0 = performance.now();
        const version = Date.now(); // force upgrade
        this._db = await new Promise((resolve, reject) => {
            const request = indexedDB.open(this._dbName, version);
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                for (const config of storeConfigs) {
                    if (!db.objectStoreNames.contains(config.name)) {
                        db.createObjectStore(config.name, { keyPath: config.keyPath });
                    }
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });

        // Populate each store
        for (const config of storeConfigs) {
            if (config.data && config.data.length > 0) {
                await this._bulkPut(config.name, config.data);
            }
        }

        const t1 = performance.now();
        return { ms: t1 - t0, storeCount: storeConfigs.length };
    },

    // Schema read — just list object store names
    schemaRead: function () {
        const t0 = performance.now();
        const names = Array.from(this._db.objectStoreNames);
        const t1 = performance.now();
        return { ms: t1 - t0, storeCount: names.length };
    },

    // Full cursor scan over an object store
    sequentialScan: async function (storeName) {
        const t0 = performance.now();
        let count = 0;
        try {
            await new Promise((resolve, reject) => {
                const tx = this._db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const request = store.openCursor();
                request.onsuccess = (event) => {
                    const cursor = event.target.result;
                    if (cursor) {
                        const _val = cursor.value; // force decode
                        count++;
                        cursor.continue();
                    } else {
                        resolve();
                    }
                };
                request.onerror = (e) => {
                    reject(e.target.error);
                };
            });
        } catch (err) {
            throw err;
        }
        const t1 = performance.now();
        return { ms: t1 - t0, rowCount: count };
    },

    // Point lookup by key
    pointLookup: async function (storeName, key) {
        const t0 = performance.now();
        const result = await new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const request = store.get(key);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        const t1 = performance.now();
        return { ms: t1 - t0, found: result !== undefined };
    },

    // Batch lookups — N sequential gets in one transaction
    batchLookup: async function (storeName, keys) {
        const t0 = performance.now();
        await new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            let completed = 0;
            for (const key of keys) {
                const request = store.get(key);
                request.onsuccess = () => {
                    completed++;
                    if (completed === keys.length) resolve();
                };
                request.onerror = () => reject(request.error);
            }
            if (keys.length === 0) resolve();
        });
        const t1 = performance.now();
        return { ms: t1 - t0, count: keys.length };
    },

    // Type decode — scan a single "column" (property) from all objects
    typeDecode: async function (storeName, property) {
        const t0 = performance.now();
        let count = 0;
        await new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const request = store.openCursor();
            request.onsuccess = (event) => {
                const cursor = event.target.result;
                if (cursor) {
                    const _val = cursor.value[property];
                    count++;
                    cursor.continue();
                } else {
                    resolve();
                }
            };
            request.onerror = () => reject(request.error);
        });
        const t1 = performance.now();
        return { ms: t1 - t0, rowCount: count };
    },

    // NULL scan
    nullScan: async function (storeName, property) {
        const t0 = performance.now();
        let nullCount = 0;
        let total = 0;
        await new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const request = store.openCursor();
            request.onsuccess = (event) => {
                const cursor = event.target.result;
                if (cursor) {
                    if (cursor.value[property] === null || cursor.value[property] === undefined) {
                        nullCount++;
                    }
                    total++;
                    cursor.continue();
                } else {
                    resolve();
                }
            };
            request.onerror = () => reject(request.error);
        });
        const t1 = performance.now();
        return { ms: t1 - t0, nullCount: nullCount, totalCount: total };
    },

    // 2-hop BFS graph traversal via cursor scan + JS filter
    graphTraverse: async function (storeName, startKey, originProp, targetProp) {
        const t0 = performance.now();

        // Hop 1: find all edges where origin == startKey
        const hop1Targets = [];
        await new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const request = store.openCursor();
            request.onsuccess = (event) => {
                const cursor = event.target.result;
                if (cursor) {
                    if (cursor.value[originProp] === startKey) {
                        hop1Targets.push(cursor.value[targetProp]);
                    }
                    cursor.continue();
                } else {
                    resolve();
                }
            };
            request.onerror = () => reject(request.error);
        });

        // Hop 2: for each hop-1 target, count edges from that node
        const hop1Set = new Set(hop1Targets);
        let hop2Count = 0;
        for (const target of hop1Set) {
            await new Promise((resolve, reject) => {
                const tx = this._db.transaction(storeName, 'readonly');
                const store = tx.objectStore(storeName);
                const request = store.openCursor();
                request.onsuccess = (event) => {
                    const cursor = event.target.result;
                    if (cursor) {
                        if (cursor.value[originProp] === target) {
                            hop2Count++;
                        }
                        cursor.continue();
                    } else {
                        resolve();
                    }
                };
                request.onerror = () => reject(request.error);
            });
        }

        const t1 = performance.now();
        return { ms: t1 - t0, hop1Count: hop1Set.size, hop2Count: hop2Count };
    },

    // Get row count
    getRowCount: async function (storeName) {
        return new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const request = store.count();
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    },

    // Memory: report via performance.memory if available (Chrome only)
    getMemory: function () {
        if (performance.memory) {
            return { usedKB: performance.memory.usedJSHeapSize / 1024 };
        }
        return { usedKB: -1 };
    },

    // Cleanup
    destroy: async function () {
        if (this._db) {
            this._db.close();
            this._db = null;
        }
        await this._deleteIfExists();
    },

    // -- Internal helpers --

    _openDb: function () {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this._dbName);
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    },

    _deleteIfExists: function () {
        return new Promise((resolve, reject) => {
            const request = indexedDB.deleteDatabase(this._dbName);
            request.onsuccess = () => resolve();
            request.onerror = () => resolve(); // ignore errors
            request.onblocked = () => resolve();
        });
    },

    _bulkPut: function (storeName, data) {
        return new Promise((resolve, reject) => {
            const tx = this._db.transaction(storeName, 'readwrite');
            const store = tx.objectStore(storeName);
            for (const row of data) {
                store.put(row);
            }
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }
};
