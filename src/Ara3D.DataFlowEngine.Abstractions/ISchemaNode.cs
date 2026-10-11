using System.Collections.Generic;

namespace Ara3D.DataFlowEngine.Abstractions;

/// <summary>
/// What a schema pass knows about one input port. Schema is the inferred shape of a connected
/// Table or Relation input (null for any other port type and for an unconnected port); Value is
/// the input's value when the pass has one, such as a Database from an earlier run.
/// </summary>
public sealed record SchemaInput(PortType Type, bool Connected, SchemaResult? Schema = null, FlowValue? Value = null);

/// <summary>
/// Implemented by a node that declares an <see cref="InferredSchemaSpec"/>. Infer computes the
/// columns of one output without evaluating anything: it may read the parameters and call the
/// catalog, never the data. Inputs is parallel to Spec.Inputs, so a Database or scalar input is
/// as visible as a table. The output is named per call, so a node with several table outputs
/// answers each in turn. Errors come back as a failed SchemaResult, never as an exception.
/// </summary>
public interface ISchemaNode : IFlowNode
{
    SchemaResult Infer(string output, IReadOnlyList<SchemaInput> inputs, ParamValues parameters, ISchemaCatalog catalog);
}
