using EndpointModel = BendIt.Api.Models.Endpoint;

namespace BendIt.Api.TestBattery.RobustnessTests;

internal sealed class ResponseDiffingTest : RobustnessTest
{
    public string BendType => "responseDiffing";

    public bool AppliesTo(EndpointModel endpoint, TestRunRequest request)
    {
        return true;
    }

    public async Task<TestResult> RunAsync(RobustnessTestContext context, CancellationToken cancellationToken)
    {
        var url = RobustnessTestHelpers.EndpointUrl(context.Endpoint, BendType);
        var first = await RobustnessTestHelpers.ExecuteAsync(context.Client, context.Endpoint.Method, url, null, context.Project, BendType, cancellationToken);
        var second = await RobustnessTestHelpers.ExecuteAsync(context.Client, context.Endpoint.Method, url, null, context.Project, BendType, cancellationToken);
        var signals = DiffSignals(first, second);
        var risk = Risk(first, second, signals);
        var representative = signals.Count > 0 ? second : first;
        return RobustnessResultFactory.Create(
            context,
            BendType,
            url,
            null,
            representative,
            risk,
            Mutation(first, second, signals),
            Evidence(context.Endpoint, first, second, signals),
            risk >= 5 ? "responseDiffing found inconsistent repeated responses that should be reviewed." : "responseDiffing observed stable repeated response behavior.");
    }

    private static int Risk(ExecutedRequest first, ExecutedRequest second, List<string> signals)
    {
        if (first.StatusCode >= 500 || second.StatusCode >= 500) return 6;
        if (signals.Any(signal => signal == "status changed")) return 5;
        return signals.Count > 0 ? 3 : 1;
    }

    private static Dictionary<string, object> Mutation(ExecutedRequest first, ExecutedRequest second, List<string> signals)
    {
        return new Dictionary<string, object>
        {
            ["type"] = "repeatResponseComparison",
            ["firstStatus"] = first.StatusCode,
            ["secondStatus"] = second.StatusCode,
            ["firstBodySizeBytes"] = first.BodySizeBytes,
            ["secondBodySizeBytes"] = second.BodySizeBytes,
            ["signals"] = signals
        };
    }

    private static string Evidence(EndpointModel endpoint, ExecutedRequest first, ExecutedRequest second, List<string> signals)
    {
        return signals.Count == 0
            ? $"{endpoint.Method} {endpoint.Path} returned stable repeated responses with HTTP {first.StatusCode}."
            : $"{endpoint.Method} {endpoint.Path} returned inconsistent repeated responses; statuses {first.StatusCode}, {second.StatusCode}; signals: {string.Join(", ", signals)}.";
    }

    private static List<string> DiffSignals(ExecutedRequest first, ExecutedRequest second)
    {
        var signals = new List<string>();
        if (first.StatusCode != second.StatusCode) signals.Add("status changed");
        if (!string.Equals(first.ContentType, second.ContentType, StringComparison.OrdinalIgnoreCase)) signals.Add("content type changed");
        if (BodySizeClass(first.BodySizeBytes) != BodySizeClass(second.BodySizeBytes)) signals.Add("body size changed");
        if (!string.Equals(RobustnessTestHelpers.ShortHash(first.Body), RobustnessTestHelpers.ShortHash(second.Body), StringComparison.Ordinal)) signals.Add("body hash changed");
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
}
