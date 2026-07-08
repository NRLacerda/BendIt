using BendIt.Api.Discovery;
using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.Discovery;

internal static class DiscoveryTests
{
    public static Task NativeApiListContainsApiRoutes()
    {
        var lines = File.ReadAllLines(Path.Combine(TestSupport.RepoRoot(), "backend", "Resources", "api-list.txt"));
        TestSupport.AssertContains(lines, "GET /api/users");
        TestSupport.AssertContains(lines, "GET /api/orders/{id}");
        TestSupport.AssertContains(lines, "POST /api/token");
        return Task.CompletedTask;
    }

    public static async Task DiscoveryAndBatteryProduceResultsForLocalApi()
    {
        await using var server = await LocalApiServer.StartAsync();
        var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(TestSupport.RepoRoot(), "backend")));
        var project = new Project { ProjectId = "local-api", BaseUrl = server.BaseUrl, Auth = new AuthConfig { Type = "none" } };
        var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
        {
            UseNativeApiList = true,
            UseSpecDiscovery = false,
            MaxWorkers = 4,
            TimeoutSeconds = 2
        }, [], CancellationToken.None);

        if (endpoints.Endpoints.Count == 0) throw new InvalidOperationException("expected native API-list discovery to register endpoints");
        var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
        if (testable.Count == 0) throw new InvalidOperationException("expected discovered local API endpoints to be testable");

        var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
        {
            BendTypes = ["authConsistency", "requestSize"],
            ParallelWorkers = 2
        }, CancellationToken.None);
        if (results.Results.Count == 0) throw new InvalidOperationException("expected test battery to produce results for discovered endpoints");
    }

    public static async Task LikelyExistsHealthEndpointProducesResultEvidence()
    {
        await using var server = await LocalApiServer.StartAsync();
        var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(TestSupport.RepoRoot(), "backend")));
        var project = new Project { ProjectId = "orgrow", BaseUrl = server.BaseUrl, Auth = new AuthConfig { Type = "none" } };
        var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
        {
            UseNativeApiList = false,
            UseSpecDiscovery = false,
            ApiList = ["GET /api/orgrow/grow/isalive"],
            MaxWorkers = 2,
            TimeoutSeconds = 2
        }, [], CancellationToken.None);

        var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
            ?? throw new InvalidOperationException("expected isalive endpoint to be discovered");
        if (endpoint.Classification != "likely_exists") throw new InvalidOperationException($"expected text/plain true endpoint to be likely_exists, got {endpoint.Classification}");

        var testable = DiscoveryOrchestrator.FilterTestableEndpoints(endpoints.Endpoints);
        if (testable.All(item => item.Id != endpoint.Id)) throw new InvalidOperationException("expected likely_exists isalive endpoint to be testable");

        var results = await new TestBatteryRunner().RunAsync(project, testable, new TestRunRequest
        {
            BendTypes = ["authConsistency", "idMutation", "httpMethodValidation"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        if (results.Results.Count == 0) throw new InvalidOperationException("expected likely_exists isalive endpoint to produce raw evidence");
        if (results.Results.All(result => result.ResultBody != "true")) throw new InvalidOperationException("expected resultBody to contain the actual API response body");
        if (results.Results.Any(result => result.Interesting)) throw new InvalidOperationException("expected public isalive endpoint evidence to be healthy, not a finding");
    }

    public static async Task LocalhostHttpsDiscoveryFallsBackToHttp()
    {
        await using var server = await LocalApiServer.StartAsync();
        var discovery = new DiscoveryOrchestrator(new TestEnvironment(Path.Combine(TestSupport.RepoRoot(), "backend")));
        var httpsBaseUrl = server.BaseUrl.Replace("http://127.0.0.1:", "https://localhost:", StringComparison.Ordinal);
        var project = new Project { ProjectId = "localhost-https-fallback", BaseUrl = httpsBaseUrl, Auth = new AuthConfig { Type = "none" } };
        var endpoints = await discovery.RunAsync(project, new DiscoveryRequest
        {
            UseNativeApiList = false,
            UseSpecDiscovery = false,
            ApiList = ["GET /api/orgrow/grow/isalive"],
            MaxWorkers = 1,
            TimeoutSeconds = 2
        }, [], CancellationToken.None);

        var endpoint = endpoints.Endpoints.SingleOrDefault(item => item.Path == "/api/orgrow/grow/isalive")
            ?? throw new InvalidOperationException("expected isalive endpoint to be discovered through http fallback");
        if (endpoint.StatusCode != 200) throw new InvalidOperationException($"expected localhost fallback endpoint status 200, got {endpoint.StatusCode}");
        if (endpoint.Scheme != "http") throw new InvalidOperationException($"expected fallback endpoint scheme http, got {endpoint.Scheme}");
        if (endpoint.Port is null) throw new InvalidOperationException("expected fallback endpoint to preserve local port");
        if (endpoint.Classification != "likely_exists" || endpoint.Confidence < 60)
        {
            throw new InvalidOperationException($"expected likely_exists with confidence >= 60, got {endpoint.Classification} {endpoint.Confidence}");
        }
    }
}
