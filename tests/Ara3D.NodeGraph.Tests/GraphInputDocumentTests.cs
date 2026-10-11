using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.NodeGraph.Tests;

/// <summary>Format 3a: the structure layer's optional "inputs" list, its canonical form, and its hash.</summary>
public class GraphInputDocumentTests
{
    private static GraphDocument WithInputs()
        => TestDocs.ConstNegate()
            .SetInput(new GraphInput("zeta", ParamKind.Text, "z"))
            .SetInput(new GraphInput("model", ParamKind.FilePath, "C:/data/a.duckdb", "The building to analyse"));

    [Test]
    public void Inputs_round_trip_sorted_by_name()
    {
        var text = WithInputs().ToCanonicalJson();
        var loaded = GraphDocumentIO.Parse(text);
        Assert.That(loaded.Inputs.Select(i => i.Name), Is.EqualTo(new[] { "model", "zeta" }));
        Assert.That(loaded.Inputs[0], Is.EqualTo(new GraphInput("model", ParamKind.FilePath, "C:/data/a.duckdb", "The building to analyse")));
        Assert.That(loaded.ToCanonicalJson(), Is.EqualTo(text));
        Assert.That(text, Does.Contain("\"inputs\": ["));
    }

    [Test]
    public void Document_without_inputs_serializes_without_the_member_and_keeps_its_hash()
    {
        var doc = TestDocs.ConstNegate();
        Assert.That(doc.ToCanonicalJson(), Does.Not.Contain("inputs"));
        Assert.That(doc.SetInput(new GraphInput("a", ParamKind.Text, "")).RemoveInput("a").ComputeGraphHash(),
            Is.EqualTo(doc.ComputeGraphHash()));
    }

    [Test]
    public void Declaring_an_input_changes_the_graph_hash()
        => Assert.That(WithInputs().ComputeGraphHash(), Is.Not.EqualTo(TestDocs.ConstNegate().ComputeGraphHash()));

    [Test]
    public void Unknown_member_in_an_input_is_a_format_error()
    {
        var text = WithInputs().ToCanonicalJson().Replace("\"kind\": \"Text\"", "\"kind\": \"Text\", \"extra\": \"x\"");
        Assert.Throws<FormatException>(() => GraphDocumentIO.Parse(text));
    }

    [Test]
    public void Kind_outside_the_five_is_a_format_error()
    {
        var text = WithInputs().ToCanonicalJson().Replace("\"kind\": \"Text\"", "\"kind\": \"Json\"");
        Assert.Throws<FormatException>(() => GraphDocumentIO.Parse(text));
    }

    [Test]
    public void Validation_reports_duplicates_bad_names_and_bad_defaults()
    {
        var doc = TestDocs.ConstNegate()
            .SetInput(new GraphInput("n", ParamKind.Integer, "x"))
            .SetInput(new GraphInput("bad name", ParamKind.Text, ""));
        doc = doc with { Inputs = doc.Inputs.Append(new GraphInput("n", ParamKind.Text, "")).ToList() };
        var kinds = doc.Validate(TestNodes.Registry).Select(e => e.Kind).ToList();
        Assert.That(kinds, Does.Contain(GraphErrorKind.DuplicateGraphInput));
        Assert.That(kinds.Count(k => k == GraphErrorKind.InvalidGraphInput), Is.EqualTo(2));
    }

    [Test]
    public void Reference_grammar()
    {
        Assert.That(GraphInputs.ReferencedName("$model"), Is.EqualTo("model"));
        Assert.That(GraphInputs.ReferencedName("$$model"), Is.Null);
        Assert.That(GraphInputs.ReferencedName("$"), Is.Null);
        Assert.That(GraphInputs.ReferencedName("$a b"), Is.Null);
        Assert.That(GraphInputs.ReferencedName("C:/$model"), Is.Null);
        Assert.That(GraphInputs.Unescape("$$model"), Is.EqualTo("$model"));
        Assert.That(GraphInputs.Unescape("plain"), Is.EqualTo("plain"));
    }
}
