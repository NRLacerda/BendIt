using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private sealed class EndpointRegistry
    {
        private readonly object _lock = new();
        private readonly HashSet<string> _registered;

        public EndpointRegistry(IEnumerable<EndpointModel> existing)
        {
            Endpoints = existing.ToList();
            _registered = Endpoints.Select(endpoint => endpoint.Id).ToHashSet(StringComparer.Ordinal);
        }

        public List<EndpointModel> Endpoints { get; }

        public void Register(EndpointCandidate candidate, bool authRequired, DiscoveryHttpResult result, string classification, int confidence)
        {
            if (!NormalizeEndpoint(candidate.Method, candidate.Url, out var normalized))
            {
                return;
            }

            lock (_lock)
            {
                if (!_registered.Add(normalized.Id))
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                Endpoints.Add(new EndpointModel
                {
                    Id = normalized.Id,
                    Method = normalized.Method,
                    Scheme = normalized.Scheme,
                    Host = normalized.Host,
                    Port = normalized.Port,
                    Path = normalized.Path,
                    QueryParams = normalized.QueryParams,
                    Source = candidate.Source,
                    SourceDetail = candidate.SourceDetail,
                    AuthRequired = authRequired,
                    Status = "processed",
                    StatusCode = result.StatusCode,
                    ContentType = result.ContentType,
                    ResponseLength = result.ResponseLength,
                    ResponseHash = result.ResponseHash,
                    Confidence = confidence,
                    Classification = classification,
                    Protected = classification == ClassProtected,
                    RedirectedTo = result.RedirectedTo,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    VerifiedAt = now
                });
            }
        }
    }
}
