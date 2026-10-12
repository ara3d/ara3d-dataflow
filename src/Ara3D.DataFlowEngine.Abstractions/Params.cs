using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ara3D.DataFlowEngine.Abstractions;

public enum ParamKind
{
    Boolean,
    Integer,
    Number,
    Text,
    Enum,
    FilePath,
    ModelRef,
    Expression,
    Json,
    DateTime,
    Fraction,
    Percent,
}

public enum SuggestKind
{
    /// <summary>Column names of the table connected to input port <c>Source</c>.</summary>
    ColumnsOfInput,
    /// <summary>Table names inside the database file named by param <c>Source</c>.</summary>
    TablesInFile,
    /// <summary>Files under the host's model roots whose extension is in <c>Source</c>,
    /// a comma-separated list such as <c>.ifc,.bos</c>.</summary>
    FilesUnderRoots,
}

/// <summary>
/// What a suggested column is for, so a host can rank and trim the list instead of every editor
/// guessing from kind names. Only <see cref="SuggestKind.ColumnsOfInput"/> reads it.
/// </summary>
public enum ColumnUse
{
    /// <summary>Every column, in table order.</summary>
    Any,
    /// <summary>A quantity to plot or sum: only numeric columns.</summary>
    Value,
    /// <summary>A caption or grouping: columns of role Label or Category first.</summary>
    Label,
    /// <summary>An identity to select or join on: columns of role Key first.</summary>
    Key,
    /// <summary>A join key: columns also present in the input named by <c>SharedWith</c> first, Key role among them first.</summary>
    JoinKey,
    /// <summary>A column to aggregate: each suggestion carries the aggregate functions its type allows,
    /// and a leading "*" entry stands for the whole row (count only).</summary>
    Aggregate,
}

/// <summary>
/// Where a parameter's value suggestions come from. Advisory only: the value
/// stays a free canonical string, and validation remains an eval-time concern.
/// <c>Use</c> says what a suggested column is for; <c>SharedWith</c> names the other input
/// port a <see cref="ColumnUse.JoinKey"/> column is compared with.
/// </summary>
public sealed record SuggestSource(SuggestKind Kind, string Source, ColumnUse Use = ColumnUse.Any, string? SharedWith = null)
{
    public static SuggestSource ColumnsOf(string inputPort, ColumnUse use = ColumnUse.Any, string? sharedWith = null)
        => new(SuggestKind.ColumnsOfInput, inputPort, use, sharedWith);

    public static SuggestSource TablesInFile(string pathParam)
        => new(SuggestKind.TablesInFile, pathParam);

    public static SuggestSource FilesWith(params string[] extensions)
        => new(SuggestKind.FilesUnderRoots, string.Join(',', extensions));
}

/// <summary>Optional editor presentation. Evaluation still validates canonical values.</summary>
public sealed record ParamControl(string Kind, double? Min = null, double? Max = null, double? Step = null,
    string? Unit = null, string? Label = null);

/// <summary>
/// One node parameter. Bindable, when set, makes the parameter a socket as well: an edge into
/// the input port named like the parameter (so the name must not repeat an input port's name)
/// supplies the value, whose kind the socket type must accept. A wired value replaces the
/// parameter's string for that evaluation; unwired, the string is used. List marks a Text
/// parameter holding a comma-separated list, for editors that offer a multi-value picker.
/// </summary>
public sealed record ParamSpec(
    string Name,
    ParamKind Kind,
    string Default = "",
    IReadOnlyList<string>? EnumValues = null,
    SuggestSource? Suggest = null,
    ParamControl? Control = null,
    PortType? Bindable = null,
    bool List = false);

/// <summary>
/// A node's parameter values as delivered by the engine: canonical string form
/// (the graph document's values layer), with typed accessors.
/// </summary>
public sealed class ParamValues
{
    private static readonly IReadOnlyDictionary<string, FlowValue> NoBound = new Dictionary<string, FlowValue>();

    public static readonly ParamValues Empty = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _values;
    private readonly IReadOnlyDictionary<string, FlowValue> _bound;

    /// <summary>
    /// <paramref name="bound"/> holds the values wired into bindable parameters. A wired Boolean,
    /// Integer, Number or Text value replaces the parameter's string with its canonical text; any
    /// other kind hides the string, so the typed accessors fall back to their defaults and the
    /// node reads the value through <see cref="GetBound"/>.
    /// </summary>
    public ParamValues(IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, FlowValue>? bound = null)
    {
        _bound = bound ?? NoBound;
        _values = _bound.Count == 0 ? values : Overlay(values, _bound);
    }

    private static Dictionary<string, string> Overlay(
        IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, FlowValue> bound)
    {
        var merged = new Dictionary<string, string>(values);
        foreach (var (name, value) in bound)
        {
            var text = CanonicalText(value);
            if (text is null) merged.Remove(name);
            else merged[name] = text;
        }
        return merged;
    }

    private static string? CanonicalText(FlowValue value)
        => value switch
        {
            BooleanValue b => b.Value ? "true" : "false",
            IntegerValue i => i.Value.ToString(CultureInfo.InvariantCulture),
            NumberValue n => n.Value.ToString("R", CultureInfo.InvariantCulture),
            TextValue t => t.Value,
            _ => null,
        };

    /// <summary>The value wired into bindable parameter <paramref name="name"/>, or null when none is.</summary>
    public FlowValue? GetBound(string name)
        => _bound.GetValueOrDefault(name);

    public IEnumerable<string> Names
        => _values.Keys;

    public string GetText(string name, string @default = "")
        => _values.TryGetValue(name, out var v) ? v : @default;

    public bool GetBoolean(string name, bool @default = false)
        => _values.TryGetValue(name, out var v) ? bool.Parse(v) : @default;

    public long GetInteger(string name, long @default = 0)
        => _values.TryGetValue(name, out var v) ? long.Parse(v, CultureInfo.InvariantCulture) : @default;

    public double GetNumber(string name, double @default = 0)
        => _values.TryGetValue(name, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : @default;

    public Fraction GetFraction(string name, double @default = 0) => new(GetNumber(name, @default));
    public Percent GetPercent(string name, double @default = 0) => new(GetNumber(name, @default));

    public void Validate(IReadOnlyList<ParamSpec> specs)
    {
        foreach (var spec in specs)
        {
            if (spec.Kind is not (ParamKind.Fraction or ParamKind.Percent)) continue;
            var value = GetNumber(spec.Name, string.IsNullOrEmpty(spec.Default) ? 0 : double.Parse(spec.Default, CultureInfo.InvariantCulture));
            if (spec.Kind == ParamKind.Fraction) _ = new Fraction(value);
            else _ = new Percent(value);
        }
    }
}
