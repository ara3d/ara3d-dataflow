# Ara3D.DataFlowEngine

The canonical evaluator for dataflow graph documents: value hashing, dependency
scheduling, memoization, dirty propagation, and standing evaluation sessions.
Executes any registered node vocabulary (`INodeRegistry` from
`Ara3D.DataFlowEngine.Abstractions`) over an `Ara3D.NodeGraph` document.
Contains no I/O and no BIM.

## What it provides

- `ValueHash.Compute(FlowValue)` — normative content hash per the spec's
  semantics part §1.1 (tagged byte encodings, canonical NaN, SHA-256),
  returned as plain lowercase hex.
- `MemoKey.Compute(...)` — the memo key: (kind, version, values-layer params,
  input value hashes per connected port). No node id, so identical work shares
  one cache entry.
- `EvalSession` — a standing evaluation over a mutable current document.
  `SetDocument`/`UpdateDocument` validate (via `GraphValidation`), evaluate one
  pass in topological order (ties by node id, ordinal), commit atomically, and
  notify observers. Observers subscribe to all passes or to one node's changes.
- `GraphEvaluator.Evaluate(doc, registry)` — one-shot convenience.
- In a standing pass Effect nodes are not executed: their inputs are computed
  and exposed (`NodeResult.EffectInputs`), the node reports `EffectPending`,
  and its downstream is `Unavailable`.
- `EvalSession.Run(ct)` is the spec's Run (semantics §6): one pass over the
  current document with `IsRun` true, Pure nodes from the memo cache where
  unchanged, every reachable ready Effect node executed exactly once in
  topological order (never memoized), and its downstream evaluated over the
  effect's outputs. The run snapshot becomes the session's current snapshot,
  so observers see it; the next standing pass returns effects to pending.
  `GraphEvaluator.Run(doc, registry)` is the one-shot form and
  `snapshot.ExecutedEffects(registry)` lists the effects that ran, in order.
  Freezing a run snapshot into a record is the Runs project's concern.
- A node that throws reports `Error` and poisons only its own downstream
  (`Unavailable` with `BlockingNodeId`); independent branches are unaffected.
- Cancellation via `IEvalContext.Cancellation`; a cancelled pass leaves the
  previous snapshot current.

## Provenance

New code, written for the BimOpenFlow rewrite (2026-08-31), implementing
`spec/dataflow-graph/semantics/semantics.md` against the frozen wave-0 seams
in `CONTRACTS.md`.
