// Copyright (c) Ram Revanur. All rights reserved.
// Licensed under the MIT License.

namespace Sharc.Cache.Tests;

/// <summary>
/// Minimal fake <see cref="TimeProvider"/> for deterministic testing of time-dependent cache behavior.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FakeTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    public FakeTimeProvider(DateTimeOffset startTime)
    {
        _utcNow = startTime;
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow += duration;

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}
