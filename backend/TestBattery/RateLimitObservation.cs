namespace BendIt.Api.TestBattery;

internal sealed class RateLimitObservation
{
    public required ExecutedRequest Baseline { get; init; }
    public required List<ExecutedRequest> Burst { get; init; }
    public required ExecutedRequest PostBurst { get; init; }
    public required int BurstRequests { get; init; }
    public required List<string> DegradationSignals { get; init; }
    public required List<string> ThrottlingHeaderNames { get; init; }
    public List<ExecutedRequest> AllRequests => [Baseline, .. Burst, PostBurst];
    public List<int> BurstStatusCodes => Burst.Select(request => request.StatusCode).ToList();
    public List<int> StatusCodes => AllRequests.Select(request => request.StatusCode).ToList();
    public bool SawTooManyRequests => AllRequests.Any(request => request.StatusCode == 429);
    public bool SawThrottlingHeaders => ThrottlingHeaderNames.Count > 0;
    public bool DegradedAfterBurst => DegradationSignals.Count > 0;
    public bool AnyRequestFailed => AllRequests.Any(request => !string.IsNullOrEmpty(request.Error));

    public static RateLimitObservation From(ExecutedRequest baseline, List<ExecutedRequest> burst, ExecutedRequest postBurst, int burstRequests)
    {
        var throttlingHeaders = new List<string>();
        foreach (var request in new[] { baseline }.Concat(burst).Append(postBurst))
        {
            foreach (var header in BuildThrottlingHeaderNames(request.ResponseHeaders))
            {
                if (!throttlingHeaders.Contains(header, StringComparer.OrdinalIgnoreCase))
                {
                    throttlingHeaders.Add(header);
                }
            }
        }

        throttlingHeaders.Sort(StringComparer.OrdinalIgnoreCase);

        return new RateLimitObservation
        {
            Baseline = baseline,
            Burst = burst,
            PostBurst = postBurst,
            BurstRequests = burstRequests,
            DegradationSignals = BuildDegradationSignals(baseline, postBurst),
            ThrottlingHeaderNames = throttlingHeaders
        };
    }

    private static List<string> BuildDegradationSignals(ExecutedRequest baseline, ExecutedRequest postBurst)
    {
        var signals = new List<string>();
        if (baseline.StatusCode != postBurst.StatusCode)
        {
            signals.Add("status changed");
        }

        if (!string.Equals(baseline.ContentType, postBurst.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            signals.Add("content type changed");
        }

        if (BodySizeClass(baseline.BodySizeBytes) != BodySizeClass(postBurst.BodySizeBytes))
        {
            signals.Add("body size changed");
        }

        if (!string.Equals(RobustnessTestHelpers.ShortHash(baseline.Body), RobustnessTestHelpers.ShortHash(postBurst.Body), StringComparison.Ordinal))
        {
            signals.Add("body hash changed");
        }

        if (HasThrottlingHeader(baseline.ResponseHeaders) != HasThrottlingHeader(postBurst.ResponseHeaders))
        {
            signals.Add("throttling header state changed");
        }

        return signals;
    }

    private static int BodySizeClass(int bytes)
    {
        if (bytes == 0) return 0;
        if (bytes < 1024) return 1;
        if (bytes < 10 * 1024) return 2;
        if (bytes < 100 * 1024) return 3;
        return 4;
    }

    private static bool HasThrottlingHeader(Dictionary<string, string> headers)
    {
        return BuildThrottlingHeaderNames(headers).Count > 0;
    }

    private static List<string> BuildThrottlingHeaderNames(Dictionary<string, string> headers)
    {
        return headers.Keys
            .Where(IsThrottlingHeader)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsThrottlingHeader(string name)
    {
        return name.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Limit", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Remaining", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RateLimit-Reset", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("X-RateLimit-", StringComparison.OrdinalIgnoreCase);
    }
}
