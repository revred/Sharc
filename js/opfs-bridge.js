// OPFS (Origin Private File System) bridge — persistent browser-native storage.
// FileSystemSyncAccessHandle provides synchronous read/write within Web Workers.
// Main thread calls via postMessage → Worker → OPFS.

window.opfsBridge = {
    _handles: new Map(),
    _nextId: 1,

    /// Opens (or creates) a database file in OPFS. Returns a handle ID.
    openDatabase: async function (fileName) {
        const root = await navigator.storage.getDirectory();
        const fileHandle = await root.getFileHandle(fileName, { create: true });
        const accessHandle = await fileHandle.createSyncAccessHandle();
        const id = this._nextId++;
        this._handles.set(id, { accessHandle, fileName, fileHandle });
        const size = accessHandle.getSize();
        return { handleId: id, fileSize: size };
    },

    /// Reads a page from OPFS into the provided buffer.
    /// pageNumber is 1-based; pageSize is in bytes.
    readPage: function (handleId, pageNumber, pageSize) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);

        const buffer = new Uint8Array(pageSize);
        const offset = (pageNumber - 1) * pageSize;
        const bytesRead = entry.accessHandle.read(buffer, { at: offset });
        return { data: buffer, bytesRead };
    },

    /// Reads a page directly into a pre-allocated SharedArrayBuffer view.
    /// Zero-copy path for WASM interop.
    readPageInto: function (handleId, pageNumber, pageSize, targetBuffer, targetOffset) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);

        const view = new Uint8Array(targetBuffer, targetOffset, pageSize);
        const fileOffset = (pageNumber - 1) * pageSize;
        return entry.accessHandle.read(view, { at: fileOffset });
    },

    /// Writes a page to OPFS from the provided data.
    writePage: function (handleId, pageNumber, pageSize, data) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);

        const offset = (pageNumber - 1) * pageSize;
        const written = entry.accessHandle.write(data, { at: offset });
        return written;
    },

    /// Flushes all pending writes to persistent storage.
    flush: function (handleId) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);
        entry.accessHandle.flush();
    },

    /// Gets the current file size in bytes.
    getFileSize: function (handleId) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);
        return entry.accessHandle.getSize();
    },

    /// Truncates or extends the file to the given size.
    truncate: function (handleId, newSize) {
        const entry = this._handles.get(handleId);
        if (!entry) throw new Error(`OPFS handle ${handleId} not found`);
        entry.accessHandle.truncate(newSize);
    },

    /// Closes the sync access handle and releases the file lock.
    close: function (handleId) {
        const entry = this._handles.get(handleId);
        if (!entry) return;
        entry.accessHandle.close();
        this._handles.delete(handleId);
    },

    /// Deletes a database file from OPFS.
    deleteDatabase: async function (fileName) {
        const root = await navigator.storage.getDirectory();
        try {
            await root.removeEntry(fileName);
            return true;
        } catch (e) {
            if (e.name === 'NotFoundError') return false;
            throw e;
        }
    },

    /// Lists all files in the OPFS root directory.
    listDatabases: async function () {
        const root = await navigator.storage.getDirectory();
        const files = [];
        for await (const [name, handle] of root.entries()) {
            if (handle.kind === 'file') {
                files.push(name);
            }
        }
        return files;
    },

    /// Returns OPFS storage quota information.
    getQuota: async function () {
        if (navigator.storage && navigator.storage.estimate) {
            const est = await navigator.storage.estimate();
            return {
                usageBytes: est.usage || 0,
                quotaBytes: est.quota || 0,
                availableBytes: (est.quota || 0) - (est.usage || 0)
            };
        }
        return { usageBytes: -1, quotaBytes: -1, availableBytes: -1 };
    },

    /// Checks whether OPFS with sync access handles is available.
    isSupported: function () {
        return typeof FileSystemSyncAccessHandle !== 'undefined'
            && typeof navigator.storage?.getDirectory === 'function';
    }
};
