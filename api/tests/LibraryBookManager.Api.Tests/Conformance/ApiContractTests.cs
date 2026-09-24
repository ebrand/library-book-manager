using System.Text.Json;
using LibraryBookManager.Api.Data;
using LibraryBookManager.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace LibraryBookManager.Api.Tests.Conformance;

/// <summary>
/// plt-api-001 — the repository's own share of REQ-API-001, -003 and -004. The standard's
/// criteria describe the platform conformance check, which is not available here; these
/// tests assert the repository state that check looks for (see TRACE.md).
/// </summary>
public sealed class ApiContractTests : IDisposable
{
    private readonly ApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    public static string ContractPath =>
        Path.Combine(RepoPaths.ApiRoot, "src", "LibraryBookManager.Api", "Contract", "openapi.v1.yaml");

    private static async Task<(OpenApiDocument Document, OpenApiDiagnostic Diagnostic)> LoadContract()
    {
        var settings = new OpenApiReaderSettings();
        settings.TryAddReader(OpenApiConstants.Yaml, new OpenApiYamlReader());
        await using var stream = File.OpenRead(ContractPath);
        var result = await OpenApiDocument.LoadAsync(stream, OpenApiConstants.Yaml, settings);
        return (result.Document!, result.Diagnostic!);
    }

    private IReadOnlyCollection<(string Method, string Path)> MappedRoutes() =>
        _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m.ToUpperInvariant(), "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .ToHashSet();

    [Fact(DisplayName = "REQ-API-001: the repository publishes a valid OpenAPI 3.1 document")]
    public async Task Contract_is_valid_openapi_3_1()
    {
        var (document, diagnostic) = await LoadContract();

        Assert.Empty(diagnostic.Errors);
        Assert.Equal(OpenApiSpecVersion.OpenApi3_1, diagnostic.SpecificationVersion);
        var validationErrors = document.Validate(ValidationRuleSet.GetDefaultRuleSet());
        Assert.Empty(validationErrors ?? []);
    }

    [Fact(DisplayName = "REQ-API-001: every public route the API serves is in the OpenAPI document, and nothing else is")]
    public async Task Every_route_is_documented()
    {
        var (document, _) = await LoadContract();
        var documented = document.Paths
            .SelectMany(p => p.Value.Operations!.Keys.Select(m => (m.Method.ToUpperInvariant(), p.Key)))
            .ToHashSet();

        var mapped = MappedRoutes();

        Assert.NotEmpty(mapped);
        Assert.Empty(mapped.Except(documented).Select(r => $"undocumented: {r.Item1} {r.Item2}"));
        Assert.Empty(documented.Except(mapped).Select(r => $"documented but not served: {r.Item1} {r.Item2}"));
    }

    [Fact(DisplayName = "REQ-API-003: the contract carries a major version, in its info block and in every route")]
    public async Task Contract_is_major_versioned()
    {
        var (document, _) = await LoadContract();

        Assert.StartsWith("1.", document.Info.Version);
        Assert.All(document.Paths.Keys, path => Assert.StartsWith("/v1/", path));
        Assert.All(MappedRoutes(), route => Assert.StartsWith("/v1/", route.Path));
    }

    [Fact(DisplayName = "REQ-API-004: responses publish the API's own identifiers (ISBN, barcode) and no internal key")]
    public async Task No_internal_identifiers_in_responses()
    {
        var librarian = _api.User(Role.Librarian);
        _api.Title("9780261102217", copies: 2);

        var detail = await _api.ClientAs(librarian).GetStringAsync("/v1/titles/9780261102217");
        var search = await _api.ClientAs(librarian).GetStringAsync("/v1/titles?isbn=9780261102217");

        foreach (var json in new[] { detail, search })
            Assert.DoesNotContain(PropertyNames(JsonDocument.Parse(json).RootElement),
                name => name.Equals("id", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith("Id", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "REQ-API-004: the contract is defined in the API's own contract module and names no protobuf types")]
    public void Contract_is_owned_here()
    {
        var text = File.ReadAllText(ContractPath);
        Assert.DoesNotContain("google.protobuf", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".proto", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(RepoPaths.ApiRoot, "*.proto", SearchOption.AllDirectories));
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
