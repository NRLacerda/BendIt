using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class RateLimitTest : RobustnessTest
{
    private const int DefaultRateLimitBurstRequests = 5;
    private const int MaxRateLimitBurstRequests = 10;

    public string BendType => "rateLimit";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var burstCount = BurstCount(context.Request);
        var baseline = await RobustnessTestHelpers.ExecuteAsync(context.Client, context.Endpoint.Method, url, null, context.Project, BendType, cancellationToken);
        var burst = new List<ExecutedRequest>(burstCount);
        for (var i = 0; i < burstCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            burst.Add(await RobustnessTestHelpers.ExecuteAsync(context.Client, context.Endpoint.Method, url, null, context.Project, BendType, cancellationToken));
        }

        var postBurst = await RobustnessTestHelpers.ExecuteAsync(context.Client, context.Endpoint.Method, url, null, context.Project, BendType, cancellationToken);
        var observation = RateLimitObservation.From(baseline, burst, postBurst, burstCount);
        var risk = Risk(observation);
        var outcome = RobustnessTestHelpers.OutcomeFor(BendType, postBurst.StatusCode, risk);
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, postBurst.StatusCode, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"rateLimit burst observed HTTP {postBurst.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(observation),
            Request = new ResultRequest
            {
                HeadersMasked = RobustnessTestHelpers.MaskedHeaders(context.Project),
                Body = null,
                BodySizeBytes = 0
            },
            Result = new HttpResult
            {
                StatusCode = postBurst.StatusCode,
                StatusText = postBurst.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(postBurst.ResponseHeaders),
                ContentType = postBurst.ContentType,
                BodySizeBytes = postBurst.BodySizeBytes,
                DurationMs = postBurst.DurationMs
            },
            ResultBody = postBurst.Body,
            Evidence = Evidence(context.Endpoint, observation),
            Outcome = outcome,
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(risk, observation),
            OwaspCategory = RobustnessTestHelpers.OwaspCategoryFor(BendType),
            Recommendation = RobustnessTestHelpers.RecommendationFor(BendType, risk),
            Risk = risk,
            Severity = RobustnessTestHelpers.SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = false,
            TokenUsed = context.Project.Auth.Type != "none",
            AuthContext = context.Project.Auth,
            CreatedAt = now
        };
    }

    private static int BurstCount(TestRunRequest request)
    {
        var requested = request.MaxRequestsPerEndpoint > 0
            ? request.MaxRequestsPerEndpoint
            : DefaultRateLimitBurstRequests;
        return Math.Clamp(requested, 2, MaxRateLimitBurstRequests);
    }

    private static int Risk(RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed) return 6;
        if (observation.DegradedAfterBurst) return 6;
        if (!observation.SawTooManyRequests) return 5;
        if (!observation.SawThrottlingHeaders) return 5;
        return 2;
    }

    private static Dictionary<string, object> Mutation(RateLimitObservation observation)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "controlledRateLimitBurst",
            ["baselineStatus"] = observation.Baseline.StatusCode,
            ["burstRequests"] = observation.BurstRequests,
            ["burstStatuses"] = observation.BurstStatusCodes,
            ["postBurstStatus"] = observation.PostBurst.StatusCode,
            ["saw429"] = observation.SawTooManyRequests,
            ["sawThrottlingHeaders"] = observation.SawThrottlingHeaders,
            ["throttlingHeaders"] = observation.ThrottlingHeaderNames,
            ["degradedAfterBurst"] = observation.DegradedAfterBurst,
            ["degradationSignals"] = observation.DegradationSignals
        };
    }

    private static string Evidence(EndpointModel endpoint, RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed)
        {
            var errors = observation.AllRequests
                .Where(request => !string.IsNullOrWhiteSpace(request.Error))
                .Select(request => request.Error)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return $"{endpoint.Method} {endpoint.Path} rateLimit burst did not complete cleanly: {string.Join("; ", errors)}.";
        }

        var findings = new List<string>();
        if (!observation.SawTooManyRequests)
        {
            findings.Add("no HTTP 429 observed");
        }

        if (!observation.SawThrottlingHeaders)
        {
            findings.Add("no throttling headers observed");
        }
        else
        {
            findings.Add("throttling headers observed: " + string.Join(", ", observation.ThrottlingHeaderNames));
        }

        findings.Add(observation.DegradedAfterBurst
            ? "post-burst response differed from baseline: " + string.Join(", ", observation.DegradationSignals)
            : "post-burst response matched the baseline");

        return $"{endpoint.Method} {endpoint.Path} sent baseline, {observation.BurstRequests} burst request(s), and post-burst comparison; statuses: {string.Join(", ", observation.StatusCodes)}; {string.Join("; ", findings)}.";
    }

    private static string AnalysisSummary(int risk, RateLimitObservation observation)
    {
        if (observation.AnyRequestFailed)
        {
            return "rateLimit burst could not complete all requests and should be reviewed.";
        }

        return risk >= 5
            ? "rateLimit burst found missing or degraded throttling behavior and should be reviewed."
            : "rateLimit burst observed throttling controls and normal post-burst behavior.";
    }
}
