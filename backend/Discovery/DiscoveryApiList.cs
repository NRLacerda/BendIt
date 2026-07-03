using BendIt.Api.Models;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private List<EndpointCandidate> ApiListCandidates(string baseUrl, DiscoveryRequest request)
    {
        var lines = new List<string>();
        if (request.UseNativeApiList)
        {
            lines.AddRange(ResourceLines("api-list.txt"));
        }

        lines.AddRange(request.ApiList);
        if (lines.Count == 0 && !request.UseSpecDiscovery)
        {
            lines.AddRange(["GET /health", "GET /openapi.json"]);
        }

        var nativeLines = ResourceLines("api-list.txt").Select(line => line.Trim()).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<EndpointCandidate>();
        foreach (var line in lines)
        {
            var (method, route) = ParseApiListLine(line);
            if (string.IsNullOrEmpty(route) || !ResolveCandidateUrl(baseUrl, route, out var fullUrl))
            {
                continue;
            }

            var source = nativeLines.Contains(line.Trim()) ? "nativeApiList" : "customApiList";
            candidates.Add(new EndpointCandidate(method, fullUrl, [source], route, 40, false));
        }

        return candidates;
    }

    private static (string Method, string Route) ParseApiListLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
        {
            return ("", "");
        }

        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && IsHttpMethod(fields[0])
            ? (fields[0].ToUpperInvariant(), fields[1])
            : (HttpMethod.Get.Method, fields[0]);
    }
}
