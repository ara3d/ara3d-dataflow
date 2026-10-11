using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine.Tests;

/// <summary>
/// Spec semantics §2: a bindable parameter is also an input port of its own name. A wired value
/// replaces the parameter's string, an unwired one leaves the string in force, and the edge is
/// validated and memoized like any other.
/// </summary>
[TestFixture]
public class BindableParamTests
{
    /// <summary>greet(name param, bindable Text) → "hello " + name.</summary>
    private static readonly IFlowNode Greet = new FakeNode(
        new NodeSpec("test.greet", 1, NodeCapability.Pure,
            Array.Empty<PortSpec>(),
            new PortSpec[] { new("out", PortType.Text) },
            new[] { new ParamSpec("name", ParamKind.Text, "nobody", Bindable: PortType.Text) }),
        (_, _, p) => new FlowValue[] { new TextValue("hello " + p.GetText("name", "nobody")) });

    /// <summary>twice(n param, bindable Integer) → n * 2.</summary>
    private static readonly IFlowNode Twice = new FakeNode(
        new NodeSpec("test.twice", 1, NodeCapability.Pure,
            Array.Empty<PortSpec>(),
            new PortSpec[] { new("out", PortType.Integer) },
            new[] { new ParamSpec("n", ParamKind.Integer, "1", Bindable: PortType.Integer) }),
        (_, _, p) => new FlowValue[] { new IntegerValue(p.GetInteger("n") * 2) });

    /// <summary>probe(p param, bindable Any) → the wired value, or Text of the string.</summary>
    private static readonly IFlowNode Bound = new FakeNode(
        new NodeSpec("test.bound", 1, NodeCapability.Pure,
            Array.Empty<PortSpec>(),
            new PortSpec[] { new("out", PortType.Any) },
            new[] { new ParamSpec("p", ParamKind.Text, Bindable: PortType.Any) }),
        (_, _, p) => new[] { p.GetBound("p") ?? new TextValue("string:" + p.GetText("p", "missing")) });

    private static readonly INodeRegistry Registry =
        NodeRegistry.Combine(TestNodes.Registry.Nodes, new[] { Greet, Twice, Bound });

    private static string TextOutput(EvalSnapshot snapshot, string nodeId)
        => ((TextValue)snapshot.Results[nodeId].Outputs[0]).Value;

    private static GraphDocument Source(string value)
        => GraphDocument.Empty.AddNode("src", "test.text", 1).SetParam("src", "value", value);

    [Test]
    public void Unwired_parameter_uses_its_string()
    {
        var doc = GraphDocument.Empty.AddNode("g", "test.greet", 1).SetParam("g", "name", "ada");
        Assert.That(TextOutput(doc.Evaluate(Registry), "g"), Is.EqualTo("hello ada"));
    }

    [Test]
    public void Unwired_parameter_never_makes_the_node_unready()
    {
        var snapshot = GraphDocument.Empty.AddNode("g", "test.greet", 1).Evaluate(Registry);
        Assert.That(snapshot.Results["g"].Status, Is.EqualTo(NodeStatus.Ok));
    }

    [Test]
    public void Wired_value_replaces_the_string()
    {
        var doc = Source("grace").AddNode("g", "test.greet", 1).SetParam("g", "name", "ada")
            .Connect("src.out", "g.name");
        Assert.That(doc.Validate(Registry), Is.Empty);
        Assert.That(TextOutput(doc.Evaluate(Registry), "g"), Is.EqualTo("hello grace"));
    }

    [Test]
    public void Wired_integer_arrives_in_canonical_form()
    {
        var doc = GraphDocument.Empty
            .AddNode("c", "test.const", 1).SetParam("c", "value", "21")
            .AddNode("t", "test.twice", 1)
            .Connect("c.out", "t.n");
        var snapshot = doc.Evaluate(Registry);
        Assert.That(((IntegerValue)snapshot.Results["t"].Outputs[0]).Value, Is.EqualTo(42));
    }

    [Test]
    public void Non_scalar_value_is_read_through_GetBound_and_hides_the_string()
    {
        var database = new DatabaseValue("a.duckdb", "h1");
        var values = new ParamValues(new Dictionary<string, string> { ["p"] = "stale.duckdb" },
            new Dictionary<string, FlowValue> { ["p"] = database });
        Assert.That(values.GetBound("p"), Is.SameAs(database));
        Assert.That(values.GetText("p", "none"), Is.EqualTo("none"));
        Assert.That(new ParamValues(new Dictionary<string, string> { ["p"] = "x" }).GetBound("p"), Is.Null);
    }

    [Test]
    public void Edge_whose_kind_the_socket_rejects_is_a_validation_error()
    {
        var doc = Source("grace").AddNode("t", "test.twice", 1).Connect("src.out", "t.n");
        var errors = doc.Validate(Registry);
        Assert.That(errors.Select(e => e.Kind), Is.EqualTo(new[] { GraphErrorKind.PortTypeMismatch }));
    }

    [Test]
    public void Edge_to_a_parameter_that_is_not_bindable_is_an_unknown_port()
    {
        var doc = Source("x").AddNode("c", "test.const", 1).Connect("src.out", "c.value");
        Assert.That(doc.Validate(Registry).Select(e => e.Kind), Is.EqualTo(new[] { GraphErrorKind.UnknownPort }));
    }

    [Test]
    public void Connecting_a_second_edge_replaces_the_first()
    {
        var doc = Source("a").AddNode("s2", "test.text", 1).SetParam("s2", "value", "b")
            .AddNode("g", "test.greet", 1)
            .Connect("src.out", "g.name").Connect("s2.out", "g.name");
        Assert.That(doc.Validate(Registry), Is.Empty);
        Assert.That(TextOutput(doc.Evaluate(Registry), "g"), Is.EqualTo("hello b"));
    }

    [Test]
    public void Wired_value_enters_the_memo_key()
    {
        var session = new EvalSession(Registry);
        session.SetDocument(Source("grace").AddNode("g", "test.greet", 1).SetParam("g", "name", "ada"));
        var before = session.UpdateDocument(d => d);
        var wired = session.UpdateDocument(d => d.Connect("src.out", "g.name"));
        Assert.That(TextOutput(before, "g"), Is.EqualTo("hello ada"));
        Assert.That(TextOutput(wired, "g"), Is.EqualTo("hello grace"));
        Assert.That(wired.Executions("g"), Is.EqualTo(2));
        var changed = session.UpdateDocument(d => d.SetParam("src", "value", "linus"));
        Assert.That(TextOutput(changed, "g"), Is.EqualTo("hello linus"));
        Assert.That(changed.Executions("g"), Is.EqualTo(3));
    }

    [Test]
    public void Upstream_failure_blocks_the_bound_node()
    {
        var doc = GraphDocument.Empty
            .AddNode("c", "test.const", 1).SetParam("c", "value", "1")
            .AddNode("x", "test.throw", 1)
            .AddNode("b", "test.bound", 1)
            .Connect("c.out", "x.in").Connect("x.out", "b.p");
        var snapshot = doc.Evaluate(Registry);
        Assert.That(snapshot.Results["b"].Status, Is.EqualTo(NodeStatus.Unavailable));
        Assert.That(snapshot.Results["b"].BlockingNodeId, Is.EqualTo("x"));
    }

    [Test]
    public void ParamSpec_flags_default_off()
    {
        var spec = new ParamSpec("n", ParamKind.Text);
        Assert.That(spec.Bindable, Is.Null);
        Assert.That(spec.List, Is.False);
    }
}
