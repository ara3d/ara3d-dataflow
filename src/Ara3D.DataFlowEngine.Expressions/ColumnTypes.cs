using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.DataFlowEngine.Expressions;

/// <summary>Conversions between the closed column vocabulary and the expression language's
/// scalar types. They live here because the scalar types do, and Expressions depends on Abstractions.</summary>
public static class ColumnTypes
{
    public static ScalarType? ToScalarType(this ColumnType type)
        => type switch
        {
            ColumnType.Boolean => ScalarType.Boolean,
            ColumnType.Integer => ScalarType.Integer,
            ColumnType.Number => ScalarType.Number,
            ColumnType.Text => ScalarType.Text,
            _ => null,
        };

    public static ColumnType ToColumnType(this ScalarType? type)
        => type switch
        {
            ScalarType.Boolean => ColumnType.Boolean,
            ScalarType.Integer => ColumnType.Integer,
            ScalarType.Number => ColumnType.Number,
            ScalarType.Text => ColumnType.Text,
            _ => ColumnType.Unknown,
        };
}
