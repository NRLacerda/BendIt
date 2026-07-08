using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class ParameterPollutionValidatorTests
{
    public static async Task ParameterPollutionValidatorAcceptsStrictQueryRejection()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_parameter_pollution_query_strict", "/api/parameter-pollution/query-strict");
        endpoint.QueryParams = ["id", "role"];
        var result = await RunSingleAsync(server, endpoint, "parameter-pollution-query-strict");

        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected strict duplicate query rejection to be low risk");
        }

        if (!result.Evidence.Contains("rejected or ignored duplicated parameters", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected parameterPollution evidence to describe safe duplicate handling");
        }

        if (!result.Mutation.TryGetValue("baselineStatus", out var baselineStatus) || Convert.ToInt32(baselineStatus) != 200)
        {
            throw new InvalidOperationException("expected parameterPollution mutation to record a successful baseline");
        }
    }

    public static async Task ParameterPollutionValidatorFlagsQueryOverride()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_parameter_pollution_query_vulnerable", "/api/parameter-pollution/query-vulnerable");
        endpoint.QueryParams = ["id", "role"];
        var result = await RunSingleAsync(server, endpoint, "parameter-pollution-query-vulnerable");

        if (result.Risk < 7 || !result.Interesting)
        {
            throw new InvalidOperationException("expected duplicate query override to be a finding");
        }

        if (!result.Evidence.Contains("duplicateQuery", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("privileged value accepted", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("alternate identifier accepted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected parameterPollution evidence to mention query override signals");
        }

        if (!result.OwaspCategory.StartsWith("API3:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("expected privileged parameter pollution to map to OWASP API3");
        }
    }

    public static async Task ParameterPollutionValidatorAcceptsStrictBodyRejection()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_parameter_pollution_body_strict", "/api/parameter-pollution/body-strict", "POST");
        var result = await RunSingleAsync(server, endpoint, "parameter-pollution-body-strict");

        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected strict duplicate body rejection to be low risk");
        }

        if (!result.Evidence.Contains("rejected or ignored duplicated parameters", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected safe body duplicate handling evidence");
        }
    }

    public static async Task ParameterPollutionValidatorFlagsBodyOverride()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_parameter_pollution_body_vulnerable", "/api/parameter-pollution/body-vulnerable", "POST");
        var result = await RunSingleAsync(server, endpoint, "parameter-pollution-body-vulnerable");

        if (result.Risk < 7 || !result.Interesting)
        {
            throw new InvalidOperationException("expected duplicate body override to be a finding");
        }

        if (!result.Evidence.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("privileged value accepted", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("alternate identifier accepted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected parameterPollution evidence to mention body override signals");
        }

        if (!result.Request.HeadersMasked.TryGetValue("Content-Type", out var contentType) || string.IsNullOrWhiteSpace(contentType))
        {
            throw new InvalidOperationException("expected representative parameterPollution request to include the body content type");
        }
    }

    private static async Task<TestResult> RunSingleAsync(LocalApiServer server, Endpoint endpoint, string projectId)
    {
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["parameterPollution"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
