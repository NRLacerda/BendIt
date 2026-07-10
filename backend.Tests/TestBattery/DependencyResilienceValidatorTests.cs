using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class DependencyResilienceValidatorTests
{
    public static async Task DependencyResilienceAcceptsStableDependency()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_dependency_healthy", "/api/dependency/healthy");
        var result = await RunDependencyAsync(server, endpoint, "dependency-healthy");

        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected stable dependency responses to be low risk");
        }

        if (!result.Evidence.Contains("stayed stable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected dependencyResilience evidence to describe stable repeated access");
        }
    }

    public static async Task DependencyResilienceFlagsMongoTimeout()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_dependency_mongo", "/api/dependency/mongo-timeout");
        var results = await RunAsync(server, [endpoint], ["dependencyResilience"], "dependency-mongo");
        var result = results.Results.First(item => item.BendType == "dependencyResilience");

        if (result.Risk < 8 || !result.Interesting)
        {
            throw new InvalidOperationException("expected Mongo dependency timeout to be high risk");
        }

        if (!result.Evidence.Contains("MongoTimeoutException", StringComparison.OrdinalIgnoreCase) ||
            !result.OwaspCategory.StartsWith("API4:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("expected dependencyResilience to report Mongo timeout signals and map to API4");
        }
    }

    public static async Task ApiDownGuardStopsAfterRepeatedMatchingFailures()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoints = Enumerable.Range(1, 6)
            .Select(index => TestSupport.EndpointFor(server, $"endpoint_dependency_guard_{index}", "/api/dependency/generic-failure"))
            .ToList();

        var results = await RunAsync(server, endpoints, ["authConsistency"], "api-down-stop");

        if (!results.Results.Any(result => result.BendType == "apiDownGuard"))
        {
            throw new InvalidOperationException("expected apiDownGuard result after repeated matching failures");
        }

        if (results.Results.Any(result => result.EndpointId == "endpoint_dependency_guard_6"))
        {
            throw new InvalidOperationException("expected apiDownGuard to stop before the sixth endpoint was tested");
        }
    }

    public static async Task ApiDownGuardDoesNotStopBelowThreshold()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoints = Enumerable.Range(1, 4)
            .Select(index => TestSupport.EndpointFor(server, $"endpoint_dependency_below_{index}", "/api/dependency/generic-failure"))
            .Append(TestSupport.EndpointFor(server, "endpoint_dependency_after_below", "/api/dependency/healthy"))
            .ToList();

        var results = await RunAsync(server, endpoints, ["authConsistency"], "api-down-below");

        if (results.Results.Any(result => result.BendType == "apiDownGuard"))
        {
            throw new InvalidOperationException("expected no apiDownGuard below threshold");
        }

        if (!results.Results.Any(result => result.EndpointId == "endpoint_dependency_after_below"))
        {
            throw new InvalidOperationException("expected test battery to continue below threshold");
        }
    }

    public static async Task ApiDownGuardDoesNotStopDifferentFingerprints()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoints = Enumerable.Range(1, 5)
            .Select(index => TestSupport.EndpointFor(server, $"endpoint_dependency_mixed_{index}", "/api/dependency/mixed-failure"))
            .ToList();

        var results = await RunAsync(server, endpoints, ["authConsistency"], "api-down-mixed");

        if (results.Results.Any(result => result.BendType == "apiDownGuard"))
        {
            throw new InvalidOperationException("expected no apiDownGuard for alternating failure fingerprints");
        }
    }

    public static async Task ApiDownGuardCanBeDisabled()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_dependency_disabled", "/api/dependency/mongo-timeout");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "api-down-disabled",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["dependencyResilience", "authConsistency"],
            DownDetectionThreshold = 0,
            ParallelWorkers = 1
        }, CancellationToken.None);

        if (results.Results.Any(result => result.BendType is "dependencyResilience" or "apiDownGuard"))
        {
            throw new InvalidOperationException("expected threshold 0 to disable dependencyResilience and apiDownGuard");
        }
    }

    private static async Task<TestResult> RunDependencyAsync(LocalApiServer server, Endpoint endpoint, string projectId)
    {
        var results = await RunAsync(server, [endpoint], ["dependencyResilience"], projectId);
        return results.Results.First(result => result.BendType == "dependencyResilience");
    }

    private static Task<ResultsDocument> RunAsync(LocalApiServer server, IReadOnlyList<Endpoint> endpoints, List<string> bendTypes, string projectId)
    {
        return new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, endpoints, new TestRunRequest
        {
            BendTypes = bendTypes,
            ParallelWorkers = 1
        }, CancellationToken.None);
    }
}
