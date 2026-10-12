using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssetDashboard.IntegrationTests;

/// <summary>
/// The API contract is the OpenAPI document committed at contracts/openapi.v1.json. The frontend's
/// TypeScript types are generated from that file, so an unintended API change fails here first.
/// To accept an intended change: run the tests with UPDATE_API_CONTRACT=1, then regenerate the
/// frontend types (cd web && npm run api:generate) and commit both.
/// </summary>
[Collection(ApiCollection.Name)]
public class ApiContractTests(ApiFixture api)
{
    [Fact]
    public async Task Openapi_document_matches_the_committed_contract()
    {
        var live = Normalize(await api.Client.GetStringAsync("/openapi/v1.json"));
        var path = Path.Combine(RepoRoot(), "contracts", "openapi.v1.json");

        if (Environment.GetEnvironmentVariable("UPDATE_API_CONTRACT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, live);
            return;
        }

        Assert.True(File.Exists(path), $"No committed contract at {path}. Run the tests with UPDATE_API_CONTRACT=1 to create it.");
        var committed = await File.ReadAllTextAsync(path);
        if (committed != live)
        {
            var received = path.Replace(".json", ".received.json");
            await File.WriteAllTextAsync(received, live);
            Assert.Fail($"The API contract changed. Compare {received} with {path}. If the change is intended, " +
                        "run the tests with UPDATE_API_CONTRACT=1, regenerate the frontend types (cd web && npm run api:generate) and commit both.");
        }
    }

    /// <summary>Drops parts that depend on where the API runs (servers) and fixes the formatting.</summary>
    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AssetDashboard.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AssetDashboard.slnx) not found.");
    }
}
