using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator(IWebHostEnvironment environment)
{
    private const string ClassConfirmed = "confirmed";
    private const string ClassLikelyExists = "likely_exists";
    private const string ClassProtected = "protected";
    private const string ClassMethodNotAllowed = "method_not_allowed";
    private const string ClassMaybeExists = "maybe_exists";
    private const string ClassNotFound = "not_found";
    private const string ClassSoft404 = "soft_404";
    private const string ClassRateLimited = "rate_limited";
    private const string ClassServerError = "server_error";
    private const string ClassUnknown = "unknown";

    public async Task<EndpointsDocument> RunAsync(
        Project project,
        DiscoveryRequest request,
        IReadOnlyList<EndpointModel> existing,
        CancellationToken cancellationToken)
    {
        var config = NormalizeDiscoveryRequest(request);
        using var client = CreateClient(config);
        var baseline = await BuildBaselineAsync(project.BaseUrl, client, config, cancellationToken);
        var registry = new EndpointRegistry(existing);

        if (project.IsWebPage)
        {
            var javascriptCandidates = await JavaScriptCandidatesAsync(project.BaseUrl, client, config, cancellationToken);
            await VerifyAndRegisterAsync(project, client, config, baseline, registry, javascriptCandidates, cancellationToken);
        }

        if (config.UseSpecDiscovery)
        {
            var specCandidates = await ProbeSpecRoutesAsync(project.BaseUrl, client, config, baseline, registry, cancellationToken);
            await VerifyAndRegisterAsync(project, client, config, baseline, registry, specCandidates, cancellationToken);
        }

        var apiListCandidates = ApiListCandidates(project.BaseUrl, config);
        await VerifyAndRegisterAsync(project, client, config, baseline, registry, apiListCandidates, cancellationToken);

        if (registry.Endpoints.Count == 0)
        {
            var fallback = NormalizeDiscoveryRequest(new DiscoveryRequest { ApiList = ["GET /health", "GET /openapi.json"] });
            var fallbackCandidates = ApiListCandidates(project.BaseUrl, fallback);
            await VerifyAndRegisterAsync(project, client, fallback, baseline, registry, fallbackCandidates, cancellationToken);
        }

        return new EndpointsDocument
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Endpoints = registry.Endpoints
        };
    }

    public static List<EndpointModel> FilterTestableEndpoints(IEnumerable<EndpointModel> endpoints)
    {
        return endpoints.Where(endpoint =>
        {
            var source = endpoint.Source.FirstOrDefault() ?? "";
            return endpoint.Classification switch
            {
                null or "" or ClassConfirmed or ClassLikelyExists or ClassProtected or ClassMethodNotAllowed
                    => string.IsNullOrEmpty(endpoint.Classification) || endpoint.Confidence >= 60 || source == "openapi",
                _ => false
            };
        }).ToList();
    }

    private static DiscoveryRequest NormalizeDiscoveryRequest(DiscoveryRequest request)
    {
        request.MaxWorkers = request.MaxWorkers <= 0 || request.MaxWorkers > 32 ? 6 : request.MaxWorkers;
        request.TimeoutSeconds = request.TimeoutSeconds <= 0 || request.TimeoutSeconds > 60 ? 10 : request.TimeoutSeconds;
        request.MaxBodyBytes = request.MaxBodyBytes <= 0 || request.MaxBodyBytes > 5 * 1024 * 1024 ? 1024 * 1024 : request.MaxBodyBytes;
        return request;
    }

    private static HttpClient CreateClient(DiscoveryRequest request)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = request.FollowRedirects };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BendIt discovery");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/yaml");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/yaml");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html;q=0.8");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*;q=0.5");
        return client;
    }

    private IEnumerable<string> ResourceLines(string name)
    {
        var path = Path.Combine(environment.ContentRootPath, "Resources", name);
        return File.Exists(path)
            ? File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            : [];
    }
}
