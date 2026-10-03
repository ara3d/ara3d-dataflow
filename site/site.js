// Renders the conformance vectors that site/build.mjs bundles into vectors.js.
// Nothing here evaluates a graph: every count, order, output, and hash shown
// is read from a vector's "expect" member.
"use strict";

const SPEC = window.DATAFLOW_SPEC;
const SVG = "http://www.w3.org/2000/svg";
const esc = s => String(s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const json = v => JSON.stringify(v, null, 2);
const el = (tag, attrs = {}, text) => {
  const e = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v);
  if (text !== undefined) e.textContent = text;
  return e;
};

/** Canonical text of a serialized value such as {kind:"Integer", value:12}. */
const showValue = v => v === null || v === undefined ? "null"
  : `${v.kind}: ${v.value === null ? "null" : JSON.stringify(v.value)}`;

/** One line per step, as the vector states it. */
const stepLabel = (s, i) => {
  const n = `${i + 1}. ${s.action}`;
  return s.action === "setValue" ? `${n} ${s.node}.${s.param} = "${s.value}"` : n;
};

// ---------- the graph drawing ----------

const NODE_W = 172, NODE_H = 78, COL_GAP = 92, ROW_GAP = 24, PAD = 20;

/** Columns by longest path from a source; rows by node id within a column. */
function layout(doc) {
  const nodes = doc.structure.nodes, edges = doc.structure.edges;
  const src = e => e.from.split(".")[0], dst = e => e.to.split(".")[0];
  const depth = Object.fromEntries(nodes.map(n => [n.id, 0]));
  for (let pass = 0; pass < nodes.length; pass++)
    for (const e of edges) depth[dst(e)] = Math.max(depth[dst(e)], depth[src(e)] + 1);
  const cols = [];
  for (const n of [...nodes].sort((a, b) => a.id < b.id ? -1 : 1)) (cols[depth[n.id]] ??= []).push(n);
  const pos = {};
  cols.forEach((col, c) => col.forEach((n, r) => {
    pos[n.id] = { x: PAD + c * (NODE_W + COL_GAP), y: PAD + r * (NODE_H + ROW_GAP) };
  }));
  const rows = Math.max(...cols.map(c => c.length));
  return { pos, width: PAD * 2 + cols.length * NODE_W + (cols.length - 1) * COL_GAP, height: PAD * 2 + rows * NODE_H + (rows - 1) * ROW_GAP };
}

/** Port names a node uses, from the edges that touch it. */
function ports(doc, id, side) {
  const key = side === "in" ? "to" : "from";
  const names = doc.structure.edges.map(e => e[key].split(".")).filter(([n]) => n === id).map(([, p]) => p);
  return [...new Set(names)].sort();
}

function portY(doc, id, side, port, pos) {
  const list = ports(doc, id, side);
  return pos[id].y + NODE_H * (list.indexOf(port) + 1) / (list.length + 1);
}

function drawGraph(svg, doc, values, step) {
  svg.replaceChildren();
  const { pos, width, height } = layout(doc);
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  svg.setAttribute("width", width);

  for (const e of doc.structure.edges) {
    const [a, ap] = e.from.split("."), [b, bp] = e.to.split(".");
    const x1 = pos[a].x + NODE_W, y1 = portY(doc, a, "out", ap, pos);
    const x2 = pos[b].x, y2 = portY(doc, b, "in", bp, pos);
    const mx = (x1 + x2) / 2;
    svg.append(el("path", { class: "edge", d: `M${x1},${y1} C${mx},${y1} ${mx},${y2} ${x2},${y2}` }));
    svg.append(el("text", { class: "port", x: x2 - 4, y: y2 - 4, "text-anchor": "end" }, bp));
  }

  const execs = step?.expect?.executions;
  for (const n of doc.structure.nodes) {
    const { x, y } = pos[n.id];
    const count = execs ? (execs[n.id] ?? 0) : undefined;
    const pending = execs && n.kind === "test.effect" && step.action !== "run";
    const state = count > 0 ? "ran" : pending ? "pending" : "";
    const g = el("g", { class: `node ${state}` });
    g.append(el("rect", { x, y, width: NODE_W, height: NODE_H, rx: 7 }));
    g.append(el("text", { class: "id", x: x + 10, y: y + 20 }, n.id));
    g.append(el("text", { class: "kind", x: x + NODE_W - 10, y: y + 20, "text-anchor": "end" }, `${n.kind} v${n.version}`));
    const v = values[n.id];
    if (v) g.append(el("text", { class: "param", x: x + 10, y: y + 42 }, `${v.kind ? v.kind + " " : ""}value = "${v.value}"`));
    const badge = count === undefined ? "" : count > 0 ? `executed ×${count}` : pending ? "pending, not executed" : "not executed";
    g.append(el("text", { class: "badge", x: x + 10, y: y + NODE_H - 12 }, badge));
    svg.append(g);
  }
}

// ---------- the vector viewer ----------

const viewable = [...SPEC.vectors.semantics, ...SPEC.vectors.runs];
let current = null, currentStep = 0;

/** Pairs each input step with its expected outcome, where the vector gives one. */
const stepsOf = v => (v.input.steps ?? []).map((s, i) => ({ ...s, expect: v.expect.steps?.[i] }));

/** The values layer after applying every setValue up to and including step i. */
function valuesAt(v, i) {
  const values = structuredClone(v.input.document?.values ?? {});
  stepsOf(v).slice(0, i + 1).filter(s => s.action === "setValue")
    .forEach(s => { values[s.node] = { ...values[s.node], [s.param]: s.value }; });
  return values;
}

function detail(v, step, i, last) {
  const rows = [];
  if (step.expect?.effectOrder) rows.push(["Effects executed, in order", step.expect.effectOrder.length ? step.expect.effectOrder.join(", ") : "none"]);
  if (last && v.expect.outputs)
    for (const [port, val] of Object.entries(v.expect.outputs)) rows.push([`Output ${port} after the last step`, showValue(val)]);
  let html = rows.length ? `<dl>${rows.map(([k, d]) => `<dt>${esc(k)}</dt><dd>${esc(d)}</dd>`).join("")}</dl>` : "";
  if (step.action === "run" && v.expect.record)
    html += `<p class="dim">The run record this step must produce. Timestamp and engine version are not compared, so the vector leaves them out.</p><pre>${esc(json(v.expect.record))}</pre>`;
  if (step.action === "replay") {
    if (v.input.record) html += `<p class="dim">The record being replayed:</p><pre>${esc(json(v.input.record))}</pre>`;
    if (step.providedInputs?.length) html += `<p class="dim">Inputs provided to the replay:</p><pre>${esc(json(step.providedInputs))}</pre>`;
    const r = v.expect.replay;
    html += `<dl><dt>Expected replay outcome</dt><dd class="${r.outcome === "ok" ? "ok" : "bad"}">${esc(r.outcome)}${r.node ? ` (node ${esc(r.node)}, parameter ${esc(r.param)})` : ""}</dd></dl>`;
  }
  return html;
}

function showStep(i) {
  currentStep = i;
  const v = current, steps = stepsOf(v), step = steps[i];
  document.querySelectorAll("#v-steps button").forEach((b, j) => b.setAttribute("aria-selected", String(j === i)));
  const svg = document.getElementById("v-graph");
  svg.parentElement.hidden = !v.input.document;
  document.querySelector(".legend").hidden = !v.input.document || !steps.some(s => s.expect?.executions);
  if (v.input.document) drawGraph(svg, v.input.document, valuesAt(v, i), step);
  document.getElementById("v-detail").innerHTML = detail(v, step, i, i === steps.length - 1);
}

function showVector(v) {
  current = v;
  document.querySelectorAll("#vector-list button").forEach(b => b.setAttribute("aria-selected", String(b.dataset.case === `${v.part}/${v.case}`)));
  document.getElementById("v-title").textContent = `${v.part}/${v.case}`;
  document.getElementById("v-desc").textContent = v.description;
  const bar = document.getElementById("v-steps");
  bar.replaceChildren(...stepsOf(v).map((s, i) => {
    const b = document.createElement("button");
    b.type = "button";
    b.setAttribute("role", "tab");
    b.textContent = stepLabel(s, i);
    b.onclick = () => showStep(i);
    return b;
  }));
  showStep(0);
}

function buildList() {
  const nav = document.getElementById("vector-list");
  for (const part of ["semantics", "runs"]) {
    const h = document.createElement("div");
    h.className = "part";
    h.textContent = `${part} ${SPEC.versions[part]}`;
    nav.append(h);
    for (const v of SPEC.vectors[part]) {
      const b = document.createElement("button");
      b.type = "button";
      b.dataset.case = `${v.part}/${v.case}`;
      b.textContent = v.case;
      b.onclick = () => showVector(v);
      nav.append(b);
    }
  }
}

// ---------- the tables ----------

function exprTable() {
  document.getElementById("expr-version").textContent = `version ${SPEC.versions.expressions}`;
  const env = e => Object.entries(e).map(([k, b]) => `${k}: ${showValue(b.value)}`).join("\n") || "none";
  const expected = x => x.error ? `<span class="bad">${esc(x.error)} error</span>` : `${esc(x.type)} &rarr; <code>${esc(showValue(x.value))}</code>`;
  document.querySelector("#expr-table tbody").innerHTML = SPEC.vectors.expressions.map(v =>
    `<tr title="${esc(v.description)}"><td><code>${esc(v.case)}</code></td><td><code>${esc(v.input.expression)}</code></td>` +
    `<td><code>${esc(env(v.input.environment ?? {}))}</code></td><td>${expected(v.expect)}</td></tr>`).join("");
}

function formatTable() {
  const expected = x => x.valid
    ? `<span class="ok">valid</span>${x.equalGraphHash ? ", and the stripped document has the same graph hash" : ""}` +
      `${x.graphHash ? `<br><code>graph hash ${esc(x.graphHash.slice(0, 16))}&hellip;</code>` : ""}`
    : `<span class="bad">invalid</span>: ${esc(x.reason)}`;
  document.querySelector("#format-table tbody").innerHTML = SPEC.vectors.format.map(v =>
    `<tr><td><code>${esc(v.case)}</code></td><td>${esc(v.description)}` +
    Object.entries(v.input).map(([k, d]) => `<details><summary>${esc(k)}</summary><pre>${esc(json(d))}</pre></details>`).join("") + `</td>` +
    `<td>${expected(v.expect)}</td></tr>`).join("");
}

buildList();
showVector(SPEC.vectors.semantics.find(v => v.case.startsWith("002")) ?? viewable[0]);
exprTable();
formatTable();
