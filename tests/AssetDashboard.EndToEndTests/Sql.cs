using Npgsql;

namespace AssetDashboard.EndToEndTests;

/// <summary>Hand-written SQL against the system's database, to check what the screen shows.</summary>
public sealed class Sql(string connectionString)
{
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    public const string OpenContracts = """FROM "Contracts" c JOIN "Customers" cu ON cu."Id" = c."CustomerId" WHERE c."Status" <> 'Closed'""";
    public const string ActiveAssets = """FROM "Assets" a JOIN "Contracts" c ON c."Id" = a."ContractId" WHERE a."Status" <> 'Sold'""";
}
