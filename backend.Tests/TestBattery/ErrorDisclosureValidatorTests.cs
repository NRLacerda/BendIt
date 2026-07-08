using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class ErrorDisclosureValidatorTests
{
    public static async Task ErrorDisclosureValidatorAcceptsSanitizedValidationErrors()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/error-disclosure/safe-validation", "error-safe");
        if (result.Risk >= 5 || result.Interesting) throw new InvalidOperationException("expected sanitized validation errors to be low risk");
        if (!result.Evidence.Contains("without dangerous implementation disclosure", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected errorDisclosure evidence to describe sanitized validation behavior");
    }

    public static async Task ErrorDisclosureValidatorFlagsInternalFieldHints()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/error-disclosure/internal-fields", "error-internal-fields");
        if (result.Risk != 5 || !result.Interesting) throw new InvalidOperationException($"expected internal field hints to be medium risk, got {result.Risk}");
        if (!result.Evidence.Contains("internal field hint", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("tenantId", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected errorDisclosure evidence to mention internal field hints");
    }

    public static async Task ErrorDisclosureValidatorFlagsStackTraces()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/error-disclosure/stack-trace", "error-stack-trace");
        if (result.Risk < 9 || !result.Interesting) throw new InvalidOperationException("expected stack trace disclosure to be high risk");
        if (!result.Evidence.Contains("exception", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("stack trace", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected errorDisclosure evidence to mention exception and stack trace categories");
        if (!result.Mutation.TryGetValue("probes", out var probes) || probes is null) throw new InvalidOperationException("expected errorDisclosure mutation to include probe metadata");
    }

    public static async Task ErrorDisclosureValidatorFlagsDatabaseConnectionLeaks()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/error-disclosure/database-timeout", "error-database");
        if (result.Risk < 7 || !result.Interesting) throw new InvalidOperationException("expected database connection disclosure to be high risk");
        if (!result.Evidence.Contains("database or connection detail", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("connection pool", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected errorDisclosure evidence to mention database connection details");
        if (!result.OwaspCategory.StartsWith("API8:", StringComparison.Ordinal)) throw new InvalidOperationException("expected errorDisclosure to map to OWASP API8");
    }

    public static async Task ErrorDisclosureValidatorSkipsGetEndpoints()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_error_get", "/api/error-disclosure/stack-trace");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "error-get-skip",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["errorDisclosure"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        if (results.Results.Count != 0) throw new InvalidOperationException("expected errorDisclosure to be skipped for GET endpoints");
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
            BendTypes = ["errorDisclosure"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
