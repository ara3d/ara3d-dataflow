# Ara3D DataFlow

A dataflow engine for .NET whose answers can be reproduced and checked by
someone else, plus the written specification that defines what "correct" means.
A graph of nodes computes tables and scalar values from its inputs. The engine
guarantees that the same graph over the same inputs always produces the same
outputs, keeps the graph file free of results so it can be reviewed like source
code, refuses to write anything to the outside world until asked, and can freeze
one evaluation into a record that anyone with the engine can replay and verify.

It is for developers building tools where a computed answer has to be defended
later: compliance checks, quantity takeoffs, reconciliation, audits, and any
analysis that ends with a sign-off. The first consumer is
[BIM Open Toolkit](https://github.com/ara3d/bim-open-toolkit), which uses it to
analyze building models. Nothing in this repository knows about buildings.

## Goals

The engine exists to make five things true at once. Each one is easy alone;
holding all five is the design problem.

1. **The same question gives the same answer.** Evaluate a graph twice, on two
   machines, a year apart, and every output is bit-identical. Nondeterminism is
   treated as a bug, not a tolerance.
2. **The graph file is source, not a snapshot.** It holds the question, never
   the answer. It is small, diffable, and reviewable in a pull request, and
   moving a node on the canvas does not change what the graph means.
3. **Looking is safe.** An editor or an agent can evaluate a graph continuously
   while someone edits it, and nothing on disk or in a model changes until a
   person or agent explicitly starts a Run.
4. **A finished evaluation is evidence.** A Run freezes into a record that pins
   the graph, every external input, and every output by content hash. A third
   party can replay it and get a binary verdict: reproduced, or not, and where
   it diverged.
5. **Correctness is defined outside any one implementation.** A written
   specification and a set of conformance vectors say what the engine must do.
   The C# engine passes them, and any second implementation must pass the same
   files or it is wrong.

## What it does not do

- It does not read or write files, models, databases, or the network. Nodes do
  that, and nodes live in other repositories.
- It does not ship any useful nodes. The only node vocabulary here is the
  `test.*` set used by the conformance suite.
- It does not support cycles, loops, or iteration. A graph with a cycle is
  invalid.
- It does not stream or process tables incrementally. A node receives whole
  tables and returns whole tables.
- It does not evaluate in parallel or across machines. Evaluation is
  single-threaded and synchronous.
- It does not roll back effects. If the third of five export nodes fails, the
  first two have already written their files.
- It does not sign run records. The signing interface exists; no implementation
  does.
- It does not orchestrate tasks, people, or approvals. It is a dataflow engine,
  not a workflow engine.

## A small example

A graph document is a JSON file with the extension `.dfg.json`. This one adds
two constants:

```json
{
  "formatVersion": "0.1.0",
  "structure": {
    "edges": [
      { "from": "a.out", "to": "sum.a" },
      { "from": "b.out", "to": "sum.b" }
    ],
    "nodes": [
      { "id": "a", "kind": "test.const", "version": 1 },
      { "id": "b", "kind": "test.const", "version": 1 },
      { "id": "sum", "kind": "test.add", "version": 1 }
    ]
  },
  "values": {
    "a": { "kind": "Integer", "value": "5" },
    "b": { "kind": "Integer", "value": "7" }
  }
}
```

Building and evaluating the same graph in C#, using the test kit:

```csharp
using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.DataFlowEngine.TestKit;

var doc = Graph
    .Node("a", "test.const", ("value", "5"))
    .Node("b", "test.const", ("value", "7"))
    .Node("sum", "test.add")
    .Connect("a.out", "sum.a")
    .Connect("b.out", "sum.b")
    .Build();

var session = new FlowTestSession();
session.Evaluate(doc);
var total = (IntegerValue)session.Output("sum.out");   // total.Value == 12
```

Freezing that evaluation produces a run record. The hashes below are real: they
are frozen in `spec/dataflow-graph/runs/conformance/001-run-and-replay.json`
and the conformance suite fails if the engine ever computes different ones.

```json
{
  "runVersion": "0.1.0",
  "graphHash": "f24e18d761d065b1ab61fdc3b42f4405e8eaeca569f723ad1e0344b56edf660d",
  "inputs": [],
  "nodeOutputs": {
    "a.out":   "054cf7d9d119099e90e449f8448d9b53e00b51508f5f56a2dfd58151ffdbe8ce",
    "b.out":   "95d6f8b53c8e5c1d2e3c443dfee0705a7ebdff9f6442377ef935ecc87dd1695a",
    "sum.out": "2770f01dcc36089e038d75ede7c4c008d37d30d604f0c59cae40d90b8d367eaa"
  },
  "recordedOutputs": {
    "sum.out": { "kind": "Integer", "value": 12 }
  },
  "effects": []
}
```

## Principles

Each principle below states the rule, why it exists, and the other designs that
were available. The alternatives are listed so a reader can judge whether the
choice fits their problem, and so a future change knows what it is trading.

### 1. Determinism is normative

A node's outputs must be a function of exactly three things: its kind and
version, its parameter values, and the values on its connected input ports. No
clock, no randomness, no ambient files, no dependence on evaluation order or on
another node's state. A node that needs external content must take it as an
input value or name it through a content-hashed parameter.

**Why.** Every other goal rests on this one. Replay can only give a binary
verdict if a divergence always means something changed. Memoization is only
sound if re-running a node could never produce a different answer. Live
evaluation can only be trusted if observing never perturbs.

**Alternatives.**

- *Tolerant determinism*: allow floating-point drift within an epsilon. This
  makes replay a judgment call and makes hashes useless as identity. Rejected.
- *A "volatile" node capability* that opts a node out of memoization and
  replay. This is a plausible future extension for nodes such as "current
  time", but every volatile node poisons the evidence value of everything
  downstream, so the first version leaves it out.
- *Determinism by convention only*, with no spec text. This is what most
  visual dataflow tools do, and it is why their outputs cannot be used as
  evidence.

### 2. Identity is content, never location

Values, graphs, and external inputs are identified by a SHA-256 hash over a
byte encoding the spec pins down exactly: a tag byte per kind, little-endian
integers, canonical NaN, UTF-8 text with a length prefix, tables column by
column. Two values are equal if and only if their hashes are equal. A graph's
identity is the hash of its canonical JSON. A file input is identified by the
hash of its bytes, not by its path.

**Why.** Paths lie: the file behind them changes. Object identity does not
survive reload. In-memory structural equality is not portable across processes
or implementations. A content hash is the only identity that works in a memo
key, in a run record on disk, and in a second implementation written in another
language.

**Alternatives.**

- *Path plus modification time*, the build-tool convention. Cheap, but a
  record that says "the file at this path on that date" cannot be verified
  later.
- *Structural equality in memory*. Fine for a single process, useless as
  evidence.
- *A faster non-cryptographic hash* such as xxHash. The spec fixes SHA-256 for
  version 0.1 and notes that an algorithm field can arrive with signing. The
  cost is real: hashing a table walks every cell.

### 3. Pure by default, effects only inside a Run

Every node declares a capability: `Pure` or `Effect`. Pure nodes evaluate
freely, are memoized, and may run any number of times. Effect nodes never
execute during ordinary evaluation. Outside a Run they report `EffectPending`,
their inputs are computed and exposed, and their downstream is unavailable. A
Run first brings every Pure node up to date, then executes each reachable
Effect node exactly once in topological order with ties broken by node id.
The library implements the gating. The Run driver that executes the pending
effects is not yet part of it; see the status section.

**Why.** An editor evaluates a graph on every keystroke. If a node that writes
a CSV could run during that, editing would spray files across the disk. Gating
effects behind an explicit Run makes "looking is safe" a structural property
rather than a convention every node author has to remember. Fixing the effect
order makes the observable behavior of a Run deterministic too.

**Alternatives.**

- *No distinction*, the spreadsheet and Grasshopper default. Simpler, and it
  makes live preview unsafe.
- *Effects as separate actions outside the graph*, triggered by the host.
  This loses the guarantee that an effect's inputs come from the same
  consistent snapshot as everything else.
- *Transactional effects with rollback*. Desirable and deferred. Version 0.1
  states plainly that effects already executed are not rolled back when a
  later one fails.
- *A capability lattice* finer than two values, such as "reads external
  state" versus "writes external state". The two-value split is enough to
  make the safety rule enforceable by project reference alone: a host can
  put every Effect node in one assembly.

### 4. A Run record is the evidence, and the graph is the replay instrument

A graph keeps evolving. What gets archived, handed to a reviewer, or one day
signed is a run: the graph hash, every external input's content hash, every
node output's value hash, the serialized values of the terminal outputs, which
effects ran in what order, a UTC timestamp, and the engine version. Replay
checks the graph hash, checks the input hashes, recomputes effect-free, and
reports the first divergence with its node id.

**Why.** An analysis and its result have different lifecycles. The analysis is
edited and reused across projects. The result is a fact about one moment. Mixing
them in one file, as notebooks do, produces stale results, dirty diffs, and
permanent confusion about which values were typed and which were computed. The
[design document](https://github.com/ara3d/bim-open-toolkit/blob/main/docs/platoflow/platoflow-graph-semantics.md)
behind this engine calls the graph "a notebook that never saves its outputs"
and makes saving them a deliberate act with its own file.

**Alternatives.**

- *Embed outputs in the graph file*, the Jupyter model. Rejected for the
  reasons above.
- *Sign the graph*. The wrong object: it proves what was asked, not what was
  answered.
- *Keep results only in a host database*. Not portable, and not something a
  third party can verify with the engine alone.
- *Embed the graph document inside the record.* Deferred; the record carries
  the graph hash and the document travels beside it.

### 5. Presentation is strippable

A graph document has four layers. `structure` (nodes and edges) and `values`
(parameter values) determine evaluation. `layout` (canvas positions) and
`session` (camera, display flags) are presentation. The graph hash covers only
the first two. Removing the last two changes nothing about evaluation.

**Why.** Editors need somewhere to keep canvas state, and that state should
travel with the file. Putting it in the same file without excluding it from
identity would mean every nudge of a node changed the graph's hash and
invalidated every run record. Two documents with equal graph hashes are the
same analysis, whatever they look like on screen.

**Alternatives.**

- *One flat document with everything hashed.* Simple and wrong for the
  reasons above.
- *A separate sidecar file for layout.* Cleaner identity, but two files to
  keep in sync, and users move one and forget the other.
- *Layout stored only by the editor*, never in the file. The graph then
  looks different on every machine.

### 6. The specification is the authority, and vectors decide

`spec/dataflow-graph` holds four independently versioned parts: format,
semantics, expressions, and runs. Each ships JSON schemas and a directory of
numbered conformance vectors. The C# engine is the canonical implementation
because it passes every vector. When the engine and the spec disagree, one of
them has a bug and the fix lands in both in the same change.

**Why.** A run record is only evidence if a reader can trust that a different
engine, or the same engine five years later, computes the same hashes. That
trust has to rest on a document and a test corpus, not on whatever the code
happened to do. Independent versioning of the four parts lets the expression
language grow without touching the document format.

**Alternatives.**

- *The implementation is the spec.* Cheaper to start, and it leaves a second
  implementation, such as a TypeScript preview evaluator in a browser, with
  nothing to test against.
- *Two independent implementations from day one*, cross-checked. Stronger
  and much more expensive.
- *Frozen expected values written by hand.* Impractical for hashes. The
  compromise is that a vector may ship `"TBD-by-engine"`, the first canonical
  engine run fills it, and from then on a changed hash is a breaking change
  rather than a test update.

### 7. The node contract is tiny and stable

`Ara3D.DataFlowEngine.Abstractions` is 297 lines. It defines the five value
kinds, port and parameter descriptors, the node specification, the evaluation
context, the node interface, and the registry. Every node pack and the engine
compile against it and nothing else.

**Why.** A change to this assembly is a change to every node pack ever
written. Keeping it small keeps it still, and a still contract is what lets
many people, or many agents, write nodes in parallel without coordinating.

**Alternatives.**

- *A rich SDK* with helpers for table access, parameter parsing, and
  validation. Convenient, and every helper is a future breaking change.
  Helpers live in the packs instead.
- *Generic or structural port types* such as "a table with these columns".
  Deferred. Ports are the five kinds plus `Any`, and nodes validate table
  shape at evaluation time.

### 8. Documents are immutable values

A `GraphDocument` is an immutable record. Every editing operation returns a
new document and leaves the input untouched. Two documents are equal when their
canonical serializations are equal. Undo and redo are a stack of whole
documents.

**Why.** Immutable documents make the standing evaluation session simple: a
snapshot is a document plus results, it is never half-updated, and observers can
hold a reference without fear. Whole-document undo is a few lines of code
because documents are small, typically kilobytes.

**Alternatives.**

- *A mutable model with change events.* The usual editor design, and the
  usual source of torn reads and stale observers.
- *Operation-based undo* (a command pattern). Necessary when documents are
  large; unnecessary here, and it doubles the code every editing operation
  has to carry.

### Smaller decisions

| Decision | Chosen | Other options | Reason |
|---|---|---|---|
| Parameter values in the file | Always JSON strings in a canonical form | Typed JSON values | Keeps 64-bit integers exact, keeps the layer uniform, makes hashing trivial. Cost: nodes parse strings on every fresh evaluation. |
| Conversion at edges | None. Kinds must match exactly unless one side is `Any` | Implicit Integer to Number widening | Edge semantics stay trivial and a value arrives bit-identical. The expression language widens internally. Widening at edges is explicitly deferred. |
| Null | Exists only inside table cells and expressions; any null operand makes an operator yield null; `coalesce` is the escape | SQL three-valued logic; null as an error | One rule with no special cases. `x == null` is null, never true, so absence is tested with `coalesce`. |
| Evaluation strategy | Whole graph, eager, single-threaded, memoized | Lazy pull evaluation; parallel branches | The simplest scheduler is the easiest to prove deterministic. The spec permits any dependency-respecting order for Pure nodes, so parallel evaluation is a permitted future change. |
| Memo key | Kind, version, parameter values, and the hash of each connected input | Include node id | Omitting the id lets two identical nodes share one cache entry. |
| Cache lifetime | Transient and unbounded; never persisted | Persistent cache; eviction policy | Reload recomputes cheaply through the memoizer. An eviction policy is a future change that no vector constrains. |
| Effect ordering | Topological, ties by node id ascending by code point | Any order; author order | Effects are observable, so their order must be reproducible. |
| Format migrations | Raw JSON text transforms in a separate project | Versioned document model | An old shape the current model cannot represent never complicates the current model. |
| Number formatting | .NET round-trip ("R") invariant | A specified shortest-round-trip algorithm | Pragmatic for one .NET implementation. The spec flags that a second implementation must reproduce it byte for byte. |

## How it works

### The document

A graph document is one JSON object with `formatVersion`, `structure`,
`values`, and optionally `layout` and `session`. Any other top-level member
makes it invalid. Node ids match `[A-Za-z0-9_-]+` and contain no dot, because
the dot separates node from port in an edge endpoint such as `sum.a`. A node
references its kind by a dotted string and an integer version, and that pair is
its identity for evaluation and memoization.

Writers always emit canonical JSON: UTF-8 without a byte-order mark, LF line
endings, two-space indentation, every object key sorted by code point, nodes
sorted by id, edges sorted by target, integers plain, doubles in round-trip
form, minimal string escaping, and exactly one trailing newline. Loading and
saving a canonical file reproduces it byte for byte. The graph hash is SHA-256
over the canonical text of `{"structure": ..., "values": ...}` without the
trailing newline.

`Ara3D.NodeGraph` implements loading, saving, hashing, editing, undo, and
validation. Validation against a node registry reports duplicate ids, unknown
kinds, dangling edge endpoints, unknown ports, incompatible port types, two
edges into one input, and cycles, all at once, never by throwing.

### Evaluation

`EvalSession` holds one current document and its results. Setting or updating
the document validates it, runs one pass, commits the new snapshot atomically,
and notifies observers. On a validation error or a cancellation the previous
snapshot stays current.

A pass visits nodes in topological order, ties broken by node id. For each
node the evaluator gathers inputs from upstream results and decides a status:

| Status | Meaning |
|---|---|
| `Ok` | Evaluated, directly or from the memo cache. Outputs and their hashes are available. |
| `Unready` | A required input port has no edge, or an upstream node is unready. Not an error. |
| `EffectPending` | An Effect node outside a Run. Its inputs are captured; it did not execute. |
| `Unavailable` | An upstream node is in `Error` or `EffectPending`. The result names the blocking node. |
| `Error` | The node threw. The message is recorded and only this node's downstream is affected. |

A Pure node that is ready computes its memo key and, on a hit, reuses the
cached outputs without executing. On a miss it executes, its outputs are
hashed, and the entry is cached. An unconnected optional input port receives a
`MissingValue` placeholder that never flows along an edge and never enters a
memo key. The result of every node carries an execution count, which is what
the conformance vectors assert when they say a memo hit or a clean re-evaluation
executed nothing.

Dirty propagation falls out of the memo key rather than being tracked
separately. When a parameter changes, that node's key changes and it
re-executes. If its output hash is unchanged, every downstream node's key is
unchanged and they all hit the cache.

### Values and hashing

Five kinds flow along edges: Boolean, Integer (64-bit), Number (IEEE double),
Text, and Table. A table has ordered named columns of one scalar kind each, and
cells may be null. Ports are the five kinds plus `Any`. `ValueHash.Compute`
implements the encoding from the semantics part, section 1.1, and returns 64
lowercase hex characters.

### Runs and replay

`RunRecorder.Freeze` turns a snapshot into a `RunRecord`: it walks nodes in
topological order, records every `Ok` node's output hashes, embeds the full
value of every output port that has no outgoing edge, and lists executed
Effect nodes with their status. The caller supplies the external input
descriptors, the engine version string, and the timestamp, because the engine
has no clock and no I/O. The record serializes with the same canonical JSON
rules as a graph, so it is itself hashable and diffable.

`RunReplay.Replay` takes a record, a document, a registry, and the current
input hashes. It refuses with `GraphMismatch` or `InputMismatch` before
computing anything, then evaluates and compares every output hash, reporting
the first `OutputMismatch` by node and port.

### Expressions

Expression-kind parameters hold a small statically typed language over the
scalar kinds: literals, bare or `[bracket quoted]` identifiers bound to the
columns of a row, arithmetic, `&` for text concatenation, comparisons, `and`,
`or`, `not`, a right-associative conditional, and thirteen builtins from `abs`
to `coalesce`. Integer division always yields a Number. Integer overflow is a
deterministic evaluation error, never a wrap. The pipeline is
`Expression.Parse(text).Check(environment).Eval(lookup)`; parse and type
errors are collected with character offsets rather than thrown.

### Migrations

`GraphMigrator.MigrateToCurrent` reads `formatVersion`, chains registered
`IGraphMigration` steps to the current version, and returns canonical text. The
production registry is empty because 0.1.0 is the first format. The mechanism
is exercised by tests with fake migrations.

## Using it

### Prerequisites and build

The .NET 8 SDK. The three `Ara3D.*` package dependencies (`Ara3D.Utils`,
`Ara3D.Collections`, `Ara3D.DataTable`) come from nuget.org; the standalone
default version is pinned in `Directory.Build.props` as `Ara3DSdkVersion`.

When this repository is checked out as a submodule of a host repository, the
host's `Directory.Build.props` sits above this one and wins for every version
property it defines, so package versions follow the host. Standalone or inside
[BIM Open Toolkit](https://github.com/ara3d/bim-open-toolkit), these two
commands build and test without further setup:

```bash
dotnet build Ara3D.DataFlow.slnx
```

```bash
dotnet test Ara3D.DataFlow.slnx
```

### Writing a node

A node is a stateless class. One instance serves every evaluation.

```csharp
using Ara3D.DataFlowEngine.Abstractions;

public sealed class ScaleNode : IFlowNode
{
    public NodeSpec Spec { get; } = new(
        "math.scale", 1, NodeCapability.Pure,
        Inputs: [new PortSpec("in", PortType.Integer)],
        Outputs: [new PortSpec("out", PortType.Integer)],
        Params: [new ParamSpec("factor", ParamKind.Integer, "2")],
        "Multiplies an integer by the factor parameter.");

    public IReadOnlyList<FlowValue> Eval(IEvalContext context,
        IReadOnlyList<FlowValue> inputs, ParamValues parameters)
        => [new IntegerValue(parameters.GetInteger("factor", 2) * ((IntegerValue)inputs[0]).Value)];
}
```

Declare `NodeCapability.Effect` for a node that changes the world, and check
`context.IsRun` inside it as a second line of defense. Report a non-fatal
problem with `context.Warn`. Throw to report failure; the engine records the
message and poisons only the downstream. For one-off nodes in tests,
`DelegateNode` builds an `IFlowNode` from a spec and a lambda.

### Evaluating a graph

```csharp
using Ara3D.DataFlowEngine;
using Ara3D.DataFlowEngine.Abstractions;
using Ara3D.NodeGraph;

var registry = new NodeRegistry([new ScaleNode()]);
var doc = GraphDocumentIO.Load("analysis.dfg.json");
var snapshot = doc.Evaluate(registry);
var result = snapshot.Results["n1"];   // Status, Outputs, OutputHashes, Warnings
```

For an editor or a long-running host, keep a session and subscribe:

```csharp
var session = new EvalSession(registry);
using var subscription = session.Subscribe("n1", r => Console.WriteLine(r.Status));
session.SetDocument(doc);
session.UpdateDocument(d => d.SetParam("n1", "factor", "3"));
```

### Recording a run

```csharp
using Ara3D.DataFlowEngine.Runs;

var record = RunRecorder.Freeze(session.Snapshot, registry,
    inputs: [], "MyApp 0.1.0", DateTimeOffset.UtcNow);
record.Save("analysis.run.json");

var verdict = RunReplay.Replay(RunRecordJson.Load("analysis.run.json"), doc, registry, inputs: []);
// verdict.Outcome is Ok, GraphMismatch, InputMismatch, or OutputMismatch
```

## Trade-offs

- **Hashing costs a full pass over every value.** A fresh table evaluation
  walks every cell to hash it. Memoization repays this on the second
  evaluation, not the first.
- **The memo cache never evicts.** A long editing session over large tables
  grows without bound. Restart the session to clear it.
- **One thread, one graph at a time.** Independent branches could evaluate in
  parallel under the spec, and do not.
- **Strings for parameters** mean every node re-parses its parameters on
  every fresh evaluation and the document cannot express a typed schema on
  its own.
- **No edge conversion** means a Number output cannot feed an Integer input
  even when the value is whole. Insert a node or use an expression.
- **Effects are not transactional.** A partially completed Run leaves
  partial side effects.
- **Number formatting depends on .NET.** A second implementation must
  reproduce .NET's round-trip double formatting exactly or its graph hashes
  will differ.

## Status on 2026-09-16

The code was written from 2026-08-31 inside BIM Open Toolkit and moved into
this repository on 2026-09-15. Every spec part is version 0.1.0 and marked
Draft. The seven source projects total about 4,100 lines of C#.

**Tested.** `dotnet test` on 2026-09-16 with .NET 8, from the submodule
checkout inside BIM Open Toolkit: 609 tests passed, 0 failed, 3 skipped. The suites cover the document format (44 tests), the
evaluator (74), the expression language (422), run records (24), the test kit
(24), migrations (9), and the conformance runner (12 run, 3 skipped). The 28
conformance vectors in `spec/` all execute: 8 format, 4 semantics, 14
expressions (run from the expressions test project), and 2 runs. The three
skips are the conformance runner's own placeholders, described next.

**Specified but not implemented.**

- *No Run driver in the engine.* `EvalSession` never executes Effect nodes.
  The only code that does is a private driver inside the conformance test
  `SemanticsVectorTests`, which exists so the effect-gating vectors can run.
  `RunRecorder.Freeze` records whatever snapshot it is given, so a host that
  calls it after ordinary evaluation records a run in which no effect ran.
- *Replay skips Effect node outputs.* The spec says replay recomputes them as
  pure functions. `RunReplay` cannot yet, and skips them.
- *The runs conformance runner* verifies the frozen graph and value hashes,
  then ignores the record-creation and replay steps.
- *External inputs are not resolved by the engine.* The spec says the engine
  resolves `FilePath` and `ModelRef` parameters to content before evaluation.
  The engine hashes the path string in the memo key. A node that reads a file
  must hash the content itself, and the host must supply the content hashes to
  `RunRecorder.Freeze`.
- *Signing* is an interface, `IRunSigner`, with no implementation.
- *Expression environments are scalar-only.* The spec allows `Any` and Table
  bindings; the type checker rejects them.

**Spec and code divergences.**

- `ParamKind` in code has `Fraction` and `Percent`; the format spec's
  parameter table does not list them.
- Format validity rules 6 and 7 (keys in `values`, `layout`, and
  `session.display` must name existing nodes) are not enforced by the loader
  or the validator. A node id is checked only for being non-empty and free of
  dots; the kind id pattern is not checked at all.
- Catalog rule 4 (each parameter string parses as its declared kind) is
  enforced only for `Fraction` and `Percent`. Other kinds are parsed by the
  node at evaluation time.

## Related work

The engine combines ideas that each exist elsewhere. Its narrow claim is the
combination: a written spec with conformance vectors, content-hashed identity,
effect gating, and an evidence-grade run record, for tabular analysis at
desktop scale.

- **Grasshopper and Dynamo** are live visual dataflow editors over a model.
  Their definitions are not diffable or reviewable as text, and their nodes
  can write to the model during ordinary evaluation. This engine's file
  format and effect gating are the deliberate inversion of both.
- **Jupyter** embeds outputs in the notebook file. The run record exists so
  this engine never does.
- **dbt** treats derived datasets as source-controlled definitions with run
  artifacts kept separate. It is the closest match to the semantic core, for
  SQL warehouses rather than in-process tables.
- **Nix, Bazel, and other hermetic build systems** identify inputs and
  outputs by content hash and forbid undeclared dependencies. The
  determinism rule and the memo key follow the same reasoning applied to a
  graph that is edited live.
- **DVC and Pachyderm** version data pipelines by content hash with
  provenance. They target large files and distributed execution; this engine
  targets a single process.
- **Salsa and Adapton** are incremental computation frameworks keyed on
  input identity. They optimize recomputation inside a compiler; this engine's
  memoization is the same idea with a portable, spec-defined key.
- **Excel** is the closest cousin in audience: formulas and values in one
  file. Its known failure, stale or ambiguous values, is what separating the
  graph from the run is meant to avoid.

These comparisons describe those tools as understood on 2026-09-16 and may
age.

## Repository layout

| Path | Purpose |
|---|---|
| `spec/dataflow-graph/` | The normative specification in four parts, each with JSON schemas and conformance vectors. Read `README.md` there first, then format, semantics, expressions, runs. |
| `src/Ara3D.DataFlowEngine.Abstractions/` | The node contract: value kinds, ports, parameters, `NodeSpec`, `IFlowNode`, `IEvalContext`, `NodeRegistry`. |
| `src/Ara3D.NodeGraph/` | The document model: load and save in canonical form, graph hash, validation, pure editing operations, undo history. |
| `src/Ara3D.NodeGraph.Migrations/` | Version-to-version upgrades over raw JSON text. |
| `src/Ara3D.DataFlowEngine/` | The evaluator: topological scheduling, value hashing, memo keys and cache, `EvalSession`, node results. |
| `src/Ara3D.DataFlowEngine.Expressions/` | Lexer, parser, type checker, and evaluator for the expression language. |
| `src/Ara3D.DataFlowEngine.Runs/` | Run records, canonical JSON for them, freezing, replay, and the signing interface. |
| `src/Ara3D.DataFlowEngine.TestKit/` | The `test.*` node vocabulary, the fluent graph builder, `FlowTestSession`, assertions, and `DelegateNode`. Shipped as a package for node-pack authors. |
| `tests/` | One test project per source project, plus `Ara3D.DataFlowEngine.Conformance`, which discovers and runs every vector under `spec/`. |

Each source project has its own README with the details that matter to someone
editing it.

## Who it is for

Use it if you are building a tool in .NET where computed results must be
reproduced or audited later, where a graph will be evaluated live while it is
edited, or where more than one implementation of the same graph language must
agree. Use the test kit if you are writing a node pack for such a tool.

Do not use it yet if you need cycles or iteration, streaming over tables
larger than memory, parallel or distributed evaluation, transactional effects,
signed records, or a ready-made node library. Those are either out of scope or
listed above as not yet implemented.

## License

MIT. See `LICENSE`. Report problems at
https://github.com/ara3d/ara3d-dataflow/issues.
