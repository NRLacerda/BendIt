using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class DependencyResilienceTest : RobustnessTest
{
    private const string BendTypeValue = "dependencyResilience";

    public string BendType => BendTypeValue;

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.DownDetectionThreshold(request) > 0;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var requestCount = RobustnessTestHelpers.DownDetectionThreshold(context.Request);
        var observations = new List<ExecutedRequest>(requestCount);
        for (var i = 0; i < requestCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                context.Endpoint.Method,
                url,
                null,
                context.Project,
                BendType,
                cancellationToken));
        }

        var analysis = Analyze(observations);
        var representative = analysis.Representative;
        var risk = analysis.Risk;
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, representative.StatusCode, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"dependencyResilience observed HTTP {representative.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(analysis, requestCount),
            Request = new ResultRequest
            {
                HeadersMasked = RobustnessTestHelpers.MaskedHeaders(context.Project),
                Body = null,
                BodySizeBytes = 0
            },
            Result = new HttpResult
            {
                StatusCode = representative.StatusCode,
                StatusText = representative.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(representative.ResponseHeaders),
                ContentType = representative.ContentType,
                BodySizeBytes = representative.BodySizeBytes,
                DurationMs = representative.DurationMs
            },
            ResultBody = representative.Body,
            Evidence = Evidence(context.Endpoint, analysis),
            Outcome = RobustnessTestHelpers.OutcomeFor(BendType, representative.StatusCode, risk),
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(analysis),
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

    private static DependencyAnalysis Analyze(List<ExecutedRequest> observations)
    {
        var fingerprints = observations.Select(ApiDownGuard.Fingerprint).ToList();
        var longestFailureStreak = 0;
        var currentStreak = 0;
        string? previousFailureFingerprint = null;

        for (var i = 0; i < observations.Count; i++)
        {
            if (ApiDownGuard.IsFailureLike(observations[i].StatusCode, observations[i].ContentType, observations[i].Body, observations[i].Error))
            {
                currentStreak = fingerprints[i] == previousFailureFingerprint ? currentStreak + 1 : 1;
                previousFailureFingerprint = fingerprints[i];
                longestFailureStreak = Math.Max(longestFailureStreak, currentStreak);
            }
            else
            {
                currentStreak = 0;
                previousFailureFingerprint = null;
            }
        }

        var signals = observations
            .SelectMany(request => RobustnessTestHelpers.DependencyFailureSignals(request.Body + " " + request.Error))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(signal => signal, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var representative = observations
            .OrderByDescending(request => RobustnessTestHelpers.DependencyFailureSignals(request.Body + " " + request.Error).Count)
            .ThenByDescending(request => request.StatusCode >= 500 || request.StatusCode == 0 ? 1 : 0)
            .ThenByDescending(request => request.DurationMs)
            .First();
        var stable = fingerprints.Distinct(StringComparer.Ordinal).Count() == 1;
        var degraded = observations.Any(request => request.StatusCode >= 500 || request.StatusCode == 0 || !string.IsNullOrWhiteSpace(request.Error));
        var risk = signals.Count > 0 ? 8 : degraded ? 6 : stable ? 2 : 5;

        return new DependencyAnalysis(observations, representative, signals, risk, stable, longestFailureStreak);
    }

    private static Dictionary<string, object> Mutation(DependencyAnalysis analysis, int requestCount)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "controlledDependencyProbe",
            ["requests"] = requestCount,
            ["statuses"] = analysis.Observations.Select(request => request.StatusCode).ToList(),
            ["stableFingerprint"] = analysis.StableFingerprint,
            ["failureStreak"] = analysis.LongestFailureStreak,
            ["dependencySignals"] = analysis.Signals
        };
    }

    private static string Evidence(EndpointModel endpoint, DependencyAnalysis analysis)
    {
        var statuses = string.Join(", ", analysis.Observations.Select(request => request.StatusCode));
        if (analysis.Signals.Count > 0)
        {
            return $"{endpoint.Method} {endpoint.Path} showed potential backend dependency resource exhaustion during repeated access; statuses: {statuses}; dependency signals: {string.Join(", ", analysis.Signals)}.";
        }

        return analysis.Risk >= 5
            ? $"{endpoint.Method} {endpoint.Path} degraded during repeated dependency resilience probes; statuses: {statuses}."
            : $"{endpoint.Method} {endpoint.Path} stayed stable during repeated dependency resilience probes; statuses: {statuses}.";
    }

    private static string AnalysisSummary(DependencyAnalysis analysis)
    {
        if (analysis.Signals.Count > 0)
        {
            return "dependencyResilience found downstream dependency or connection-pool failure signatures.";
        }

        return analysis.Risk >= 5
            ? "dependencyResilience found degraded behavior during repeated access."
            : "dependencyResilience observed stable repeated access behavior.";
    }

    private sealed record DependencyAnalysis(
        List<ExecutedRequest> Observations,
        ExecutedRequest Representative,
        List<string> Signals,
        int Risk,
        bool StableFingerprint,
        int LongestFailureStreak);
}
