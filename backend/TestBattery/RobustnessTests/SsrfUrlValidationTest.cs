using System.Text.Json;
using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class SsrfUrlValidationTest : RobustnessTest
{
    private const string BendTypeValue = "ssrfUrlValidation";

    private static readonly SsrfProbe[] Probes =
    [
        new("url", "http://example.invalid/bendit-ssrf-probe"),
        new("targetUrl", "https://example.invalid/bendit-ssrf-probe"),
        new("callbackUrl", "http://example.test/bendit-ssrf-probe"),
        new("webhookUrl", "https://example.test/bendit-ssrf-probe")
    ];

    private static readonly string[] SafeRejectionTerms =
    [
        "invalid url",
        "invalid uri",
        "not allowed",
        "disallowed",
        "blocked",
        "forbidden",
        "unsupported",
        "validation",
        "error"
    ];

    private static readonly string[] FetchAttemptTerms =
    [
        "dns",
        "resolve",
        "enotfound",
        "eai_again",
        "connection refused",
        "timeout",
        "fetch failed",
        "download failed",
        "could not connect",
        "name or service not known"
    ];

    public string BendType => BendTypeValue;

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return RobustnessTestHelpers.AllowsRequestBody(endpoint.Method);
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var observations = new List<SsrfObservation>();

        foreach (var probe in Probes)
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [probe.Field] = probe.Url
            });
            var executed = await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                context.Endpoint.Method,
                url,
                body,
                context.Project,
                BendType,
                cancellationToken);

            observations.Add(new SsrfObservation(probe, body, executed, Signals(executed), Risk(executed)));
        }

        var representative = observations
            .OrderByDescending(observation => observation.Risk)
            .ThenBy(observation => observation.Executed.StatusCode is >= 400 and < 500 ? 1 : 0)
            .ThenBy(observation => observation.Probe.Field, StringComparer.Ordinal)
            .First();

        var now = DateTimeOffset.UtcNow;
        var risk = representative.Risk;
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, representative.Executed.StatusCode, risk);

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"ssrfUrlValidation observed HTTP {representative.Executed.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(observations, representative),
            Request = new ResultRequest
            {
                HeadersMasked = RobustnessTestHelpers.MaskedHeaders(context.Project),
                Body = representative.Body,
                BodySizeBytes = representative.Body.Length
            },
            Result = new HttpResult
            {
                StatusCode = representative.Executed.StatusCode,
                StatusText = representative.Executed.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(representative.Executed.ResponseHeaders),
                ContentType = representative.Executed.ContentType,
                BodySizeBytes = representative.Executed.BodySizeBytes,
                DurationMs = representative.Executed.DurationMs
            },
            ResultBody = representative.Executed.Body,
            Evidence = Evidence(context.Endpoint, representative, observations),
            Outcome = RobustnessTestHelpers.OutcomeFor(BendType, representative.Executed.StatusCode, risk),
            Interesting = interesting,
            AnalysisSummary = AnalysisSummary(risk, representative),
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

    private static Dictionary<string, object> Mutation(List<SsrfObservation> observations, SsrfObservation representative)
    {
        return new Dictionary<string, object>
        {
            ["type"] = BendTypeValue,
            ["field"] = representative.Probe.Field,
            ["probeUrl"] = representative.Probe.Url,
            ["safeReservedHostsOnly"] = true,
            ["statuses"] = observations.ToDictionary(
                observation => observation.Probe.Field,
                observation => observation.Executed.StatusCode,
                StringComparer.OrdinalIgnoreCase),
            ["signals"] = representative.Signals
        };
    }

    private static int Risk(ExecutedRequest executed)
    {
        if (!string.IsNullOrWhiteSpace(executed.Error)) return 6;
        if (executed.StatusCode is >= 400 and < 500) return 2;

        var signals = Signals(executed);
        if (signals.Any(signal => signal.StartsWith("fetch-", StringComparison.OrdinalIgnoreCase)))
        {
            return executed.StatusCode >= 500 ? 7 : 6;
        }

        if (executed.StatusCode >= 500) return 6;
        if (executed.StatusCode is >= 200 and < 400)
        {
            return HasSafeRejectionMessage(executed.Body) ? 2 : 5;
        }

        return 3;
    }

    private static List<string> Signals(ExecutedRequest executed)
    {
        var signals = new List<string>();
        if (!string.IsNullOrWhiteSpace(executed.Error))
        {
            signals.Add("request-failed");
        }

        var body = executed.Body ?? "";
        foreach (var term in SafeRejectionTerms)
        {
            if (body.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                signals.Add("safe-rejection:" + term);
            }
        }

        foreach (var term in FetchAttemptTerms)
        {
            if (body.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (executed.Error?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                signals.Add("fetch-attempt:" + term);
            }
        }

        if (executed.StatusCode is >= 400 and < 500)
        {
            signals.Add("client-error-rejection");
        }

        return signals.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasSafeRejectionMessage(string body)
    {
        return SafeRejectionTerms.Any(term => body.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Evidence(EndpointModel endpoint, SsrfObservation representative, List<SsrfObservation> observations)
    {
        var statuses = string.Join(", ", observations.Select(observation => $"{observation.Probe.Field}:{observation.Executed.StatusCode}"));
        var signalText = representative.Signals.Count == 0 ? "none" : string.Join(", ", representative.Signals);
        return representative.Risk >= 5
            ? $"{endpoint.Method} {endpoint.Path} showed potential unsafe server-side URL consumption for field {representative.Probe.Field}; probe host was documentation-reserved; statuses: {statuses}; signals: {signalText}."
            : $"{endpoint.Method} {endpoint.Path} rejected or safely handled SSRF-safe URL probes using documentation-reserved hosts; statuses: {statuses}; signals: {signalText}.";
    }

    private static string AnalysisSummary(int risk, SsrfObservation representative)
    {
        if (risk >= 5)
        {
            return $"ssrfUrlValidation found that URL-looking field {representative.Probe.Field} was accepted or produced fetch-related behavior.";
        }

        return "ssrfUrlValidation observed expected rejection or safe handling of URL-looking fields.";
    }

    private sealed record SsrfProbe(string Field, string Url);

    private sealed record SsrfObservation(SsrfProbe Probe, string Body, ExecutedRequest Executed, List<string> Signals, int Risk);
}
