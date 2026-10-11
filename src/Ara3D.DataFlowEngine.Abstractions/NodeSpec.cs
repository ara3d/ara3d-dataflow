using System.Collections.Generic;

namespace Ara3D.DataFlowEngine.Abstractions;

/// <summary>
/// Pure nodes may be evaluated freely and memoized; Effect nodes execute only inside an explicit Run.
/// </summary>
public enum NodeCapability
{
    Pure,
    Effect,
}

public sealed record NodeSpec(
    string Kind,
    int Version,
    NodeCapability Capability,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    IReadOnlyList<ParamSpec> Params,
    string Description = "",
    IReadOnlyList<SchemaSpec>? Schemas = null)
{
    /// <summary>The declared columns of the Table and Relation outputs, at most one per output;
    /// empty for a node that declares none.</summary>
    public IReadOnlyList<SchemaSpec> Schemas { get; init; } = Schemas ?? [];
}
