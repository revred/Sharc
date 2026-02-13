using System.Buffers;

namespace Sharc.Core.IO;

/// <summary>
/// Provides a mechanism for renting and returning byte buffers to minimize GC pressure.
/// Decouples Sharc from a specific pooling implementation.
/// </summary>
public interface ISharcBufferPool
{
    /// <summary>
    /// Rents a byte buffer of at least the specified length.
    /// </summary>
    byte[] Rent(int minimumLength);

    /// <summary>
    /// Recycles a previously rented buffer back to the pool.
    /// </summary>
    void Recycle(byte[] array, bool clearArray = false);
}
