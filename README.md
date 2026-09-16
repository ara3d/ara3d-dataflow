# Ara3D DataFlow

A deterministic, replayable dataflow engine for tables and scalars, with the
specification that defines it.

A graph is a pure function from content-hashed inputs to values. Nothing changes
the world except an explicit Run, and a Run freezes into a record that can be
replayed and checked later. That makes the engine suitable for work whose answer
has to be defensible: compliance checks, reconciliation, analysis over tabular
data with sign-off.

## Contents

| Directory | What it holds |
|---|---|
| `spec/dataflow-graph` | The normative specification in four independently versioned parts: format, semantics, expressions, runs. Each part ships JSON schemas and conformance vectors. |
| `src/Ara3D.DataFlowEngine.Abstractions` | The node SDK: value kinds, port and parameter descriptors, the node contract, the registry. Every node pack compiles against this and nothing else. |
| `src/Ara3D.NodeGraph` | The graph document model: load, save, validate, transactional editing. Knows nothing about evaluation. |
| `src/Ara3D.NodeGraph.Migrations` | Version-to-version document upgrades. |
| `src/Ara3D.DataFlowEngine` | The canonical evaluator: value hashing, dependency scheduling, memoization, dirty propagation, standing sessions. |
| `src/Ara3D.DataFlowEngine.Expressions` | Parser, type checker, and evaluator for the expression language used by derive and filter nodes. |
| `src/Ara3D.DataFlowEngine.Runs` | Freezing an evaluation into a run record and replaying it. |
| `src/Ara3D.DataFlowEngine.TestKit` | The spec's `test.*` node vocabulary, a fluent graph builder, and evaluation assertions, shipped as a package for node-pack authors. |
| `tests/` | Unit tests per project plus `Ara3D.DataFlowEngine.Conformance`, which runs every vector under `spec/`. |

## Design principles

1. Determinism is normative. Same graph, same inputs, same outputs. Observed nondeterminism is a bug.
2. Content identity, never location. Values and graphs are SHA-256 hashed with a byte encoding the spec pins down.
3. Pure by default, effects gated. Pure nodes memoize and evaluate freely. Effect nodes run only inside a Run, in a fixed order.
4. Evaluation is evidence. The run record pins graph hash, input hashes, and outputs.
5. Presentation is strippable. Layout and session layers never affect hashes or dirtiness.
6. Spec first, one canonical implementation, conformance vectors decide. Any other implementation passes the same vectors or is wrong.
7. Tiny node SDK. The Abstractions project changes rarely because churn there is churn everywhere.

## Building

Requires the .NET 8 SDK.

```bash
dotnet build Ara3D.DataFlow.slnx
dotnet test Ara3D.DataFlow.slnx
```

The `Ara3D.*` package dependencies come from nuget.org. When this repo is
checked out as a submodule of a host repo, the host's `Directory.Build.props`
and `nuget.config` take precedence, so package versions and feeds follow the host.

## What this repo does not contain

No BIM, no I/O beyond reading graph and run documents, and no node packs. Node
packs live with the products that use them. The first consumer is
[BIM Open Toolkit](https://github.com/ara3d/bim-open-toolkit), which includes
this repo as `submodules/ara3d-dataflow`.
