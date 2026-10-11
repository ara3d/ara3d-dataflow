using System;
using System.Collections.Generic;
using System.Linq;

namespace Ara3D.NodeGraph;

/// <summary>
/// Graph inputs (format.md §3a): the reference form, the resolution of every declared input to a
/// value (the caller's, else the default), and the substitution into the values layer. Pure; the
/// engine evaluates the substituted values, so memo keys and run records see the resolved text.
/// </summary>
public static class GraphInputs
{
    public const char Sigil = '$';

    /// <summary>The input name when <paramref name="value"/> is a reference (<c>$name</c>, the whole value); else null.
    /// <c>$$...</c> is the escape for a literal value that starts with a dollar sign, so it is not a reference.</summary>
    public static string? ReferencedName(string value)
        => value.Length > 1 && value[0] == Sigil && GraphInput.IsValidName(value[1..]) ? value[1..] : null;

    /// <summary>The literal text a non-reference value stands for: <c>$$x</c> reads <c>$x</c>.</summary>
    public static string Unescape(string value)
        => value.StartsWith("$$", StringComparison.Ordinal) ? value[1..] : value;

    /// <summary>The reference form of an input name, <c>$name</c>.</summary>
    public static string Reference(string name)
        => Sigil + name;

    /// <summary>Every (node id, parameter, input name) reference in the values layer.</summary>
    public static IEnumerable<(string NodeId, string Param, string Input)> References(this GraphDocument doc)
        => doc.Values.SelectMany(node => node.Value
            .Select(p => (Node: node.Key, Param: p.Key, Input: ReferencedName(p.Value)))
            .Where(r => r.Input is not null)
            .Select(r => (r.Node, r.Param, r.Input!)));

    /// <summary>
    /// The value of every declared input: <paramref name="given"/> where present, else the default.
    /// Errors: a given name the document does not declare, a value not valid for its kind.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Resolve(
        this GraphDocument doc, IReadOnlyDictionary<string, string>? given, List<GraphError> errors)
    {
        var resolved = new Dictionary<string, string>();
        foreach (var input in doc.Inputs)
        {
            var value = given is not null && given.TryGetValue(input.Name, out var g) ? g : input.Default;
            if (input.CheckValue(value) is { } problem)
                errors.Add(new(GraphErrorKind.InvalidGraphInput, input.Name,
                    $"Graph input '{input.Name}' ({input.Kind}) has value '{value}': {problem}"));
            resolved[input.Name] = value;
        }
        if (given is not null)
            foreach (var name in given.Keys.Where(n => !resolved.ContainsKey(n)).OrderBy(n => n, StringComparer.Ordinal))
                errors.Add(new(GraphErrorKind.UnknownGraphInput, name,
                    $"Graph input '{name}' is not declared by the document"));
        return resolved;
    }

    /// <summary>Resolves with the given values, throwing ArgumentException naming every problem.</summary>
    public static IReadOnlyDictionary<string, string> ResolveOrThrow(
        this GraphDocument doc, IReadOnlyDictionary<string, string>? given)
    {
        var errors = new List<GraphError>();
        var resolved = doc.Resolve(given, errors);
        return errors.Count == 0 ? resolved : throw new ArgumentException(string.Join("; ", errors.Select(e => e.Message)));
    }

    /// <summary>The same document with every reference replaced by its resolved value and every
    /// <c>$$</c> escape by a single dollar sign. Inputs are kept, so the result is still a valid
    /// document. A reference with no resolved value (validation reports it) is left as written.</summary>
    public static GraphDocument WithResolvedValues(this GraphDocument doc, IReadOnlyDictionary<string, string> resolved)
        => doc with
        {
            Values = doc.Values.ToDictionary(
                node => node.Key,
                node => (IReadOnlyDictionary<string, string>)node.Value.ToDictionary(
                    p => p.Key,
                    p => ResolveValue(p.Value, resolved))),
        };

    private static string ResolveValue(string value, IReadOnlyDictionary<string, string> resolved)
        => ReferencedName(value) is { } name
            ? resolved.GetValueOrDefault(name, value)
            : Unescape(value);
}
