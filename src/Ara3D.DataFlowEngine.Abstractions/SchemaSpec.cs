using System.Collections.Generic;

namespace Ara3D.DataFlowEngine.Abstractions;

/// <summary>
/// What a node declares about the columns of one Table or Relation output (named by
/// <see cref="Output"/>): <see cref="StaticSchemaSpec"/> (always these columns),
/// <see cref="InferredSchemaSpec"/> (computed from the inputs and parameters by
/// <see cref="ISchemaNode"/>) or <see cref="DynamicSchemaSpec"/> (depends on the data, so
/// unknown until run). An output with no spec is undeclared, which is not the same as dynamic.
/// </summary>
public abstract record SchemaSpec(string Output)
{
    public static SchemaSpec Static(string output, Schema schema, IReadOnlyDictionary<string, string>? kinds = null)
        => new StaticSchemaSpec(output, schema, kinds);

    public static SchemaSpec Inferred(string output)
        => new InferredSchemaSpec(output);

    public static SchemaSpec Dynamic(string output)
        => new DynamicSchemaSpec(output);
}

/// <summary>
/// Kinds maps a column name to the name of a column kind (the registry of kinds lives above
/// the engine, so only the name is carried). A column named in Kinds must be in Schema.
/// </summary>
public sealed record StaticSchemaSpec(string Output, Schema Schema, IReadOnlyDictionary<string, string>? Kinds = null)
    : SchemaSpec(Output);

public sealed record InferredSchemaSpec(string Output) : SchemaSpec(Output);

public sealed record DynamicSchemaSpec(string Output) : SchemaSpec(Output);
