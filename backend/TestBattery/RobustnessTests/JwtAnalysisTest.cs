using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class JwtAnalysisTest : RobustnessTest
{
    private static readonly JwtProbe[] Probes =
    [
        new("malformedJwt", "Bearer invalid.invalid.invalid"),
        new("unsignedJwtShape", "Bearer eyJhbGciOiJub25lIn0.eyJzdWIiOiJiZW5kaXQtcHJvYmUifQ."),
        new("emptyBearer", "Bearer ")
    ];

    public string BendType => "jwtAnalysis";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var observations = new List<JwtObservation>();
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
                [new KeyValuePair<string, string>("Authorization", probe.AuthorizationValue)],
                ["Authorization", "Cookie"],
                cancellationToken);
            observations.Add(new JwtObservation(probe, executed));
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
            risk >= 5 ? "jwtAnalysis found malformed JWT handling that should be reviewed." : "jwtAnalysis observed expected rejection of malformed JWT probes.",
            new Dictionary<string, string> { ["Accept"] = "application/json", ["Authorization"] = "Bearer ***" });
    }

    private static int Risk(EndpointModel endpoint, ExecutedRequest executed)
    {
        if (endpoint.AuthRequired && executed.StatusCode is >= 200 and < 300) return 9;
        if (executed.StatusCode >= 500) return 6;
        if (executed.StatusCode is 401 or 403) return 2;
        return endpoint.AuthRequired ? 5 : 3;
    }

    private static Dictionary<string, object> Mutation(List<JwtObservation> observations)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "malformedJwtBoundary",
            ["probes"] = observations.Select(item => new Dictionary<string, object>
            {
                ["name"] = item.Probe.Name,
                ["status"] = item.Executed.StatusCode
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<JwtObservation> observations, int risk)
    {
        var statuses = string.Join(", ", observations.Select(item => $"{item.Probe.Name}:{item.Executed.StatusCode}"));
        return risk >= 5
            ? $"{endpoint.Method} {endpoint.Path} accepted or mishandled malformed bearer token probes; statuses: {statuses}."
            : $"{endpoint.Method} {endpoint.Path} rejected malformed bearer token probes; statuses: {statuses}.";
    }

    private sealed record JwtProbe(string Name, string AuthorizationValue);
    private sealed record JwtObservation(JwtProbe Probe, ExecutedRequest Executed);
}
