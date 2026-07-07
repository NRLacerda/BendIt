using BendIt.Api.Models;
using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery;

internal abstract class SingleRequestRobustnessTest : RobustnessTest
{
    public abstract string BendType { get; }

    public virtual bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var body = RequestBody(context.Request);
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var executed = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            url,
            body,
            context.Project,
            BendType,
            cancellationToken);

        return CreateResult(context, url, body, executed);
    }

    protected virtual string? RequestBody(TestRunRequest request)
    {
        return null;
    }

    protected virtual Dictionary<string, object> Mutation(TestRunRequest request)
    {
        return new Dictionary<string, object> { ["type"] = BendType };
    }

    protected virtual int Risk(EndpointModel endpoint, ExecutedRequest executed)
    {
        return RobustnessTestHelpers.RiskFor(BendType, executed.StatusCode, endpoint, executed);
    }

    protected virtual string Evidence(EndpointModel endpoint, ExecutedRequest executed)
    {
        return RobustnessTestHelpers.EvidenceFor(endpoint, BendType, executed.StatusCode, executed);
    }

    protected virtual bool SensitiveDataDetected(ExecutedRequest executed, int risk)
    {
        return risk >= 8 || BendType == "sensitiveDataExposure" && RobustnessTestHelpers.SensitiveDataFindings(executed).Count > 0;
    }

    private TestResult CreateResult(RobustnessTestContext context, string url, string? body, ExecutedRequest executed)
    {
        var risk = Risk(context.Endpoint, executed);
        var outcome = RobustnessTestHelpers.OutcomeFor(BendType, executed.StatusCode, risk);
        var interesting = RobustnessTestHelpers.IsInteresting(BendType, executed.StatusCode, risk);
        var now = DateTimeOffset.UtcNow;

        return new TestResult
        {
            Id = "result_" + RobustnessTestHelpers.ShortHash(context.Endpoint.Id + BendType + now.ToString("O")),
            ProjectId = context.Project.ProjectId,
            TestRunId = context.TestRunId,
            EndpointId = context.Endpoint.Id,
            BendType = BendType,
            Category = RobustnessTestHelpers.CategoryFor(BendType),
            Title = $"{BendType} returned HTTP {executed.StatusCode}",
            Method = context.Endpoint.Method,
            Url = url,
            OriginalUrl = url,
            Mutation = Mutation(context.Request),
            Request = new ResultRequest
            {
                HeadersMasked = RobustnessTestHelpers.MaskedHeaders(context.Project),
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
            Evidence = Evidence(context.Endpoint, executed),
            Outcome = outcome,
            Interesting = interesting,
            AnalysisSummary = RobustnessTestHelpers.AnalysisSummary(BendType, executed.StatusCode, risk, interesting, executed),
            OwaspCategory = RobustnessTestHelpers.OwaspCategoryFor(BendType),
            Recommendation = RobustnessTestHelpers.RecommendationFor(BendType, risk),
            Risk = risk,
            Severity = RobustnessTestHelpers.SeverityFor(risk),
            Confidence = Math.Min(96, 58 + risk * 4),
            Reproducible = risk >= 6,
            SensitiveDataDetected = SensitiveDataDetected(executed, risk),
            TokenUsed = context.Project.Auth.Type != "none",
            AuthContext = context.Project.Auth,
            CreatedAt = now
        };
    }
}
