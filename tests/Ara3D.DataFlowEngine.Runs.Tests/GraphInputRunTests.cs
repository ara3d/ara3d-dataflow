using Ara3D.DataFlowEngine;
using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine.Runs.Tests;

/// <summary>A run pins each graph input and its resolved value; replay evaluates with them.</summary>
[TestFixture]
public class GraphInputRunTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);

    private static GraphDocument Graph()
        => TestGraphs.AddGraph(a: "$n", b: "7").SetInput(new GraphInput("n", ParamKind.Integer, "5", "First addend"));

    private static RunRecord Freeze(GraphDocument doc, Dictionary<string, string>? given = null)
        => RunRecorder.Freeze(doc.Evaluate(TestGraphs.Registry, given ?? new()), TestGraphs.Registry,
            Array.Empty<RunInput>(), "engine", Timestamp);

    [Test]
    public void Record_pins_the_resolved_values_and_round_trips()
    {
        var record = Freeze(Graph(), new() { ["n"] = "30" });
        Assert.That(record.GraphInputs, Is.EqualTo(new Dictionary<string, string> { ["n"] = "30" }));
        Assert.That(record.RecordedOutputs["sum.out"], Is.EqualTo(new IntegerValue(37)));

        var text = record.ToCanonicalJson();
        Assert.That(text, Does.Contain("\"graphInputs\": {\n    \"n\": \"30\"\n  }"));
        Assert.That(RunRecordJson.Parse(text).GraphInputs, Is.EqualTo(record.GraphInputs));
    }

    [Test]
    public void Default_is_pinned_when_nothing_was_given()
        => Assert.That(Freeze(Graph()).GraphInputs["n"], Is.EqualTo("5"));

    [Test]
    public void Graph_without_inputs_writes_no_member()
        => Assert.That(Freeze(TestGraphs.AddGraph()).ToCanonicalJson(), Does.Not.Contain("graphInputs"));

    [Test]
    public void Same_graph_different_values_share_a_graph_hash_but_not_outputs()
    {
        var first = Freeze(Graph());
        var second = Freeze(Graph(), new() { ["n"] = "30" });
        Assert.That(second.GraphHash, Is.EqualTo(first.GraphHash));
        Assert.That(second.NodeOutputs["sum.out"], Is.Not.EqualTo(first.NodeOutputs["sum.out"]));
    }

    [Test]
    public void Replay_uses_the_pinned_values()
    {
        var doc = Graph();
        var record = Freeze(doc, new() { ["n"] = "30" });
        Assert.That(RunReplay.Replay(record, doc, TestGraphs.Registry, Array.Empty<RunInput>()),
            Is.EqualTo(ReplayResult.Success));
    }
}
