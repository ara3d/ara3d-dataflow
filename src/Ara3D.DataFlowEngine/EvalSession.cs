using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

namespace Ara3D.DataFlowEngine;

/// <summary>
/// A standing, synchronous, single-threaded evaluation over a mutable current
/// document. Each SetDocument/UpdateDocument validates, runs one pass (through
/// the memo cache, so unchanged nodes never re-execute), commits atomically,
/// then notifies observers. Run executes the current document's Effect nodes
/// (spec semantics §6) and commits that snapshot the same way; the next standing
/// pass returns them to pending. On cancellation or invalid input the previous
/// snapshot stays current.
/// </summary>
public sealed class EvalSession
{
    private static readonly EvalSnapshot EmptySnapshot = new(
        GraphDocument.Empty, new Dictionary<string, NodeResult>(), Array.Empty<string>());

    private readonly INodeRegistry _registry;
    private readonly MemoCache _memo = new();
    private Dictionary<string, int> _counts = new();
    private readonly List<Action<EvalSnapshot>> _observers = new();
    private readonly List<(string NodeId, Action<NodeResult> Observer)> _nodeObservers = new();

    public EvalSession(INodeRegistry registry)
        => _registry = registry;

    public EvalSnapshot Snapshot { get; private set; } = EmptySnapshot;

    public GraphDocument Document
        => Snapshot.Document;

    public NodeResult? GetResult(string nodeId)
        => Snapshot.Results.GetValueOrDefault(nodeId);

    /// <summary>Validates, evaluates, commits, and notifies. Throws InvalidGraphException on validation errors.</summary>
    public EvalSnapshot SetDocument(GraphDocument doc, CancellationToken ct = default)
        => Commit(doc, isRun: false, ct);

    /// <summary>
    /// A Run over the current document: Pure nodes come from the memo cache where
    /// unchanged, every reachable ready Effect node executes exactly once in
    /// topological order (ties by node id), and its downstream evaluates over the
    /// effect's outputs. The snapshot becomes current, so observers see the run.
    /// </summary>
    public EvalSnapshot Run(CancellationToken ct = default)
        => Commit(Document, isRun: true, ct);

    private EvalSnapshot Commit(GraphDocument doc, bool isRun, CancellationToken ct)
    {
        var errors = doc.Validate(_registry);
        if (errors.Count > 0)
            throw new InvalidGraphException(errors);

        var counts = new Dictionary<string, int>(_counts);
        var (results, warnings) = Evaluator.Pass(doc, _registry, _memo, counts, isRun, ct);

        var previous = Snapshot;
        _counts = counts;
        foreach (var id in _counts.Keys.Where(id => !results.ContainsKey(id)).ToList())
            _counts.Remove(id);
        Snapshot = new(doc, results, warnings);
        Notify(previous, Snapshot);
        return Snapshot;
    }

    public EvalSnapshot UpdateDocument(Func<GraphDocument, GraphDocument> edit, CancellationToken ct = default)
        => SetDocument(edit(Document), ct);

    /// <summary>Observes every completed pass with its full consistent snapshot.</summary>
    public IDisposable Subscribe(Action<EvalSnapshot> observer)
    {
        _observers.Add(observer);
        return new Subscription(() => _observers.Remove(observer));
    }

    /// <summary>
    /// Observes one node id; called after a pass only when that node's result
    /// changed (status, error, or output hashes). Not called when the node is
    /// absent from the document.
    /// </summary>
    public IDisposable Subscribe(string nodeId, Action<NodeResult> observer)
    {
        var entry = (nodeId, observer);
        _nodeObservers.Add(entry);
        return new Subscription(() => _nodeObservers.Remove(entry));
    }

    private void Notify(EvalSnapshot previous, EvalSnapshot current)
    {
        foreach (var observer in _observers.ToList())
            observer(current);
        foreach (var (nodeId, observer) in _nodeObservers.ToList())
            if (current.Results.TryGetValue(nodeId, out var result)
                && Changed(previous.Results.GetValueOrDefault(nodeId), result))
                observer(result);
    }

    private static bool Changed(NodeResult? before, NodeResult after)
        => before is null
           || before.Status != after.Status
           || before.Error != after.Error
           || !before.OutputHashes.SequenceEqual(after.OutputHashes);

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose()
            => dispose();
    }
}
