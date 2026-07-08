using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class SecurityValidatorTests
{
    public static async Task SecurityHeadersValidatorRecordsMisconfigurationEvidence()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_headers", "/api/users");
        var result = (await RunAsync(server, endpoint, ["securityHeaders"], "security-headers")).Results.Single();
        if (result.OwaspCategory != "API8: Security Misconfiguration") throw new InvalidOperationException($"expected securityHeaders to map to API8, got {result.OwaspCategory}");
        if (!result.Evidence.Contains("missing X-Content-Type-Options", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected securityHeaders evidence to mention missing X-Content-Type-Options");
        if (result.Result.HeadersMasked.Count == 0) throw new InvalidOperationException("expected securityHeaders to persist response headers");
    }

    public static async Task AuthBoundaryValidatorSendsMalformedJwtProbes()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_auth_boundary", "/api/users");
        endpoint.Classification = "protected";
        endpoint.Confidence = 80;
        endpoint.AuthRequired = true;
        var result = (await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "auth-boundary",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "jwt" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["jwtAnalysis"],
            ParallelWorkers = 1
        }, CancellationToken.None)).Results.Single();

        if (!result.Request.HeadersMasked.TryGetValue("Authorization", out var masked) || masked != "Bearer ***") throw new InvalidOperationException("expected jwtAnalysis to record masked Authorization header");
        if (!result.Evidence.Contains("malformed bearer token", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected jwtAnalysis evidence to mention malformed bearer token");
        if (result.Risk < 9 || !result.Interesting) throw new InvalidOperationException("expected protected endpoint accepting malformed JWT to be high risk");
    }

    public static async Task IdMutationDetectsQueryIdentifierParameters()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_query_id", "/api/users");
        endpoint.QueryParams = ["userId"];
        var result = (await RunAsync(server, endpoint, ["idMutation"], "query-id-mutation")).Results.SingleOrDefault()
            ?? throw new InvalidOperationException("expected idMutation to run for query identifier parameter");
        if (!result.Url.Contains("userId=2", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"expected mutated query identifier in URL, got {result.Url}");
        if (!result.OwaspCategory.StartsWith("API1:", StringComparison.Ordinal)) throw new InvalidOperationException("expected query idMutation to map to OWASP API1");
    }

    public static async Task InventoryValidatorFlagsLiveLegacyRoutes()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_legacy_inventory", "/api/v1/legacy/users");
        endpoint.Source = ["customApiList"];
        var result = (await RunAsync(server, endpoint, ["inventoryExposure"], "inventory-exposure")).Results.SingleOrDefault()
            ?? throw new InvalidOperationException("expected inventoryExposure to run for legacy route");
        if (!result.OwaspCategory.StartsWith("API9:", StringComparison.Ordinal)) throw new InvalidOperationException($"expected inventoryExposure to map to API9, got {result.OwaspCategory}");
        if (!result.Evidence.Contains("legacy route", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("versioned API route", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected inventoryExposure evidence to include legacy and versioned route signals");
        if (result.Risk < 7 || !result.Interesting) throw new InvalidOperationException("expected live legacy route to be a finding");
    }

    public static async Task SensitiveDataValidatorReportsDataClassesWithoutValues()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_sensitive_profile", "/api/profile");
        var result = (await RunAsync(server, endpoint, ["sensitiveDataExposure"], "sensitive-data")).Results.Single();
        if (!result.OwaspCategory.StartsWith("API3:", StringComparison.Ordinal)) throw new InvalidOperationException($"expected sensitiveDataExposure to map to API3, got {result.OwaspCategory}");
        if (!result.Evidence.Contains("credential-shaped response field", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("email address", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected sensitiveDataExposure evidence to report sensitive data classes");
        if (result.Evidence.Contains("secret-token-value", StringComparison.OrdinalIgnoreCase) || result.Evidence.Contains("alice@example.com", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected sensitiveDataExposure evidence to avoid copying sensitive values");
        if (!result.SensitiveDataDetected || result.Risk < 8) throw new InvalidOperationException("expected credential-shaped exposure to be high risk and marked sensitive");
    }

    private static Task<ResultsDocument> RunAsync(LocalApiServer server, Endpoint endpoint, List<string> bendTypes, string projectId)
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
