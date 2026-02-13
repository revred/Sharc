using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// Default implementation of ISharcBufferPool using the shared system array pool.
/// </summary>
public sealed class SharcBufferPool : ISharcBufferPool
{
    /// <summary>
    /// Static shared instance for convenience.
    /// </summary>
    public static readonly ISharcBufferPool Shared = new SharcBufferPool();

    /// <inheritdoc />
    public byte[] Rent(int minimumLength) => ArrayPool<byte>.Shared.Rent(minimumLength);

    /// <inheritdoc />
    public void Recycle(byte[] array, bool clearArray = false) => ArrayPool<byte>.Shared.Return(array, clearArray);
}
