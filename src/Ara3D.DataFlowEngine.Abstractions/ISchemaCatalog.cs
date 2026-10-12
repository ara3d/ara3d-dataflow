using System.Collections.Generic;
using System.Linq;

namespace Ara3D.DataFlowEngine.Abstractions;

/// <summary>The only design-time I/O a node may use to learn a shape: what columns a source
/// has. Implementations may read a CSV header, query information_schema, or answer from a
/// dictionary in tests.</summary>
public interface ISchemaCatalog
{
    SchemaResult CsvSchema(string source, string path);
    SchemaResult TableSchema(string source, string table);

    /// <summary>The result schema of user SQL whose inputs t1..tN have the given schemas.</summary>
    SchemaResult QuerySchema(string sql, IReadOnlyList<Schema> inputs);

    /// <summary>The result schema of SQL that reads files itself (DuckDB's read_csv, read_parquet
    /// and the like). Files lists the paths the SQL reads, so an implementation can cache the
    /// answer until one of them changes; a glob or an empty list is not cached.</summary>
    SchemaResult FileQuerySchema(string sql, IReadOnlyList<string> files);

    /// <summary>The columns a format-specific reader yields for one file. Format names the reader
    /// and Options its selectors, as raw text: "duckdb" (table), "xlsx" (sheet, headerRow, range),
    /// "sqlite" (table, or sql for a query). A format the implementation does not read is a
    /// failed result, never an exception.</summary>
    SchemaResult FileSchema(string format, string path, IReadOnlyDictionary<string, string> options);
}

/// <summary>A catalog answered from fixed tables, for tests and for graphs whose
/// sources are declared rather than probed. Source keys are "source/reference";
/// raw SQL is keyed by its exact text.</summary>
public sealed class StaticCatalog(IReadOnlyDictionary<string, Schema> entries, IReadOnlyDictionary<string, Schema>? queries = null) : ISchemaCatalog
{
    public static string Key(string source, string reference) => $"{source}/{reference}";

    public static string FileKey(string format, string path, IReadOnlyDictionary<string, string> options)
        => Key(format, path + string.Concat(options.OrderBy(o => o.Key, System.StringComparer.Ordinal).Select(o => $"#{o.Key}={o.Value}")));

    public SchemaResult CsvSchema(string source, string path) => Lookup(source, path);
    public SchemaResult TableSchema(string source, string table) => Lookup(source, table);

    public SchemaResult QuerySchema(string sql, IReadOnlyList<Schema> inputs)
        => queries is not null && queries.TryGetValue(sql, out var schema)
            ? SchemaResult.Of(schema)
            : SchemaResult.Fail("Raw SQL cannot be typed without a database catalog.");

    public SchemaResult FileQuerySchema(string sql, IReadOnlyList<string> files) => QuerySchema(sql, []);

    /// <summary>Answers from entries keyed by <see cref="FileKey"/>.</summary>
    public SchemaResult FileSchema(string format, string path, IReadOnlyDictionary<string, string> options)
        => entries.TryGetValue(FileKey(format, path, options), out var schema)
            ? SchemaResult.Of(schema)
            : SchemaResult.Fail($"Unknown file source '{FileKey(format, path, options)}'.");

    private SchemaResult Lookup(string source, string reference)
        => entries.TryGetValue(Key(source, reference), out var schema)
            ? SchemaResult.Of(schema)
            : SchemaResult.Fail($"Unknown source '{source}/{reference}'.");
}
