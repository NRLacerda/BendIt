using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class RateLimitValidatorTests
{
    public static async Task RateLimitValidatorDetectsThrottledBurst()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/rate-limit/throttled", "rate-limit-throttled", 5);
        if (result.Risk >= 5 || result.Interesting) throw new InvalidOperationException("expected throttled burst with headers and healthy post-burst response to be low risk");
        if (!result.Evidence.Contains("throttling headers observed", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("429", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected rateLimit evidence to mention 429 and throttling headers");
    }

    public static async Task RateLimitValidatorDetectsMissingThrottling()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/rate-limit/open", "rate-limit-missing", 4);
        if (result.Risk < 5 || !result.Interesting) throw new InvalidOperationException("expected missing throttling to be suspicious");
        if (!result.Evidence.Contains("no HTTP 429 observed", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("no throttling headers observed", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected rateLimit evidence to mention missing 429 and headers");
    }

    public static async Task RateLimitValidatorDetectsDegradedPostBurstResponse()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/rate-limit/degraded", "rate-limit-degraded", 3);
        if (result.Risk < 6 || !result.Interesting) throw new InvalidOperationException("expected degraded post-burst behavior to be medium risk");
        if (!result.Evidence.Contains("post-burst response differed", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected rateLimit evidence to mention degraded post-burst response");
    }

    public static async Task RateLimitValidatorCapsRequestedBurstSize()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/rate-limit/cap", "rate-limit-cap", 50);
        if (!result.Mutation.TryGetValue("burstRequests", out var burstRequests) || Convert.ToInt32(burstRequests) != 10)
        {
            throw new InvalidOperationException("expected rateLimit burstRequests mutation value to be capped at 10");
        }
    }

    private static async Task<TestResult> RunSingleAsync(LocalApiServer server, string path, string projectId, int maxRequests)
    {
        var endpoint = TestSupport.EndpointFor(server, "endpoint_" + projectId.Replace("-", "_"), path);
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["rateLimit"],
            MaxRequestsPerEndpoint = maxRequests,
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
