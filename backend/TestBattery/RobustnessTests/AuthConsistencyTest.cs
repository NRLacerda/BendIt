using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class AuthConsistencyTest : RobustnessTest
{
    private static readonly string[] AuthHeaderNames = ["Authorization", "Cookie", "X-API-Key", "Api-Key"];

    public string BendType => "authConsistency";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var executed = await RobustnessTestHelpers.ExecuteAsync(
            context.Client,
            context.Endpoint.Method,
            url,
            null,
            context.Project,
            BendType,
            "application/json",
            true,
            [],
            AuthHeaderNames,
            cancellationToken);
        var risk = Risk(context.Endpoint, executed);
        var mutation = new Dictionary<string, object>
        {
            ["type"] = "missingAuthentication",
            ["removedHeaders"] = AuthHeaderNames,
            ["status"] = executed.StatusCode
        };
        var requestHeaders = RobustnessTestHelpers.MaskedHeaders(context.Project);
        foreach (var header in AuthHeaderNames)
        {
            requestHeaders.Remove(header);
        }

        return RobustnessResultFactory.Create(
            context,
            BendType,
            url,
            null,
            executed,
            risk,
            mutation,
            Evidence(context.Endpoint, executed, risk),
            AnalysisSummary(executed, risk),
            requestHeaders);
    }

    private static int Risk(EndpointModel endpoint, ExecutedRequest executed)
    {
        if (endpoint.AuthRequired && executed.StatusCode is >= 200 and < 300) return 9;
        if (executed.StatusCode >= 500) return 6;
        if (executed.StatusCode is 401 or 403) return 2;
        if (executed.StatusCode >= 400) return 2;
        return endpoint.AuthRequired ? 5 : 3;
    }

    private static string Evidence(EndpointModel endpoint, ExecutedRequest executed, int risk)
    {
        var suffix = executed.Truncated ? $" Response capture truncated at {RobustnessTestHelpers.MaxResultBodyBytes} bytes." : "";
        if (endpoint.AuthRequired && executed.StatusCode is >= 200 and < 300)
        {
            return $"{endpoint.Method} {endpoint.Path} returned HTTP {executed.StatusCode} when authentication headers were removed.{suffix}";
        }

        return risk >= 5
            ? $"{endpoint.Method} {endpoint.Path} did not clearly reject a missing-authentication probe; observed HTTP {executed.StatusCode}.{suffix}"
            : $"{endpoint.Method} {endpoint.Path} rejected or safely handled a missing-authentication probe with HTTP {executed.StatusCode}.{suffix}";
    }

    private static string AnalysisSummary(ExecutedRequest executed, int risk)
    {
        return risk >= 5
            ? $"authConsistency found potentially inconsistent authentication enforcement with HTTP {executed.StatusCode}."
            : $"authConsistency observed expected authentication boundary behavior with HTTP {executed.StatusCode}.";
    }
}
