using BendIt.Api.Models;

namespace BendIt.Api.TestBattery;

internal static class RobustnessResultFactory
{
    public static TestResult Create(
        RobustnessTestContext context,
        string bendType,
        string url,
        string? body,
        ExecutedRequest executed,
        int risk,
        Dictionary<string, object> mutation,
        string evidence,
        string analysisSummary,
        Dictionary<string, string>? requestHeaders = null,
        bool sensitiveDataDetected = false,
        string? title = null,
        string? category = null,
        string? owaspCategory = null,
        string? recommendation = null)
    {
        var now = DateTimeOffset.UtcNow;
        var interesting = RobustnessTestHelpers.IsInteresting(bendType, executed.StatusCode, risk);
        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + bendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = bendType,
            Category = category ?? RobustnessTestHelpers.CategoryFor(bendType),
            Title = title ?? $"{bendType} returned HTTP {executed.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = mutation,
            Request = new ResultRequest
            {
                HeadersMasked = requestHeaders ?? RobustnessTestHelpers.MaskedHeaders(context.Project),
                Body = body,
                BodySizeBytes = body?.Length ?? 0
            },
            Result = new HttpResult
            {
                StatusCode = executed.StatusCode,
                StatusText = executed.StatusText,
                HeadersMasked = RobustnessTestHelpers.MaskResponseHeaders(executed.ResponseHeaders),
                ContentType = executed.ContentType,
                BodySizeBytes = executed.BodySizeBytes,
                DurationMs = executed.DurationMs
            },
            ResultBody = executed.Body,
            Evidence = evidence,
            Outcome = RobustnessTestHelpers.OutcomeFor(bendType, executed.StatusCode, risk),
            Interesting = interesting,
            AnalysisSummary = analysisSummary,
            OwaspCategory = owaspCategory ?? RobustnessTestHelpers.OwaspCategoryFor(bendType),
            Recommendation = recommendation ?? RobustnessTestHelpers.RecommendationFor(bendType, risk),
            Risk = risk,
            Severity = RobustnessTestHelpers.SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = sensitiveDataDetected,
            TokenUsed = context.Project.Auth.Type != "none",
            AuthContext = context.Project.Auth,
            CreatedAt = now
        };
    }
}
