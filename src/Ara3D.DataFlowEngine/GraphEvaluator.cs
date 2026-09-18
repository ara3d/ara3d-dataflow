using System.Threading;
using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine;

/// <summary>One-shot evaluation: a fresh session, one pass, one snapshot.</summary>
public static class GraphEvaluator
{
    public static EvalSnapshot Evaluate(this GraphDocument doc, INodeRegistry registry, CancellationToken ct = default)
        => new EvalSession(registry).SetDocument(doc, ct);

    /// <summary>One-shot Run: a fresh session, one standing pass, then the Run over it.</summary>
    public static EvalSnapshot Run(this GraphDocument doc, INodeRegistry registry, CancellationToken ct = default)
    {
        var session = new EvalSession(registry);
        session.SetDocument(doc, ct);
        return session.Run(ct);
    }
}
