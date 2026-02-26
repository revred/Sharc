// Web Crypto API bridge — hardware-accelerated ECDSA + SHA-256.
// Offloads cryptographic operations from Mono/.NET software crypto to browser native.
// Typical speedup: ~50x for ECDSA signatures (sub-millisecond vs 5+ ms).

window.webCryptoBridge = {
    _keys: new Map(),
    _nextId: 1,

    /// Generates an ECDSA P-256 key pair. Returns a key ID.
    generateKeyPair: async function () {
        const keyPair = await crypto.subtle.generateKey(
            { name: 'ECDSA', namedCurve: 'P-256' },
            true, // extractable
            ['sign', 'verify']
        );
        const id = this._nextId++;
        this._keys.set(id, keyPair);
        return id;
    },

    /// Imports a raw public key for verification. Returns a key ID.
    importPublicKey: async function (rawKeyBytes) {
        const key = await crypto.subtle.importKey(
            'raw',
            rawKeyBytes,
            { name: 'ECDSA', namedCurve: 'P-256' },
            true,
            ['verify']
        );
        const id = this._nextId++;
        this._keys.set(id, { publicKey: key });
        return id;
    },

    /// Exports the public key as raw bytes (65 bytes, uncompressed point).
    exportPublicKey: async function (keyId) {
        const entry = this._keys.get(keyId);
        if (!entry) throw new Error(`Key ${keyId} not found`);
        const pub = entry.publicKey || entry;
        const raw = await crypto.subtle.exportKey('raw', pub);
        return new Uint8Array(raw);
    },

    /// Signs data using ECDSA P-256 + SHA-256. Returns the signature bytes.
    sign: async function (keyId, data) {
        const entry = this._keys.get(keyId);
        if (!entry) throw new Error(`Key ${keyId} not found`);
        const privKey = entry.privateKey || entry;
        const signature = await crypto.subtle.sign(
            { name: 'ECDSA', hash: 'SHA-256' },
            privKey,
            data
        );
        return new Uint8Array(signature);
    },

    /// Verifies an ECDSA P-256 + SHA-256 signature. Returns boolean.
    verify: async function (keyId, data, signature) {
        const entry = this._keys.get(keyId);
        if (!entry) throw new Error(`Key ${keyId} not found`);
        const pubKey = entry.publicKey || entry;
        return await crypto.subtle.verify(
            { name: 'ECDSA', hash: 'SHA-256' },
            pubKey,
            signature,
            data
        );
    },

    /// Computes SHA-256 hash of the given data. Returns 32 bytes.
    sha256: async function (data) {
        const hash = await crypto.subtle.digest('SHA-256', data);
        return new Uint8Array(hash);
    },

    /// Releases a key pair from memory.
    disposeKey: function (keyId) {
        this._keys.delete(keyId);
    },

    /// Checks whether Web Crypto API is available.
    isSupported: function () {
        return typeof crypto !== 'undefined'
            && typeof crypto.subtle !== 'undefined';
    }
};
