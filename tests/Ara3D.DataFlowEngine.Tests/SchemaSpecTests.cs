using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.DataFlowEngine.Tests;

[TestFixture]
public class SchemaSpecTests
{
    private static readonly Schema Two = new([new("A", ColumnType.Integer, false), new("B", ColumnType.Text)]);

    private sealed class InferringNode(NodeSpec spec) : ISchemaNode
    {
        public NodeSpec Spec { get; } = spec;

        public IReadOnlyList<FlowValue> Eval(IEvalContext context, IReadOnlyList<FlowValue> inputs, ParamValues parameters)
            => [];

        public SchemaResult Infer(string output, IReadOnlyList<SchemaInput> inputs, ParamValues parameters, ISchemaCatalog catalog)
            => inputs[0].Schema ?? SchemaResult.Fail("no input");
    }

    private static NodeSpec Spec(PortType output, params SchemaSpec[] schemas)
        => new("test.s", 1, NodeCapability.Pure, [new PortSpec("in", PortType.Table)],
            [new PortSpec("out", output), new PortSpec("n", PortType.Integer)], [], Schemas: schemas);

    private static FakeNode Fake(NodeSpec spec)
        => new(spec, (_, _, _) => []);

    [Test]
    public void A_spec_without_schemas_has_an_empty_list()
        => Assert.That(new NodeSpec("k", 1, NodeCapability.Pure, [], [], []).Schemas, Is.Empty);

    [Test]
    public void Schema_specs_compare_by_value()
    {
        Assert.That(SchemaSpec.Dynamic("out"), Is.EqualTo(SchemaSpec.Dynamic("out")));
        Assert.That(SchemaSpec.Static("out", Two), Is.EqualTo(SchemaSpec.Static("out", new Schema([.. Two.Columns]))));
        Assert.That(SchemaSpec.Dynamic("out"), Is.Not.EqualTo(SchemaSpec.Inferred("out")));
    }

    [Test]
    public void Valid_declarations_have_no_problems()
    {
        Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Static("out", Two, new Dictionary<string, string> { ["a"] = "Key" }))).SchemaProblems(), Is.Empty);
        Assert.That(Fake(Spec(PortType.Relation, SchemaSpec.Dynamic("out"))).SchemaProblems(), Is.Empty);
        Assert.That(new InferringNode(Spec(PortType.Table, SchemaSpec.Inferred("out"))).SchemaProblems(), Is.Empty);
    }

    [Test]
    public void A_spec_must_name_an_existing_output()
        => Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Dynamic("nope"))).SchemaProblems(), Has.Count.EqualTo(1).And.Some.Contain("unknown output 'nope'"));

    [Test]
    public void A_spec_must_name_a_table_or_relation_output()
        => Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Dynamic("n"))).SchemaProblems(), Has.Some.Contain("not Table or Relation"));

    [Test]
    public void An_inferred_spec_needs_ISchemaNode()
        => Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Inferred("out"))).SchemaProblems(), Has.Some.Contain("ISchemaNode"));

    [Test]
    public void Kinds_must_name_columns_of_the_schema()
        => Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Static("out", Two, new Dictionary<string, string> { ["Z"] = "Key" }))).SchemaProblems(),
            Has.Some.Contain("column 'Z'"));

    [Test]
    public void One_schema_per_output()
        => Assert.That(Fake(Spec(PortType.Table, SchemaSpec.Dynamic("out"), SchemaSpec.Dynamic("out"))).SchemaProblems(),
            Has.Some.Contain("more than one"));

    [Test]
    public void The_registry_collects_problems_of_all_nodes()
    {
        var registry = new NodeRegistry([Fake(Spec(PortType.Table, SchemaSpec.Dynamic("nope")))]);
        Assert.That(registry.SchemaProblems(), Has.Count.EqualTo(1));
    }

    [Test]
    public void An_inferring_node_sees_a_non_table_input_beside_the_table()
    {
        var node = new InferringNode(Spec(PortType.Table, SchemaSpec.Inferred("out")));
        var result = node.Infer("out",
            [new SchemaInput(PortType.Table, true, SchemaResult.Of(Two)), new SchemaInput(PortType.Database, true, Value: new DatabaseValue("p", "h"))],
            ParamValues.Empty, new StaticCatalog(new Dictionary<string, Schema>()));
        Assert.That(result.Require(), Is.EqualTo(Two));
    }

    [Test]
    public void A_schema_with_warnings_is_still_ok_and_a_failure_ignores_added_warnings()
    {
        var warned = SchemaResult.Of(Two).WithWarnings([new SchemaError("no column 'x'")]);
        Assert.That(warned.Ok, Is.True);
        Assert.That(warned.Require(), Is.EqualTo(Two));
        Assert.That(warned.Warnings.Select(w => w.Message), Is.EqualTo(new[] { "no column 'x'" }));
        Assert.That(SchemaResult.Of(Two).Warnings, Is.Empty);
        Assert.That(SchemaResult.Fail("bad").WithWarnings([new SchemaError("w")]).Warnings, Is.Empty);
    }

    [Test]
    public void Schema_lookup_ignores_case_and_equality_is_structural()
    {
        Assert.That(Two.Find("b")!.Value.Name, Is.EqualTo("B"));
        Assert.That(Two.Has("c"), Is.False);
        Assert.That(new Schema([.. Two.Columns]), Is.EqualTo(Two));
        Assert.That(Two.Append(new Column("C", ColumnType.Date)).Columns, Has.Count.EqualTo(3));
    }

    [Test]
    public void Static_catalog_answers_declared_sources_and_fails_others()
    {
        var catalog = new StaticCatalog(new Dictionary<string, Schema> { [StaticCatalog.Key("db", "t")] = Two });
        Assert.That(catalog.TableSchema("db", "t").Require(), Is.EqualTo(Two));
        Assert.That(catalog.CsvSchema("db", "x").Ok, Is.False);
        Assert.That(catalog.QuerySchema("select 1", []).Ok, Is.False);
    }

    [Test]
    public void Static_catalog_answers_file_sources_by_format_path_and_options()
    {
        var options = new Dictionary<string, string> { ["table"] = "walls" };
        var catalog = new StaticCatalog(new Dictionary<string, Schema> { [StaticCatalog.FileKey("duckdb", "a.duckdb", options)] = Two });
        Assert.That(catalog.FileSchema("duckdb", "a.duckdb", options).Require(), Is.EqualTo(Two));
        Assert.That(catalog.FileSchema("duckdb", "a.duckdb", new Dictionary<string, string>()).Ok, Is.False);
        Assert.That(catalog.FileQuerySchema("select 1", ["a.csv"]).Ok, Is.False);
    }
}
