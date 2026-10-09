using System.Text.RegularExpressions;
using AssetDashboard.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace AssetDashboard.Infrastructure.Seeding;

/// <summary>
/// Writes entities with PostgreSQL binary COPY, driven by the EF Core model (table, column names,
/// store types and value converters), so it stays in step with the mappings. Keys must be set by the caller.
/// </summary>
public static partial class BulkCopy
{
    public static async Task WriteAsync<T>(AssetDbContext db, NpgsqlConnection connection, IReadOnlyCollection<T> rows, CancellationToken ct = default)
        where T : class
    {
        if (rows.Count == 0) return;
        var entity = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");
        var tableName = entity.GetTableName()!;
        var table = StoreObjectIdentifier.Table(tableName, entity.GetSchema());

        var columns = entity.GetProperties()
            .Select(p => new
            {
                Property = p,
                Name = p.GetColumnName(table)!,
                // "character varying(32)" → "character varying"; Npgsql wants the bare type name.
                Type = Modifier().Replace(p.GetColumnType(table), ""),
                Converter = p.GetValueConverter() ?? p.GetTypeMapping().Converter,
                Getter = p.GetGetter(),
            })
            .ToList();

        var columnList = string.Join(", ", columns.Select(c => $"\"{c.Name}\""));
        await using var writer = await connection.BeginBinaryImportAsync($"COPY \"{tableName}\" ({columnList}) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var row in rows)
        {
            await writer.StartRowAsync(ct);
            foreach (var c in columns)
            {
                var value = c.Getter.GetClrValue(row);
                if (value is not null && c.Converter is not null) value = c.Converter.ConvertToProvider(value);
                if (value is null) await writer.WriteNullAsync(ct);
                else await writer.WriteAsync(value, c.Type, ct);
            }
        }
        await writer.CompleteAsync(ct);
    }

    /// <summary>Moves an identity column's sequence past the highest key written by COPY.</summary>
    public static Task ResetIdentityAsync<T>(AssetDbContext db, CancellationToken ct = default) where T : class
    {
        // The table name comes from the EF model, never from user input.
        var table = db.Model.FindEntityType(typeof(T))!.GetTableName()!;
        var sql = $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"{table}\"), 0) + 1, false)";
        return db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    [GeneratedRegex(@"\(.*\)")]
    private static partial Regex Modifier();
}
