using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine.Tests;

/// <summary>
/// Format 3a: a graph declares inputs, a parameter value <c>$name</c> refers to one, evaluation
/// takes the caller's value or the default, and what the nodes saw is what the snapshot reports.
/// </summary>
[TestFixture]
public class GraphInputTests
{
    private static GraphDocument Doubled()
        => GraphDocument.Empty
            .SetInput(new GraphInput("n", ParamKind.Integer, "5", "The number to add to itself"))
            .AddNode("c", "test.const", 1).SetParam("c", "value", "$n")
            .AddNode("sum", "test.add", 1)
            .Connect("c.out", "sum.a").Connect("c.out", "sum.b");

    private static Dictionary<string, string> Given(string name, string value)
        => new() { [name] = value };

    [Test]
    public void Default_is_used_when_no_value_is_given()
    {
        var snapshot = Doubled().Evaluate(TestNodes.Registry);
        Assert.That(snapshot.IntegerOutput("sum"), Is.EqualTo(10));
        Assert.That(snapshot.GraphInputs, Is.EqualTo(new Dictionary<string, string> { ["n"] = "5" }));
    }

    [Test]
    public void Given_value_replaces_the_default()
    {
        var snapshot = Doubled().Evaluate(TestNodes.Registry, Given("n", "21"));
        Assert.That(snapshot.IntegerOutput("sum"), Is.EqualTo(42));
        Assert.That(snapshot.GraphInputs!["n"], Is.EqualTo("21"));
        Assert.That(snapshot.Document.Values["c"]["value"], Is.EqualTo("$n"), "the document keeps the reference");
    }

    [Test]
    public void Undeclared_reference_is_a_validation_error_naming_the_input()
    {
        var doc = GraphDocument.Empty.AddNode("c", "test.const", 1).SetParam("c", "value", "$missing");
        var errors = doc.Validate(TestNodes.Registry);
        Assert.That(errors.Select(e => (e.Kind, e.Target)), Is.EqualTo(new[] { (GraphErrorKind.UnknownGraphInput, "missing") }));
        var thrown = Assert.Throws<InvalidGraphException>(() => doc.Evaluate(TestNodes.Registry));
        Assert.That(thrown!.Message, Does.Contain("'missing'"));
    }

    [Test]
    public void Given_value_for_an_undeclared_input_is_rejected()
    {
        var thrown = Assert.Throws<InvalidGraphException>(
            () => Doubled().Evaluate(TestNodes.Registry, Given("other", "1")));
        Assert.That(thrown!.Errors.Single().Kind, Is.EqualTo(GraphErrorKind.UnknownGraphInput));
    }

    [Test]
    public void Given_value_of_the_wrong_kind_is_rejected()
    {
        var thrown = Assert.Throws<InvalidGraphException>(
            () => Doubled().Evaluate(TestNodes.Registry, Given("n", "twelve")));
        Assert.That(thrown!.Errors.Single().Kind, Is.EqualTo(GraphErrorKind.InvalidGraphInput));
    }

    [Test]
    public void Escaped_dollar_is_a_literal_value()
    {
        var doc = GraphDocument.Empty
            .AddNode("t", "test.text", 1).SetParam("t", "value", "$$n");
        Assert.That(doc.Validate(TestNodes.Registry), Is.Empty);
        var result = doc.Evaluate(TestNodes.Registry).Results["t"];
        Assert.That(result.Error, Is.Null);
        Assert.That(((TextValue)result.Outputs[0]).Value, Is.EqualTo("$n"));
    }

    [Test]
    public void Memo_key_follows_the_resolved_value()
    {
        var session = new EvalSession(TestNodes.Registry);
        session.SetDocument(Doubled());
        Assert.That(session.GetResult("c")!.ExecutionCount, Is.EqualTo(1));

        session.SetGraphInputs(Given("n", "7"));
        Assert.That(session.GetResult("c")!.ExecutionCount, Is.EqualTo(2), "a new value re-executes the reader");
        Assert.That(session.Snapshot.IntegerOutput("sum"), Is.EqualTo(14));

        session.SetGraphInputs(Given("n", "5"));
        Assert.That(session.GetResult("c")!.ExecutionCount, Is.EqualTo(2), "back to a known value is a memo hit");
    }

    [Test]
    public void Session_keeps_given_values_across_document_edits_and_drops_stale_ones()
    {
        var session = new EvalSession(TestNodes.Registry);
        session.SetDocument(Doubled(), Given("n", "3"));
        session.UpdateDocument(d => d.SetLayout("c", new NodeLayout(1, 2)));
        Assert.That(session.Snapshot.GraphInputs!["n"], Is.EqualTo("3"));
        Assert.That(session.Run().GraphInputs!["n"], Is.EqualTo("3"));

        session.SetDocument(Doubled().RemoveInput("n").SetParam("c", "value", "1"));
        Assert.That(session.GraphInputValues, Is.Empty);
    }

    [Test]
    public void Failed_set_keeps_the_previous_snapshot()
    {
        var session = new EvalSession(TestNodes.Registry);
        session.SetDocument(Doubled(), Given("n", "3"));
        Assert.Throws<InvalidGraphException>(() => session.SetGraphInputs(Given("n", "x")));
        Assert.That(session.Snapshot.IntegerOutput("sum"), Is.EqualTo(6));
        Assert.That(session.GraphInputValues["n"], Is.EqualTo("3"));
    }
}
