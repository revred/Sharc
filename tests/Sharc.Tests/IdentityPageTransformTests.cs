using FluentAssertions;
using Sharc.Core;
using Xunit;

namespace Sharc.Tests;

/// <summary>
/// Tests for the identity (no-op) page transform.
/// </summary>
public class IdentityPageTransformTests
{
    [Fact]
    public void TransformedPageSize_ReturnsSameSize()
    {
        var transform = IdentityPageTransform.Instance;
        transform.TransformedPageSize(4096).Should().Be(4096);
    }

    [Fact]
    public void TransformRead_CopiesDataUnchanged()
    {
        var transform = IdentityPageTransform.Instance;
        byte[] source = [1, 2, 3, 4, 5];
        byte[] dest = new byte[5];

        transform.TransformRead(source, dest, 1);

        dest.Should().Equal(source);
    }

    [Fact]
    public void TransformWrite_CopiesDataUnchanged()
    {
        var transform = IdentityPageTransform.Instance;
        byte[] source = [10, 20, 30];
        byte[] dest = new byte[3];

        transform.TransformWrite(source, dest, 1);

        dest.Should().Equal(source);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = IdentityPageTransform.Instance;
        var b = IdentityPageTransform.Instance;
        ReferenceEquals(a, b).Should().BeTrue();
    }
}
