namespace Sharc.Core.IO;

/// <summary>
/// A page source that delegates all calls to a target source.
/// The target can be swapped at runtime.
/// </summary>
internal sealed class ProxyPageSource : IPageSource
{
    private IPageSource _target;

    public ProxyPageSource(IPageSource target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <inheritdoc />
    public int PageSize => _target.PageSize;
    /// <inheritdoc />
    public int PageCount => _target.PageCount;
    /// <inheritdoc />
    public long DataVersion => _target.DataVersion;

    public void SetTarget(IPageSource target)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <inheritdoc />
    public ReadOnlySpan<byte> GetPage(uint pageNumber) => _target.GetPage(pageNumber);

    /// <inheritdoc />
    public int ReadPage(uint pageNumber, Span<byte> destination) => _target.ReadPage(pageNumber, destination);

    /// <inheritdoc />
    public void Invalidate(uint pageNumber) => _target.Invalidate(pageNumber);

    /// <inheritdoc />
    public void Dispose()
    {
        // We don't dispose the target here as it's managed by the owner of the proxy.
    }
}
