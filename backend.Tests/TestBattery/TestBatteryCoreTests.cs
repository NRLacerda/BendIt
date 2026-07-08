using System.Text;
using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class TestBatteryCoreTests
{
    public static async Task TestBatteryCapsResponseBodyCapture()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_large", "/api/large");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "large-body",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["authConsistency"],
            ParallelWorkers = 1
        }, CancellationToken.None);

        var result = results.Results.Single();
        if (Encoding.UTF8.GetByteCount(result.ResultBody) > 100 * 1024)
        {
            throw new InvalidOperationException($"expected capped resultBody <= 100 KB, got {Encoding.UTF8.GetByteCount(result.ResultBody)} bytes");
        }

        if (!result.Evidence.Contains("truncated", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected evidence to mention truncated response capture");
        }
    }

    public static async Task TestBatteryRoutesBodyTestsByHttpVerb()
    {
        await using var server = await LocalApiServer.StartAsync();
        var getEndpoint = TestSupport.EndpointFor(server, "endpoint_get", "/api/orgrow/environment/setup/{id}/{id}");
        var postEndpoint = TestSupport.EndpointFor(server, "endpoint_post", "/api/orgrow/product/create", "POST");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "verb-routing",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [getEndpoint, postEndpoint], new TestRunRequest
        {
            BendTypes = ["fieldSize", "requestSize", "massAssignment", "idMutation", "authConsistency"],
            FieldSizesKb = [8],
            BodySizesKb = [12],
            ParallelWorkers = 1
        }, CancellationToken.None);

        if (results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType is "fieldSize" or "requestSize" or "massAssignment"))
        {
            throw new InvalidOperationException("expected body-oriented tests to be skipped for GET endpoints");
        }

        if (!results.Results.Any(result => result.EndpointId == getEndpoint.Id && result.BendType == "idMutation"))
        {
            throw new InvalidOperationException("expected idMutation to run for GET endpoint with path identifiers");
        }

        var postFieldSize = results.Results.SingleOrDefault(result => result.EndpointId == postEndpoint.Id && result.BendType == "fieldSize")
            ?? throw new InvalidOperationException("expected fieldSize to run for POST endpoint");
        if (postFieldSize.Request.BodySizeBytes < 8 * 1024)
        {
            throw new InvalidOperationException($"expected fieldSize body to use configured KB size, got {postFieldSize.Request.BodySizeBytes} bytes");
        }
    }

    public static async Task TestBatteryMapsResultsToOwaspCategories()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_owasp", "/api/orgrow/environment/setup/{id}/{id}");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "owasp-mapping",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["idMutation", "authConsistency"],
            ParallelWorkers = 1
        }, CancellationToken.None);

        if (results.Results.Any(result => string.IsNullOrWhiteSpace(result.OwaspCategory)))
        {
            throw new InvalidOperationException("expected every result to include an OWASP category");
        }

        if (results.Results.Any(result => string.IsNullOrWhiteSpace(result.Recommendation)))
        {
            throw new InvalidOperationException("expected every result to include a recommendation");
        }

        if (!results.Results.Any(result => result.BendType == "idMutation" && result.OwaspCategory.StartsWith("API1:", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("expected idMutation to map to OWASP API1");
        }
    }

    public static async Task HttpMethodValidatorAcceptsStrictMethodEnforcement()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_method_strict", "/api/method/strict");
        var result = (await RunSingleAsync(server, endpoint, ["httpMethodValidation"], "method-strict")).Results.Single();
        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected strict HTTP method enforcement to be low risk");
        }

        if (!result.Evidence.Contains("rejected alternate HTTP method probes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected httpMethodValidation evidence to mention rejected alternate methods");
        }
    }

    public static async Task HttpMethodValidatorFlagsAlternateMethodAcceptance()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_method_open", "/api/method/open");
        var result = (await RunSingleAsync(server, endpoint, ["httpMethodValidation"], "method-open")).Results.Single();
        if (result.Risk < 7 || !result.Interesting)
        {
            throw new InvalidOperationException("expected alternate HTTP method acceptance to be a finding");
        }

        if (!result.Evidence.Contains("accepted unexpected HTTP method", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected httpMethodValidation evidence to mention accepted alternate methods");
        }
    }

    public static async Task ResponseDiffingAcceptsStableResponses()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_response_stable", "/api/response-diff/stable");
        var result = (await RunSingleAsync(server, endpoint, ["responseDiffing"], "response-stable")).Results.Single();
        if (result.Risk >= 5 || result.Interesting)
        {
            throw new InvalidOperationException("expected stable repeated responses to be low risk");
        }

        if (!result.Evidence.Contains("stable repeated responses", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected responseDiffing evidence to mention stable repeated responses");
        }
    }

    public static async Task ResponseDiffingFlagsChangedResponseBodies()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_response_flaky", "/api/response-diff/flaky");
        var result = (await RunSingleAsync(server, endpoint, ["responseDiffing"], "response-flaky")).Results.Single();
        if (result.Risk < 3)
        {
            throw new InvalidOperationException("expected changed response body to raise responseDiffing risk");
        }

        if (!result.Evidence.Contains("inconsistent repeated responses", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("body hash changed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected responseDiffing evidence to mention body hash change");
        }
    }

    private static Task<ResultsDocument> RunSingleAsync(LocalApiServer server, Endpoint endpoint, List<string> bendTypes, string projectId)
    {
        return new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = bendTypes,
            ParallelWorkers = 1
        }, CancellationToken.None);
    }
}
