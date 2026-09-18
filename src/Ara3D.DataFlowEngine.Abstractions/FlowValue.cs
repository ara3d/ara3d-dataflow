using Ara3D.DataTable;

namespace Ara3D.DataFlowEngine.Abstractions;

public enum ValueKind
{
    Boolean,
    Integer,
    Number,
    Text,
    Table,

    /// <summary>A relation: a logical plan and its schema, not rows. Rows exist only once an
    /// executor materializes the plan.</summary>
    Relation,

    /// <summary>Not a wire value: the placeholder for an unconnected optional input.</summary>
    Missing,
}

/// <summary>
/// The immutable values that flow along graph edges. Exactly one subtype per ValueKind.
/// </summary>
public abstract record FlowValue
{
    public abstract ValueKind Kind { get; }
}

public sealed record BooleanValue(bool Value) : FlowValue
{
    public override ValueKind Kind => ValueKind.Boolean;
}

public sealed record IntegerValue(long Value) : FlowValue
{
    public override ValueKind Kind => ValueKind.Integer;
}

public sealed record NumberValue(double Value) : FlowValue
{
    public override ValueKind Kind => ValueKind.Number;
}

public sealed record TextValue(string Value) : FlowValue
{
    public override ValueKind Kind => ValueKind.Text;
}

public sealed record TableValue(IDataTable Table) : FlowValue
{
    public override ValueKind Kind => ValueKind.Table;
}

/// <summary>
/// A plan in canonical text plus its content hash. The engine hashes and records only the
/// text and hash; Payload is the in-process plan object for downstream nodes and is never
/// part of identity, so a value read back from a run record (Payload null) equals the original.
/// </summary>
public sealed record RelationValue(string Text, string Hash, object? Payload = null) : FlowValue
{
    public override ValueKind Kind => ValueKind.Relation;

    public bool Equals(RelationValue? other)
        => other is not null && Text == other.Text && Hash == other.Hash;

    public override int GetHashCode()
        => System.HashCode.Combine(Text, Hash);
}

/// <summary>
/// The placeholder a node receives for an unconnected optional input, keeping
/// the inputs list aligned with Spec.Inputs. Never flows along an edge, is never
/// hashed, and must never be returned as an output.
/// </summary>
public sealed record MissingValue : FlowValue
{
    public static readonly MissingValue Instance = new();

    private MissingValue()
    {
    }

    public override ValueKind Kind => ValueKind.Missing;
}
