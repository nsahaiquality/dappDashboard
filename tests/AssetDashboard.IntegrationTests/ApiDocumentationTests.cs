using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AssetDashboard.IntegrationTests;

/// <summary>Keeps the interactive API documentation complete: an undocumented endpoint fails the build.</summary>
[Collection(ApiCollection.Name)]
public class ApiDocumentationTests(ApiFixture api)
{
    private async Task<JsonElement> OpenApiAsync() =>
        JsonDocument.Parse(await api.Client.GetStringAsync("/openapi/v1.json")).RootElement;

    [Fact]
    public async Task Every_api_route_is_in_the_openapi_document_with_tag_summary_and_description()
    {
        var doc = await OpenApiAsync();
        var paths = doc.GetProperty("paths");
        var routes = api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .Where(r => r.StartsWith("/api/"))
            // OpenAPI writes route constraints without the type: {id:long} → {id}.
            .Select(r => System.Text.RegularExpressions.Regex.Replace(r, @"\{(\w+):\w+\}", "{$1}").TrimEnd('/'))
            .Distinct()
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, route =>
        {
            Assert.True(paths.TryGetProperty(route, out var item) || paths.TryGetProperty(route + "/", out item), $"{route} missing from OpenAPI");
            foreach (var op in item.EnumerateObject())
            {
                Assert.True(op.Value.TryGetProperty("summary", out var s) && s.GetString()!.Length > 3, $"{op.Name} {route} has no summary");
                Assert.True(op.Value.TryGetProperty("description", out var d) && d.GetString()!.Length > 20, $"{op.Name} {route} has no description");
                Assert.True(op.Value.TryGetProperty("tags", out var t) && t.GetArrayLength() > 0, $"{op.Name} {route} has no tag");
            }
        });
    }

    [Fact]
    public async Task Document_has_title_and_described_parameters()
    {
        var doc = await OpenApiAsync();
        Assert.Equal("Asset Portfolio Dashboard API", doc.GetProperty("info").GetProperty("title").GetString());

        var parameters = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Where(op => op.Value.TryGetProperty("parameters", out _))
            .SelectMany(op => op.Value.GetProperty("parameters").EnumerateArray().Select(p => (op.Name, p)))
            .ToList();
        Assert.All(parameters, x => Assert.True(x.p.TryGetProperty("description", out _), $"parameter {x.p.GetProperty("name")} has no description"));
    }

    [Fact]
    public async Task Swagger_ui_is_served_and_the_root_redirects_to_it()
    {
        var ui = await api.Client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);

        using var noRedirect = api.Factory.CreateClient(new() { AllowAutoRedirect = false });
        var root = await noRedirect.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, root.StatusCode);
        Assert.Equal("/swagger", root.Headers.Location!.OriginalString);
    }
}
