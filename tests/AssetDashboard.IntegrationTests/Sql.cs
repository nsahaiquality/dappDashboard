using Npgsql;

namespace AssetDashboard.IntegrationTests;

/// <summary>
/// Plain SQL against the test database, written independently of the application's LINQ queries.
/// Reconciliation tests compare its answers with what the API returns.
/// </summary>
public sealed class Sql(string connectionString)
{
    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T));
    }

    public async Task<List<Dictionary<string, object?>>> RowsAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// SQL predicate on a contract alias <c>c</c> matching the portfolio filter, written by hand
    /// (customer alias <c>cu</c> must be joined).
    /// </summary>
    public static string ContractFilter(string? assetClass = null, string? country = null, int? vendorId = null,
        string? productType = null, bool underwaterOnly = false)
    {
        var where = new List<string> { "TRUE" };
        if (assetClass is not null) where.Add($"c.\"AssetClass\" = '{assetClass}'");
        if (country is not null) where.Add($"cu.\"Country\" = '{country}'");
        if (vendorId is not null) where.Add($"c.\"VendorId\" = {vendorId}");
        if (productType is not null) where.Add($"c.\"ProductType\" = '{productType}'");
        if (underwaterOnly)
            where.Add("""
                c."OutstandingPrincipal" > COALESCE((SELECT SUM(a."MarketValue") FROM "Assets" a
                                                     WHERE a."ContractId" = c."Id" AND a."Status" <> 'Sold'), 0)
                """);
        return string.Join(" AND ", where);
    }
}
