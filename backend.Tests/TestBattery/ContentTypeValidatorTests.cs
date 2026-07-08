using BendIt.Api.Models;
using BendIt.Api.TestBattery;
using BendIt.Api.Tests.Support;

namespace BendIt.Api.Tests.TestBattery;

internal static class ContentTypeValidatorTests
{
    public static async Task ContentTypeValidatorAcceptsSecureJsonOnlyEndpoint()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/content-type/strict", "content-type-strict");
        if (result.Risk >= 5 || result.Interesting) throw new InvalidOperationException("expected strict content-type enforcement to be low risk");
        if (!result.Evidence.Contains("text/plain JSON was rejected", StringComparison.OrdinalIgnoreCase) || !result.Evidence.Contains("missing Content-Type JSON was rejected", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected contentTypeValidation evidence to mention both invalid content-type rejections");
        if (!result.Mutation.TryGetValue("baselineStatus", out var baselineStatus) || Convert.ToInt32(baselineStatus) != 200 ||
            !result.Mutation.TryGetValue("textPlainStatus", out var textPlainStatus) || Convert.ToInt32(textPlainStatus) != 415 ||
            !result.Mutation.TryGetValue("missingContentTypeStatus", out var missingStatus) || Convert.ToInt32(missingStatus) != 415)
        {
            throw new InvalidOperationException("expected contentTypeValidation mutation to record baseline, text/plain, and missing content-type statuses");
        }
    }

    public static async Task ContentTypeValidatorFlagsTextPlainJsonAcceptance()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/content-type/text-plain-accepted", "content-type-text-plain");
        if (result.Risk < 5 || !result.Interesting) throw new InvalidOperationException("expected text/plain JSON acceptance to be a content-type finding");
        if (!result.Evidence.Contains("text/plain JSON was accepted", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected contentTypeValidation evidence to mention text/plain acceptance");
        if (!result.Request.HeadersMasked.TryGetValue("Content-Type", out var contentType) || contentType != "text/plain") throw new InvalidOperationException("expected representative contentTypeValidation request header to be text/plain");
        if (string.IsNullOrWhiteSpace(result.OwaspCategory) || !result.OwaspCategory.StartsWith("API8:", StringComparison.Ordinal)) throw new InvalidOperationException("expected contentTypeValidation to map to OWASP API8");
    }

    public static async Task ContentTypeValidatorFlagsMissingContentTypeAcceptance()
    {
        await using var server = await LocalApiServer.StartAsync();
        var result = await RunSingleAsync(server, "/api/content-type/missing-accepted", "content-type-missing");
        if (result.Risk < 5 || !result.Interesting) throw new InvalidOperationException("expected missing Content-Type JSON acceptance to be a content-type finding");
        if (!result.Evidence.Contains("missing Content-Type JSON was accepted", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("expected contentTypeValidation evidence to mention missing Content-Type acceptance");
        if (!result.Request.HeadersMasked.TryGetValue("Content-Type", out var contentType) || contentType != "<missing>") throw new InvalidOperationException("expected representative contentTypeValidation request header to show missing Content-Type");
        if (!result.Mutation.TryGetValue("missingContentTypeStatus", out var missingStatus) || Convert.ToInt32(missingStatus) != 200) throw new InvalidOperationException("expected contentTypeValidation mutation to record missing content-type acceptance status");
    }

    public static async Task ContentTypeValidatorSkipsGetEndpoints()
    {
        await using var server = await LocalApiServer.StartAsync();
        var endpoint = TestSupport.EndpointFor(server, "endpoint_content_type_get", "/api/content-type/strict");
        var results = await new TestBatteryRunner().RunAsync(new Project
        {
            ProjectId = "content-type-get-skip",
            BaseUrl = server.BaseUrl,
            Auth = new AuthConfig { Type = "none" }
        }, [endpoint], new TestRunRequest
        {
            BendTypes = ["contentTypeValidation"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        if (results.Results.Count != 0) throw new InvalidOperationException("expected contentTypeValidation to be skipped for GET endpoints");
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
            BendTypes = ["contentTypeValidation"],
            ParallelWorkers = 1
        }, CancellationToken.None);
        return results.Results.Single();
    }
}
