using System;
using System.Collections.Generic;

namespace Ara3D.DataFlowEngine.Abstractions;

public readonly record struct SchemaError(string Message)
{
    public override string ToString() => Message;
}

/// <summary>Either a schema or the errors that prevented one. Errors are values, never
/// exceptions, so the editor can show them on a node before anything runs. A schema may also carry
/// warnings: problems the node's run would warn about and continue past (a named column that is
/// absent and is skipped). A result with warnings is still Ok and its schema is still the one the
/// run produces.</summary>
public sealed record SchemaResult(Schema? Schema, IReadOnlyList<SchemaError> Errors)
{
    public bool Ok => Schema is not null && Errors.Count == 0;

    /// <summary>What the run would warn about; empty when it would not. Always empty on a failed result.</summary>
    public IReadOnlyList<SchemaError> Warnings { get; init; } = [];

    public static SchemaResult Of(Schema schema) => new(schema, []);
    public static SchemaResult Of(params Column[] columns) => Of(new Schema(columns));
    public static SchemaResult Of(Schema schema, IReadOnlyList<SchemaError> warnings) => new(schema, []) { Warnings = warnings };
    public static SchemaResult Fail(string message) => new(null, [new SchemaError(message)]);
    public static SchemaResult Fail(IReadOnlyList<SchemaError> errors) => new(null, errors);

    /// <summary>The same result with more warnings appended; a failed result is returned unchanged.</summary>
    public SchemaResult WithWarnings(IEnumerable<SchemaError> more)
        => Ok ? this with { Warnings = [.. Warnings, .. more] } : this;

    public Schema Require()
        => Ok ? Schema! : throw new InvalidOperationException(string.Join("; ", Errors));

    public override string ToString()
        => Ok ? Schema!.ToString() : string.Join("; ", Errors);
}
