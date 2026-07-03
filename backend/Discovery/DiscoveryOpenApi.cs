using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BendIt.Api.Models;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private async Task<List<EndpointCandidate>> ProbeSpecRoutesAsync(
        string baseUrl,
        HttpClient client,
        DiscoveryRequest request,
        BaselineFingerprint baseline,
        EndpointRegistry registry,
        CancellationToken cancellationToken)
    {
        var candidates = new List<EndpointCandidate>();
        foreach (var line in ResourceLines("spec-list.txt"))
        {
            var (method, route) = ParseApiListLine(line);
            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(route) || !ResolveCandidateUrl(baseUrl, route, out var fullUrl))
            {
                continue;
            }

            var result = await ExecuteDiscoveryRequestAsync(client, HttpMethod.Get.Method, fullUrl, request.MaxBodyBytes, cancellationToken);
            var (classification, confidence) = ClassifyDiscoveryResult(result, "openApiSpecProbe", baseline, false);
            if (classification is not ClassNotFound and not ClassSoft404 and not ClassUnknown)
            {
                registry.Register(new EndpointCandidate(HttpMethod.Get.Method, fullUrl, ["openApiSpecProbe"], route, confidence, true),
                    ProjectlessAuthRequired(classification), result, classification, confidence);
            }

            if (result.Error is null && LooksLikeOpenApiSpec(result.Body))
            {
                candidates.AddRange(ExtractOpenApiEndpoints(baseUrl, fullUrl, result.Body));
            }
        }

        return candidates;
    }

    private static bool LooksLikeOpenApiSpec(byte[] body)
    {
        if (body.Length == 0)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(body).ToLowerInvariant();
        return text.Contains("openapi") || text.Contains("swagger") || text.Contains("paths:") || text.Contains("\"paths\"");
    }

    private static IEnumerable<EndpointCandidate> ExtractOpenApiEndpoints(string baseUrl, string specUrl, byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var serverBase = baseUrl;
            if (doc.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array &&
                servers.GetArrayLength() > 0 && servers[0].TryGetProperty("url", out var serverUrl))
            {
                if (ResolveSpecServer(baseUrl, specUrl, serverUrl.GetString() ?? "", out var resolved))
                {
                    serverBase = resolved;
                }
            }

            var candidates = new List<EndpointCandidate>();
            foreach (var pathProperty in paths.EnumerateObject())
            {
                if (pathProperty.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var operation in pathProperty.Value.EnumerateObject())
                {
                    if (!IsHttpMethod(operation.Name))
                    {
                        continue;
                    }

                    if (ResolveCandidateUrl(serverBase, InstantiateOpenApiPath(pathProperty.Name), out var fullUrl))
                    {
                        candidates.Add(new EndpointCandidate(operation.Name.ToUpperInvariant(), fullUrl, ["openapi"], specUrl, 100, false));
                    }
                }
            }

            return candidates;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string InstantiateOpenApiPath(string path)
    {
        return OpenApiPathParam().Replace(path, "123");
    }

    private static bool ResolveSpecServer(string baseUrl, string specUrl, string serverUrl, out string resolved)
    {
        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var absolute))
        {
            resolved = absolute.ToString();
            return true;
        }

        var anchor = serverUrl.StartsWith('.') ? specUrl : baseUrl;
        return ResolveCandidateUrl(anchor, serverUrl, out resolved);
    }

    [GeneratedRegex(@"\{[^}/]+\}")]
    private static partial Regex OpenApiPathParam();
}
