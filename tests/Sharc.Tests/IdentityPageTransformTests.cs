/*-------------------------------------------------------------------------------------------------!
  "Where the mind is free to imagine and the craft is guided by clarity, code awakens."            |

  A collaborative work shaped by Artificial Intelligence and curated with intent by Ram Revanur.
  Software here is treated not as static text, but as a living system designed to learn and evolve.
  Built on the belief that architecture and context often define outcomes before code is written.

  This file reflects an AI-aware, agentic, context-driven, and continuously evolving approach
  to modern engineering. If you seek to transform a traditional codebase into an adaptive,
  intelligence-guided system, you may find resonance in these patterns and principles.

  Subtle conversations often begin with a single message â€” or a prompt with the right context.
  https://www.linkedin.com/in/revodoc/

  Licensed under the MIT License â€” free for personal and commercial use.                           |
--------------------------------------------------------------------------------------------------*/

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
        Assert.Equal(4096, transform.TransformedPageSize(4096));
    }

    [Fact]
    public void TransformRead_CopiesDataUnchanged()
    {
        var transform = IdentityPageTransform.Instance;
        byte[] source = [1, 2, 3, 4, 5];
        byte[] dest = new byte[5];

        transform.TransformRead(source, dest, 1);

        Assert.Equal(source, dest);
    }

    [Fact]
    public void TransformWrite_CopiesDataUnchanged()
    {
        var transform = IdentityPageTransform.Instance;
        byte[] source = [10, 20, 30];
        byte[] dest = new byte[3];

        transform.TransformWrite(source, dest, 1);

        Assert.Equal(source, dest);
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = IdentityPageTransform.Instance;
        var b = IdentityPageTransform.Instance;
        Assert.True(ReferenceEquals(a, b));
    }
}
