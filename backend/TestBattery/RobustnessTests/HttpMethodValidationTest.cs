using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class HttpMethodValidationTest : RobustnessTest
{
    private static readonly string[] CandidateMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    public string BendType => "httpMethodValidation";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var probes = CandidateMethods
            .Where(method => !method.Equals(context.Endpoint.Method, StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .ToList();
        var observations = new List<MethodObservation>();
        foreach (var method in probes)
        {
            var body = RobustnessTestHelpers.AllowsRequestBody(method) ? """{"payload":"method-validation"}""" : null;
            var executed = await RobustnessTestHelpers.ExecuteAsync(context.Client, method, url, body, context.Project, BendType, cancellationToken);
            observations.Add(new MethodObservation(method, body, executed));
        }

        var representative = observations.OrderByDescending(item => Risk(item.Executed)).First();
        var risk = observations.Max(item => Risk(item.Executed));
        return RobustnessResultFactory.Create(
            context,
            BendType,
            url,
            representative.Body,
            representative.Executed,
            risk,
            Mutation(context.Endpoint.Method, observations),
            Evidence(context.Endpoint, observations, risk),
            risk >= 5 ? "httpMethodValidation found alternate HTTP methods that were accepted." : "httpMethodValidation observed alternate methods being rejected or safely handled.",
            title: $"httpMethodValidation observed HTTP {representative.Executed.StatusCode}");
    }

    private static int Risk(ExecutedRequest executed)
    {
        if (executed.StatusCode is >= 200 and < 300) return 7;
        if (executed.StatusCode >= 500) return 6;
        if (executed.StatusCode is 405 or 404 or 401 or 403) return 2;
        return executed.StatusCode >= 400 ? 3 : 5;
    }

    private static Dictionary<string, object> Mutation(string expectedMethod, List<MethodObservation> observations)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "httpMethodProbe",
            ["expectedMethod"] = expectedMethod,
            ["probes"] = observations.Select(item => new Dictionary<string, object>
            {
                ["method"] = item.Method,
                ["status"] = item.Executed.StatusCode,
                ["accepted"] = item.Executed.StatusCode is >= 200 and < 300
            }).ToList()
        };
    }

    private static string Evidence(EndpointModel endpoint, List<MethodObservation> observations, int risk)
    {
        var accepted = observations.Where(item => item.Executed.StatusCode is >= 200 and < 300).Select(item => item.Method).ToList();
        var statuses = string.Join(", ", observations.Select(item => $"{item.Method}:{item.Executed.StatusCode}"));
        return accepted.Count > 0
            ? $"{endpoint.Method} {endpoint.Path} accepted unexpected HTTP method(s): {string.Join(", ", accepted)}; statuses: {statuses}."
            : $"{endpoint.Method} {endpoint.Path} rejected alternate HTTP method probes; statuses: {statuses}.";
    }

    private sealed record MethodObservation(string Method, string? Body, ExecutedRequest Executed);
}
