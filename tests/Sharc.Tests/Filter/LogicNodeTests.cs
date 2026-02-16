using Xunit;

namespace Sharc.Tests.Filter;

public sealed class LogicNodeTests
{
    // Helper: a node that always returns a fixed result and tracks evaluation count.
    private sealed class ConstantNode : IFilterNode
    {
        private readonly bool _result;
        public int EvaluationCount { get; private set; }

        public ConstantNode(bool result) => _result = result;

        public bool Evaluate(ReadOnlySpan<byte> payload, ReadOnlySpan<long> serialTypes,
                             int bodyOffset, long rowId)
        {
            EvaluationCount++;
            return _result;
        }
    }

    // ── AND ──

    [Fact]
    public void AndNode_AllTrue_ReturnsTrue()
    {
        var node = new AndNode([new ConstantNode(true), new ConstantNode(true)]);
        Assert.True(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void AndNode_OneFalse_ReturnsFalse()
    {
        var node = new AndNode([new ConstantNode(true), new ConstantNode(false)]);
        Assert.False(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void AndNode_ShortCircuits_OnFirstFalse()
    {
        var first = new ConstantNode(false);
        var second = new ConstantNode(true);
        var node = new AndNode([first, second]);

        node.Evaluate(default, default, 0, 0);

        Assert.Equal(1, first.EvaluationCount);
        Assert.Equal(0, second.EvaluationCount); // never evaluated
    }

    [Fact]
    public void AndNode_Empty_ReturnsTrue()
    {
        var node = new AndNode([]);
        Assert.True(node.Evaluate(default, default, 0, 0));
    }

    // ── OR ──

    [Fact]
    public void OrNode_AllFalse_ReturnsFalse()
    {
        var node = new OrNode([new ConstantNode(false), new ConstantNode(false)]);
        Assert.False(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void OrNode_OneTrue_ReturnsTrue()
    {
        var node = new OrNode([new ConstantNode(false), new ConstantNode(true)]);
        Assert.True(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void OrNode_ShortCircuits_OnFirstTrue()
    {
        var first = new ConstantNode(true);
        var second = new ConstantNode(false);
        var node = new OrNode([first, second]);

        node.Evaluate(default, default, 0, 0);

        Assert.Equal(1, first.EvaluationCount);
        Assert.Equal(0, second.EvaluationCount); // never evaluated
    }

    [Fact]
    public void OrNode_Empty_ReturnsFalse()
    {
        var node = new OrNode([]);
        Assert.False(node.Evaluate(default, default, 0, 0));
    }

    // ── NOT ──

    [Fact]
    public void NotNode_InvertsTrue_ReturnsFalse()
    {
        var node = new NotNode(new ConstantNode(true));
        Assert.False(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void NotNode_InvertsFalse_ReturnsTrue()
    {
        var node = new NotNode(new ConstantNode(false));
        Assert.True(node.Evaluate(default, default, 0, 0));
    }

    // ── Nested composition ──

    [Fact]
    public void Nested_AndOrNot_EvaluatesCorrectly()
    {
        // AND(OR(true, false), NOT(OR(false, false)))
        // = AND(true, NOT(false))
        // = AND(true, true)
        // = true
        var node = new AndNode([
            new OrNode([new ConstantNode(true), new ConstantNode(false)]),
            new NotNode(new OrNode([new ConstantNode(false), new ConstantNode(false)]))
        ]);

        Assert.True(node.Evaluate(default, default, 0, 0));
    }

    [Fact]
    public void Nested_AndOrNot_FalseCase()
    {
        // AND(OR(false, false), NOT(true))
        // = AND(false, false)
        // = false
        var node = new AndNode([
            new OrNode([new ConstantNode(false), new ConstantNode(false)]),
            new NotNode(new ConstantNode(true))
        ]);

        Assert.False(node.Evaluate(default, default, 0, 0));
    }
}
