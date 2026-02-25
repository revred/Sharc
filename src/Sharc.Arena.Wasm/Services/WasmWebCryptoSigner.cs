/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Licensed under the MIT License — free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

using Microsoft.JSInterop;
using Sharc.Core.Trust;
using Sharc.Trust;

namespace Sharc.Arena.Wasm.Services;

/// <summary>
/// Browser-native <see cref="ISharcSigner"/> that offloads ECDSA P-256 + SHA-256
/// to the Web Crypto API via <c>web-crypto-bridge.js</c>.
/// <para>
/// Performance: Web Crypto uses hardware-accelerated native crypto (~0.5 ms per signature)
/// versus Mono's software ECDSA (~5+ ms). This is critical for the Trust layer in
/// browser-hosted multi-agent scenarios where ledger entries require per-write signatures.
/// </para>
/// </summary>
public sealed class WasmWebCryptoSigner : ISharcSigner
{
    private readonly IJSRuntime _js;
    private readonly int _keyId;
    private byte[]? _cachedPublicKey;
    private bool _disposed;

    /// <inheritdoc />
    public string AgentId { get; }

    /// <inheritdoc />
    public SignatureAlgorithm Algorithm => SignatureAlgorithm.EcdsaP256;

    /// <summary>
    /// ECDSA P-256 signature size is 64 bytes (r + s, 32 bytes each).
    /// </summary>
    public int SignatureSize => 64;

    private WasmWebCryptoSigner(IJSRuntime js, int keyId, string agentId)
    {
        _js = js;
        _keyId = keyId;
        AgentId = agentId;
    }

    /// <summary>
    /// Creates a new signer with a freshly generated ECDSA P-256 key pair.
    /// </summary>
    public static async Task<WasmWebCryptoSigner> CreateAsync(IJSRuntime js, string agentId)
    {
        int keyId = await js.InvokeAsync<int>("webCryptoBridge.generateKeyPair");
        return new WasmWebCryptoSigner(js, keyId, agentId);
    }

    /// <summary>
    /// Creates a verification-only signer from a raw public key.
    /// Sign operations will throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public static async Task<WasmWebCryptoSigner> FromPublicKeyAsync(
        IJSRuntime js, string agentId, byte[] publicKey)
    {
        int keyId = await js.InvokeAsync<int>("webCryptoBridge.importPublicKey", publicKey);
        var signer = new WasmWebCryptoSigner(js, keyId, agentId);
        signer._cachedPublicKey = publicKey;
        return signer;
    }

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Web Crypto is async-only; use synchronous JS interop in Blazor WASM
        if (_js is IJSInProcessRuntime jsInProcess)
        {
            // Note: Web Crypto sign is always async even in Web Workers.
            // For synchronous contexts, we invoke the JS promise synchronously
            // via the Blazor WASM single-threaded scheduler.
            return jsInProcess.Invoke<byte[]>("webCryptoBridge.sign", _keyId, data.ToArray());
        }

        throw new InvalidOperationException(
            "WasmWebCryptoSigner.Sign requires IJSInProcessRuntime (Blazor WASM).");
    }

    /// <inheritdoc />
    public bool TrySign(ReadOnlySpan<byte> data, Span<byte> destination, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var signature = Sign(data);
            if (signature.Length > destination.Length)
            {
                bytesWritten = 0;
                return false;
            }
            signature.CopyTo(destination);
            bytesWritten = signature.Length;
            return true;
        }
        catch
        {
            bytesWritten = 0;
            return false;
        }
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            return jsInProcess.Invoke<bool>("webCryptoBridge.verify",
                _keyId, data.ToArray(), signature.ToArray());
        }

        throw new InvalidOperationException(
            "WasmWebCryptoSigner.Verify requires IJSInProcessRuntime (Blazor WASM).");
    }

    /// <inheritdoc />
    public byte[] GetPublicKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedPublicKey != null)
            return _cachedPublicKey;

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            _cachedPublicKey = jsInProcess.Invoke<byte[]>("webCryptoBridge.exportPublicKey", _keyId);
            return _cachedPublicKey;
        }

        throw new InvalidOperationException(
            "WasmWebCryptoSigner.GetPublicKey requires IJSInProcessRuntime (Blazor WASM).");
    }

    /// <summary>
    /// Async version of <see cref="Sign"/> for use outside synchronous contexts.
    /// </summary>
    public async Task<byte[]> SignAsync(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _js.InvokeAsync<byte[]>("webCryptoBridge.sign", _keyId, data);
    }

    /// <summary>
    /// Async version of <see cref="Verify"/> for use outside synchronous contexts.
    /// </summary>
    public async Task<bool> VerifyAsync(byte[] data, byte[] signature)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _js.InvokeAsync<bool>("webCryptoBridge.verify", _keyId, data, signature);
    }

    /// <summary>
    /// Async version of <see cref="GetPublicKey"/> for use outside synchronous contexts.
    /// </summary>
    public async Task<byte[]> GetPublicKeyAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedPublicKey != null)
            return _cachedPublicKey;

        _cachedPublicKey = await _js.InvokeAsync<byte[]>("webCryptoBridge.exportPublicKey", _keyId);
        return _cachedPublicKey;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_js is IJSInProcessRuntime jsInProcess)
        {
            jsInProcess.InvokeVoid("webCryptoBridge.disposeKey", _keyId);
        }
    }
}
