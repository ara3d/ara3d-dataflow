using System.Collections.Generic;
using System.Linq;

namespace Ara3D.DataFlowEngine.Abstractions;

public static class SchemaSpecValidation
{
    /// <summary>Why a node's schema declarations are wrong, one message each; empty when they are fine.
    /// Each spec must name an existing Table or Relation output once, an inferred spec needs
    /// <see cref="ISchemaNode"/>, and a static spec's kinds must name its own columns.</summary>
    public static IReadOnlyList<string> SchemaProblems(this IFlowNode node)
    {
        var spec = node.Spec;
        var problems = new List<string>();
        foreach (var s in spec.Schemas)
        {
            var port = spec.Outputs.Where(o => o.Name == s.Output).Cast<PortSpec?>().FirstOrDefault();
            if (port is null)
                problems.Add($"{spec.Kind}: schema names unknown output '{s.Output}'");
            else if (port.Value.Type is not (PortType.Table or PortType.Relation))
                problems.Add($"{spec.Kind}: schema names output '{s.Output}' of type {port.Value.Type}, not Table or Relation");
            if (s is InferredSchemaSpec && node is not ISchemaNode)
                problems.Add($"{spec.Kind}: inferred schema for '{s.Output}' but the node does not implement ISchemaNode");
            if (s is StaticSchemaSpec { Kinds: { } kinds } st)
                problems.AddRange(kinds.Keys.Where(c => !st.Schema.Has(c))
                    .Select(c => $"{spec.Kind}: kind given for column '{c}' that is not in the schema of '{s.Output}'"));
        }
        problems.AddRange(spec.Schemas.GroupBy(s => s.Output).Where(g => g.Count() > 1)
            .Select(g => $"{spec.Kind}: output '{g.Key}' has more than one schema"));
        return problems;
    }

    /// <summary>The problems of every node in the registry.</summary>
    public static IReadOnlyList<string> SchemaProblems(this INodeRegistry registry)
        => registry.Nodes.SelectMany(n => n.SchemaProblems()).ToList();
}
