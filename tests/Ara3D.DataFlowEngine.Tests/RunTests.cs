using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine.Tests;

/// <summary>EvalSession.Run per spec semantics §6: effects execute exactly once per Run, in
/// topological order, never memoized, and the standing pass afterwards is effect-free again.</summary>
[TestFixture]
public class RunTests
{
    /// <summary>const(3) -> effect e -> probe after.</summary>
    private static GraphDocument Chain()
        => GraphDocument.Empty
            .AddNode("c", "test.const", 1).SetParam("c", "value", "3")
            .AddNode("e", "test.effect", 1)
            .AddNode("after", "test.probe", 1)
            .Connect("c.out", "e.in")
            .Connect("e.out", "after.in");

    /// <summary>const(7) feeding two independent effects z and b, declared out of id order.</summary>
    private static GraphDocument TwoEffects(string zKind = "test.effect")
        => GraphDocument.Empty
            .AddNode("a", "test.const", 1).SetParam("a", "value", "7")
            .AddNode("z", zKind, 1)
            .AddNode("b", "test.effect", 1)
            .Connect("a.out", "z.in")
            .Connect("a.out", "b.in");

    private static EvalSession SessionOver(GraphDocument doc)
    {
        var session = new EvalSession(TestNodes.Registry);
        session.SetDocument(doc);
        return session;
    }

    [Test]
    public void Run_executes_the_effect_once_and_evaluates_its_downstream()
    {
        var snapshot = SessionOver(Chain()).Run();
        Assert.That(snapshot.Results["e"].Status, Is.EqualTo(NodeStatus.Ok));
        Assert.That(snapshot.IntegerOutput("e"), Is.EqualTo(3));
        Assert.That(snapshot.Results["after"].Status, Is.EqualTo(NodeStatus.Ok));
        Assert.That(snapshot.IntegerOutput("after"), Is.EqualTo(3));
        Assert.That(snapshot.Executions("e"), Is.EqualTo(1));
        Assert.That(snapshot.Executions("c"), Is.EqualTo(1), "the pure node came from the memo cache");
    }

    [Test]
    public void Effects_execute_on_every_run_while_pure_nodes_stay_memoized()
    {
        var session = SessionOver(Chain());
        session.Run();
        var snapshot = session.Run();
        Assert.That(snapshot.Executions("e"), Is.EqualTo(2));
        Assert.That(snapshot.Executions("c"), Is.EqualTo(1));
        Assert.That(snapshot.Executions("after"), Is.EqualTo(1), "same effect output, memo hit");
    }

    [Test]
    public void Standing_pass_after_a_run_is_effect_free_again()
    {
        var session = SessionOver(Chain());
        session.Run();
        var snapshot = session.SetDocument(session.Document);
        Assert.That(snapshot.Results["e"].Status, Is.EqualTo(NodeStatus.EffectPending));
        Assert.That(snapshot.Results["after"].Status, Is.EqualTo(NodeStatus.Unavailable));
        Assert.That(snapshot.Executions("e"), Is.EqualTo(1));
    }

    [Test]
    public void Effect_nodes_see_IsRun()
    {
        var doc = GraphDocument.Empty
            .AddNode("c", "test.const", 1).SetParam("c", "value", "1")
            .AddNode("r", "test.isRun", 1)
            .Connect("c.out", "r.in");
        var snapshot = SessionOver(doc).Run();
        Assert.That(((BooleanValue)snapshot.Results["r"].Outputs[0]).Value, Is.True);
    }

    [Test]
    public void Effects_run_in_topological_order_with_ties_by_node_id()
    {
        var snapshot = SessionOver(TwoEffects()).Run();
        Assert.That(snapshot.ExecutedEffects(TestNodes.Registry), Is.EqualTo(new[] { "b", "z" }));
    }

    [Test]
    public void Nothing_is_executed_before_a_run()
    {
        var snapshot = TwoEffects().Evaluate(TestNodes.Registry);
        Assert.That(snapshot.ExecutedEffects(TestNodes.Registry), Is.Empty);
    }

    [Test]
    public void A_failing_effect_poisons_only_its_own_chain()
    {
        var doc = TwoEffects(zKind: "test.effectThrow")
            .AddNode("afterZ", "test.probe", 1)
            .Connect("z.out", "afterZ.in");
        var snapshot = SessionOver(doc).Run();
        Assert.That(snapshot.Results["z"].Status, Is.EqualTo(NodeStatus.Error));
        Assert.That(snapshot.Results["z"].Error, Does.Contain("effect failed"));
        Assert.That(snapshot.Results["afterZ"].Status, Is.EqualTo(NodeStatus.Unavailable));
        Assert.That(snapshot.Results["afterZ"].BlockingNodeId, Is.EqualTo("z"));
        Assert.That(snapshot.Results["b"].Status, Is.EqualTo(NodeStatus.Ok));
        Assert.That(snapshot.ExecutedEffects(TestNodes.Registry), Is.EqualTo(new[] { "b", "z" }),
            "a failed effect still counts as executed");
    }

    [Test]
    public void Observers_see_the_run_snapshot()
    {
        var session = SessionOver(Chain());
        var seen = new List<EvalSnapshot>();
        session.Subscribe(seen.Add);
        session.Run();
        Assert.That(seen, Has.Count.EqualTo(1));
        Assert.That(seen[0].Results["e"].Status, Is.EqualTo(NodeStatus.Ok));
        Assert.That(session.Snapshot, Is.SameAs(seen[0]));
    }

    [Test]
    public void One_shot_run_over_a_document()
    {
        var snapshot = Chain().Run(TestNodes.Registry);
        Assert.That(snapshot.IntegerOutput("after"), Is.EqualTo(3));
        Assert.That(snapshot.Executions("e"), Is.EqualTo(1));
    }
}
