using System.Collections.Generic;
using System.Linq;
using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.DataFlowEngine;

/// <summary>Which Effect nodes a Run snapshot executed, in execution order (spec semantics §6).</summary>
public static class EffectOrder
{
    /// <summary>Effect nodes that ran (Ok or Error) in the snapshot, in topological order, ties by node id.</summary>
    public static IReadOnlyList<string> ExecutedEffects(this EvalSnapshot snapshot, INodeRegistry registry)
        => snapshot.Document.Sort()
            .Where(n => registry.Find(n.Kind, n.Version)!.Spec.Capability == NodeCapability.Effect
                        && snapshot.Results[n.Id].Status is NodeStatus.Ok or NodeStatus.Error)
            .Select(n => n.Id)
            .ToList();
}
