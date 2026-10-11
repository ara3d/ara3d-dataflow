using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ara3D.DataFlowEngine.Abstractions;

namespace Ara3D.NodeGraph;

/// <summary>
/// A value the graph takes from outside: a name, a parameter kind (FilePath, Text, Number,
/// Integer or Boolean), a default in canonical string form, and a description for people.
/// A node parameter refers to it by writing <c>$name</c> as its whole value (format.md §3a).
/// </summary>
public sealed record GraphInput(string Name, ParamKind Kind, string Default, string Description = "")
{
    public static readonly IReadOnlyList<ParamKind> AllowedKinds =
        [ParamKind.FilePath, ParamKind.Text, ParamKind.Number, ParamKind.Integer, ParamKind.Boolean];

    /// <summary>Names follow the parameter-name rule: a letter, then letters and digits.</summary>
    public static bool IsValidName(string name)
        => name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(char.IsAsciiLetterOrDigit);

    /// <summary>Null when <paramref name="value"/> is a canonical string of this input's kind; else the reason.</summary>
    public string? CheckValue(string value)
        => Kind switch
        {
            ParamKind.Boolean when value is not ("true" or "false") => "expected 'true' or 'false'",
            ParamKind.Integer when !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                => "expected a decimal integer",
            ParamKind.Number when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                => "expected a number in invariant form",
            _ => null,
        };
}
