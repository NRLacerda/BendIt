namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private sealed record EndpointCandidate(string Method, string Url, List<string> Source, string SourceDetail, int Confidence, bool Verified);

    private sealed record DiscoveryHttpResult(string Method, string Url)
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = "";
        public byte[] Body { get; set; } = [];
        public long ResponseLength { get; set; }
        public string ResponseHash { get; set; } = "";
        public string RedirectedTo { get; set; } = "";
        public Exception? Error { get; set; }
    }

    private sealed record BaselineFingerprint(IReadOnlyList<DiscoveryHttpResult> Samples);

    private sealed record VerifiedCandidate(EndpointCandidate Candidate, DiscoveryHttpResult HttpResult, string Classification, int Confidence);

    private sealed record NormalizedEndpoint(string Id, string Method, string Scheme, string Host, int? Port, string Path, List<string> QueryParams);
}
