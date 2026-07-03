using BendIt.Api.Models;

namespace BendIt.Api.Discovery;

public sealed partial class DiscoveryOrchestrator
{
    private static async Task VerifyAndRegisterAsync(
        Project project,
        HttpClient client,
        DiscoveryRequest request,
        BaselineFingerprint baseline,
        EndpointRegistry registry,
        IReadOnlyList<EndpointCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var authRequired = project.Auth.Type != "none";
        await Parallel.ForEachAsync(candidates, new ParallelOptions { MaxDegreeOfParallelism = request.MaxWorkers, CancellationToken = cancellationToken }, async (candidate, token) =>
        {
            var verified = await VerifyCandidateAsync(client, request, baseline, candidate, token);
            registry.Register(verified.Candidate, authRequired || ProjectlessAuthRequired(verified.Classification), verified.HttpResult, verified.Classification, verified.Confidence);
        });
    }

    private static async Task<VerifiedCandidate> VerifyCandidateAsync(
        HttpClient client,
        DiscoveryRequest request,
        BaselineFingerprint baseline,
        EndpointCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.Verified)
        {
            return new VerifiedCandidate(candidate, new DiscoveryHttpResult(candidate.Method, candidate.Url), ClassConfirmed, candidate.Confidence);
        }

        if (!IsSafeDiscoveryMethod(candidate.Method))
        {
            var source = candidate.Source.FirstOrDefault() ?? "";
            return source == "openapi"
                ? new VerifiedCandidate(candidate, new DiscoveryHttpResult(candidate.Method, candidate.Url), ClassConfirmed, Math.Max(candidate.Confidence, 100))
                : new VerifiedCandidate(candidate, new DiscoveryHttpResult(candidate.Method, candidate.Url), ClassUnknown, Math.Max(candidate.Confidence, 40));
        }

        var result = await ExecuteDiscoveryRequestAsync(client, candidate.Method, candidate.Url, request.MaxBodyBytes, cancellationToken);
        if (result.Error is not null && TryDowngradeLocalHttpsUrl(candidate.Url, out var httpUrl))
        {
            foreach (var fallbackUrl in LocalHttpFallbackUrls(httpUrl))
            {
                var retry = await ExecuteDiscoveryRequestAsync(client, candidate.Method, fallbackUrl, request.MaxBodyBytes, cancellationToken);
                if (retry.Error is null)
                {
                    candidate = candidate with { Url = fallbackUrl };
                    result = retry;
                    break;
                }
            }
        }

        var (classification, confidence) = ClassifyDiscoveryResult(result, candidate.Source.FirstOrDefault() ?? "", baseline, candidate.Source.FirstOrDefault() == "openapi");
        return new VerifiedCandidate(candidate, result, classification, Math.Max(candidate.Confidence, confidence));
    }

    private static bool IsSafeDiscoveryMethod(string value)
    {
        return value.Trim().ToUpperInvariant() is "GET" or "HEAD" or "OPTIONS";
    }
}
