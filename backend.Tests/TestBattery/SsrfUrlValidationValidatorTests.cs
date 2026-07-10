using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class SsrfUrlValidationValidatorTests
{
    public static async Task SsrfUrlValidationAcceptsClientErrorRejection()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/ssrf/strict", "ssrf-strict");

        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected 4xx URL rejection to be low risk");
        }

        if (!result.Evidence.Contains("rejected or safely handled", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected ssrfUrlValidation evidence to describe safe handling");
        }

        if (!result.Mutation.TryGetValue("safeReservedHostsOnly", out var safeOnly) || safeOnly is not bool value || !value)
        {
            throw new InvalidOperationException("expected ssrfUrlValidation mutation to record safe reserved host usage");
        }
    }

    public static async Task SsrfUrlValidationAcceptsSafeRejectionMessage()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/ssrf/message-rejection", "ssrf-message-rejection");

        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected explicit invalid URL message to be low risk");
        }

        if (!result.ResultBody.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected representative response to contain safe rejection language");
        }
    }

    public static async Task SsrfUrlValidationFlagsAcceptedUrlFields()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/ssrf/accepted", "ssrf-accepted");

        if (result.Risk < 5 || !result.Interesting)
        {
            throw new InvalidOperationException("expected accepted URL-looking field to be suspicious");
        }

        if (!result.Evidence.Contains("potential unsafe server-side URL consumption", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected ssrfUrlValidation evidence to mention potential unsafe URL consumption");
        }

        if (!result.OwaspCategory.StartsWith("API10:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("expected ssrfUrlValidation to map to OWASP API10");
        }
    }

    public static async Task SsrfUrlValidationFlagsFetchRelatedErrors()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/ssrf/fetch-error", "ssrf-fetch-error");

        if (result.Risk < 7 || !result.Interesting)
        {
            throw new InvalidOperationException("expected fetch-related server error to be a finding");
        }

        if (!result.Evidence.Contains("fetch-attempt", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected ssrfUrlValidation evidence to mention fetch-attempt signals");
        }
    }

    public static async Task SsrfUrlValidationSkipsGetEndpoints()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_ssrf_get", "/api/ssrf/strict");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "ssrf-get-skip",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["ssrfUrlValidation"],
            ParallelWorkers = 1
        }, CancellationToken.None);

        if (results.Results.Count != 0)
        {
            throw new InvalidOperationException("expected ssrfUrlValidation to be skipped for GET endpoints");
        }
    }

    private static async Task<TestResult> RunSingleAsync(LocalApiServer server, string path, string projectId)
    {
        var endpoint = TestSupport.EndpointFor(server, "endpoint_" + projectId.Replace("-", "_"), path, "POST");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["ssrfUrlValidation"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
