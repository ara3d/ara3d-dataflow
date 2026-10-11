using System;
using System.Collections.Generic;
using System.Linq;

namespace Ara3D.DataFlowEngine.Abstractions;

/// <summary>The closed column type vocabulary. The first four are the expression
/// language's scalar types; the rest can be read and passed through but not computed on.</summary>
public enum ColumnType
{
    Boolean,
    Integer,
    Number,
    Text,
    Date,
    Timestamp,
    Binary,
    Unknown,
}

public readonly record struct Column(string Name, ColumnType Type, bool Nullable = true);

/// <summary>An ordered, typed column list. Lookup is case-insensitive, matching DuckDB
/// identifier resolution, but the stored spelling is what flows downstream.</summary>
public sealed class Schema(IReadOnlyList<Column> columns) : IEquatable<Schema>
{
    public static readonly Schema Empty = new([]);

    public IReadOnlyList<Column> Columns => columns;

    public Column? Find(string name)
    {
        for (var i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return columns[i];
        return null;
    }

    public bool Has(string name)
        => Find(name) is not null;

    public Schema Append(Column column)
        => new([.. columns, column]);

    public Schema Map(Func<Column, Column> f)
        => new(columns.Select(f).ToList());

    public bool Equals(Schema? other)
        => other is not null && columns.SequenceEqual(other.Columns);

    public override bool Equals(object? obj)
        => Equals(obj as Schema);

    public override int GetHashCode()
        => columns.Aggregate(17, (h, c) => unchecked(h * 31 + c.GetHashCode()));

    public override string ToString()
        => string.Join(", ", columns.Select(c => $"{c.Name}:{c.Type}{(c.Nullable ? "?" : "")}"));
}
