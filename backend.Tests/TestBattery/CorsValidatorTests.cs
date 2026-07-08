using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class CorsValidatorTests
{
    public static async Task CorsValidatorAcceptsMissingCorsHeaders()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/cors/no-headers", "cors-no-headers");
        if (result.Risk >= 5 || result.Interesting) throw new InvalidOperationException("expected missing CORS headers to be low risk");
        if (!result.Evidence.Contains("did not expose dangerous CORS policy", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected CORS evidence to describe safe behavior");
    }

    public static async Task CorsValidatorAcceptsStrictAllowlist()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/cors/allowlist", "cors-allowlist");
        if (result.Risk >= 5 || result.Interesting) throw new InvalidOperationException("expected strict allowlist CORS behavior to be low risk");
        if (!result.OwaspCategory.StartsWith("API8:", StringComparison.Ordinal)) throw new InvalidOperationException("expected corsAnalysis to map to OWASP API8");
    }

    public static async Task CorsValidatorFlagsWildcardOrigin()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/cors/wildcard", "cors-wildcard");
        if (result.Risk < 5 || !result.Interesting) throw new InvalidOperationException("expected wildcard CORS origin to be suspicious");
        if (!result.Evidence.Contains("wildcard origin allowed", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected CORS evidence to mention wildcard origin");
    }

    public static async Task CorsValidatorFlagsReflectedCredentialedOrigin()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/cors/reflected-credentials", "cors-reflected-credentials");
        if (result.Risk < 8 || !result.Interesting) throw new InvalidOperationException("expected reflected credentialed CORS origin to be high risk");
        if (!result.Evidence.Contains("reflected arbitrary origin", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("credentialed cross-origin access allowed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected CORS evidence to mention reflected origin and credentials");
        }
    }

    public static async Task CorsValidatorFlagsUnsafePreflight()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/cors/unsafe-preflight", "cors-unsafe-preflight");
        if (result.Risk < 8 || !result.Interesting) throw new InvalidOperationException("expected unsafe CORS preflight to be high risk");
        if (!result.Evidence.Contains("preflight", StringComparison.OrdinalIgnoreCase) ||
            !result.Evidence.Contains("sensitive request headers allowed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("expected CORS evidence to mention unsafe preflight headers");
        }
    }

    private static async Task<TestResult> RunSingleAsync(LocalApiServer server, string path, string projectId)
    {
        var endpoint = TestSupport.EndpointFor(server, "endpoint_" + projectId.Replace("-", "_"), path);
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = projectId,
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["corsAnalysis"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
