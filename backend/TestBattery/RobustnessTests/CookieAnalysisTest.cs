using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class CookieAnalysisTest : RobustnessTest
{
    private static readonly CookieProbe[] Probes =
    [
        new("invalidSessionCookie", "bendit_invalid_auth=1"),
        new("emptySessionCookie", "session="),
        new("duplicatedSessionCookie", "session=invalid; session=also-invalid")
    ];

    public string BendType => "cookieAnalysis";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var observations = new List<CookieObservation>();
        foreach (var probe in Probes)
        {
            var executed = await RobustnessTestHelpers.ExecuteAsync(
                context.Client,
                context.Endpoint.Method,
                url,
                null,
                context.Project,
                BendType,
                "application/json",
                true,
                [new KeyValuePair<string, string>("Cookie", probe.CookieValue)],
                ["Authorization", "Cookie"],
                cancellationToken);
            observations.Add(new CookieObservation(probe, executed));
        }

        var representative = observations.OrderByDescending(item => Risk(context.Endpoint, item.Executed)).First();
        var risk = Risk(context.Endpoint, representative.Executed);
        return RobustnessResultFactory.Create(
            context,
            BendType,
            url,
            null,
            representative.Executed,
            risk,
            Mutation(observations),
            Evidence(context.Endpoint, observations, risk),
            risk >= 5 ? "cookieAnalysis found invalid cookie handling that should be reviewed." : "cookieAnalysis observed expected rejection of invalid cookie probes.",
            new Dictionary<string, string> { ["Accept"] = "application/json", ["Cookie"] = "***" });
    }

    private static int Risk(EndpointModel endpoint, ExecutedRequest executed)
    {
        if (endpoint.AuthRequired && executed.StatusCode is >= 200 and < 300) return 9;
        if (executed.StatusCode >= 500) return 6;
        if (executed.StatusCode is 401 or 403) return 2;
        return endpoint.AuthRequired ? 5 : 3;
    }

    private static Dictionary<string, object> Mutation(List<CookieObservation> observations)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "malformedCookieBoundary",
            ["probes"] = observations.Select(item => new Dictionary<string, object>
            {
                ["name"] = item.Probe.Name,
                ["status"] = item.Executed.StatusCode
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<CookieObservation> observations, int risk)
    {
        var statuses = string.Join(", ", observations.Select(item => $"{item.Probe.Name}:{item.Executed.StatusCode}"));
        return risk >= 5
            ? $"{endpoint.Method} {endpoint.Path} accepted or mishandled invalid cookie probes; statuses: {statuses}."
            : $"{endpoint.Method} {endpoint.Path} rejected invalid cookie probes; statuses: {statuses}.";
    }

    private sealed record CookieProbe(string Name, string CookieValue);
    private sealed record CookieObservation(CookieProbe Probe, ExecutedRequest Executed);
}
